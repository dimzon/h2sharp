/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * Query.query(limit, target): streaming rows, materialized fallback and CTAS/INSERT SELECT. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using H2Sharp.Command;
using H2Sharp.Command.Query;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class QueryTargetOracleTests
{
    [Fact]
    public void FlatQueriesStreamIntoTheTargetAndReturnNoResult()
    {
        SessionLocal session = NewSession("query_target_flat");
        try
        {
            Execute(session, "CREATE TABLE T(ID INT, NAME VARCHAR(20))");
            Execute(session, "INSERT INTO T VALUES(1,'a'),(2,'b'),(3,'c')");
            CollectingTarget target = new();
            CommandContainer container = Assert.IsType<CommandContainer>(
                session.PrepareCommand("SELECT ID, NAME FROM T WHERE ID > 1"));
            Assert.Null(((H2Sharp.Command.Query.Query)container.GetPreparedCommand()).Run(0, target));
            Assert.Equal(["2|b", "3|c"], target.Rows);
            Assert.Equal(2, target.GetRowCount());
        }
        finally { session.Close(); }
    }

    [Fact]
    public void OrderingDistinctGroupingAndLimitsStillReachTheTargetInOrder()
    {
        SessionManager(out SessionLocal session, out Database database, "query_target_shapes");
        _ = database;
        try
        {
            Execute(session, "CREATE TABLE T(ID INT, V INT)");
            Execute(session, "INSERT INTO T VALUES(1,10),(2,10),(3,20),(4,10)");
            AssertTarget(session, "SELECT ID FROM T ORDER BY ID DESC", ["4", "3", "2", "1"]);
            AssertTarget(session, "SELECT DISTINCT V FROM T ORDER BY V DESC", ["20", "10"]);
            AssertTarget(session, "SELECT V, COUNT(*) FROM T GROUP BY V ORDER BY V", ["10|3", "20|1"]);
            AssertTarget(session, "SELECT ID FROM T ORDER BY ID LIMIT 2 OFFSET 1", ["2", "3"]);
            AssertTarget(session, "SELECT ID FROM T ORDER BY ID FETCH FIRST 2 ROWS ONLY", ["1", "2"]);
        }
        finally { session.Close(); }
    }

    [JavaOracleFact]
    public void CreateTableAsSelectMatchesTheReference()
    {
        Check([
            "CREATE TABLE SRC(ID INT, NAME VARCHAR(20), V INT)",
            "INSERT INTO SRC VALUES(1,'a',10),(2,'b',20),(3,'c',10)",
            "CREATE TABLE COPY AS SELECT ID, NAME FROM SRC WHERE ID > 1",
            "SELECT * FROM COPY ORDER BY ID",
            "CREATE TABLE NAMED(X, Y) AS SELECT ID, V*2 FROM SRC ORDER BY ID",
            "SELECT * FROM NAMED ORDER BY X",
            "CREATE TABLE EMPTY AS SELECT * FROM SRC WITH NO DATA",
            "SELECT COUNT(*) FROM EMPTY",
            "CREATE TABLE AGG AS SELECT V, COUNT(*) C FROM SRC GROUP BY V ORDER BY V",
            "SELECT * FROM AGG",
            "CREATE TABLE LIMITED AS SELECT ID FROM SRC ORDER BY ID DESC FETCH FIRST 1 ROW ONLY",
            "SELECT * FROM LIMITED",
            "INSERT INTO COPY SELECT ID+10, NAME FROM SRC WHERE V=10",
            "SELECT * FROM COPY ORDER BY ID",
            "CREATE TABLE BAD(A, B, C) AS SELECT ID FROM SRC",
            "CREATE TABLE SRC AS SELECT 1",
        ], allowErrors: true);
    }

    [JavaOracleFact]
    public void InsertFromSelectStreamsTheSameRowsAsTheReference()
    {
        Check([
            "CREATE TABLE T(ID INT, V INT)",
            "CREATE TABLE SRC(ID INT, V INT)",
            "INSERT INTO SRC VALUES(1,10),(2,20),(3,30)",
            "INSERT INTO T SELECT * FROM SRC WHERE ID > 1",
            "INSERT INTO T(ID) SELECT ID+10 FROM SRC WHERE V < 30",
            "INSERT INTO T SELECT ID, V FROM SRC ORDER BY ID DESC LIMIT 1",
            "SELECT * FROM T ORDER BY ID",
            "INSERT INTO T SELECT * FROM T",
            "SELECT COUNT(*) FROM T",
        ], allowErrors: true);
    }

    private static void AssertTarget(SessionLocal session, string sql, string[] expected)
    {
        CollectingTarget target = new();
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        Assert.Null(((H2Sharp.Command.Query.Query)container.GetPreparedCommand()).Run(0, target));
        Assert.Equal(expected, target.Rows);
    }

    private static SessionLocal NewSession(string name)
    {
        SessionManager(out SessionLocal session, out Database database, name);
        _ = database;
        return session;
    }

    private static void SessionManager(out SessionLocal session, out Database database, string name)
    {
        database = new Database(new ConnectionInfo("jdbc:h2:mem:" + name));
        session = new SessionLocal(database, "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

    private static void Execute(SessionLocal session, string sql)
        => session.PrepareCommand(sql).ExecuteUpdate(false);

    private static void Check(string[] statements, bool allowErrors = false)
    {
        List<string> port = [];
        using H2Connection connection = new("jdbc:h2:mem:query_target_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        for (int i = 0; i < statements.Length; i++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statements[i];
            try
            {
                if (statements[i].StartsWith("SELECT", StringComparison.Ordinal))
                {
                    using DbDataReader reader = command.ExecuteReader();
                    port.Add($"={i}:C:{reader.FieldCount}");
                    while (reader.Read())
                    {
                        List<string> cells = [];
                        for (int c = 0; c < reader.FieldCount; c++)
                            cells.Add(reader.IsDBNull(c) ? "NULL"
                                : Convert.ToString(reader.GetValue(c), CultureInfo.InvariantCulture)!);
                        port.Add($"={i}:R:" + string.Join('|', cells));
                    }
                }
                else port.Add($"={i}:U:{command.ExecuteNonQuery()}");
            }
            catch (H2SharpException e) { port.Add($"={i}:E:{e.H2ErrorCode}"); }
        }
        List<string> java = OracleRunner.Run(statements, """
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:query_target")) {
              java.sql.Statement s=c.createStatement();
              for(int i=0;i<inputs.length;i++) {
                try {
                  if(!s.execute(inputs[i])) { System.out.println("="+i+":U:"+s.getUpdateCount()); continue; }
                  java.sql.ResultSet r=s.getResultSet(); int count=r.getMetaData().getColumnCount();
                  System.out.println("="+i+":C:"+count);
                  while(r.next()) {
                    StringBuilder b=new StringBuilder("="+i+":R:");
                    for(int col=1;col<=count;col++) { if(col>1)b.append('|'); String v=r.getString(col); b.append(v==null?"NULL":v); }
                    System.out.println(b);
                  }
                } catch(java.sql.SQLException e) { System.out.println("="+i+":E:"+e.getErrorCode()); }
              }
            }
            """, extraJavaArgs: ["-Duser.language=en"]);
        Assert.True(java.SequenceEqual(port), "sql:\n" + string.Join("\n", statements)
            + "\njava:\n" + string.Join("\n", java) + "\nport:\n" + string.Join("\n", port));
        if (!allowErrors) Assert.DoesNotContain(port, line => line.Contains(":E:", StringComparison.Ordinal));
    }

    private sealed class CollectingTarget : ResultTarget
    {
        internal List<string> Rows { get; } = [];
        private long count;
        public void AddRow(params Value?[] values)
        {
            Rows.Add(string.Join('|', values.Select(v => v is null or ValueNull ? "NULL" : v.GetString())));
            count++;
        }
        public long GetRowCount() => count;
        public void LimitsWereApplied() { }
    }
}
