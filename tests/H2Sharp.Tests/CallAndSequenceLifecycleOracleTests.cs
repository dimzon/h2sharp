/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * Native sequence blockers: CALL, search path, formatter and persistent next values. */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using H2Sharp.Command;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class CallAndSequenceLifecycleOracleTests
{
    [JavaOracleFact]
    public void ScalarAndTableCallsMatchTheReference()
    {
        Check([
            "CALL 1+2", "CALL CAST(1 AS REAL)", "CALL NULL", "CALL ROWNUM()",
            "CALL ARRAY[1,2]", "CALL TABLE(X INT=ARRAY[1,2],Y VARCHAR=ARRAY['a','b'])",
            "CREATE SEQUENCE S CACHE 3", "CALL CURRENT VALUE FOR S", "CALL NEXT VALUE FOR S",
            "CALL CURRENT VALUE FOR S", "CALL NEXT VALUE FOR S", "CALL CURRENT VALUE FOR S",
        ]);
    }

    [JavaOracleFact]
    public void SequenceLookupRespectsCurrentSchemaSearchPathAndQualification()
    {
        Check([
            "CREATE SCHEMA A", "CREATE SCHEMA B", "CREATE SEQUENCE A.S START WITH 10", "CREATE SEQUENCE B.S START WITH 20",
            "SET SCHEMA_SEARCH_PATH A,B", "CALL NEXT VALUE FOR S", "CALL NEXT VALUE FOR B.S",
            "SET SCHEMA B", "CALL NEXT VALUE FOR S", "CALL NEXT VALUE FOR A.S", "CALL NEXT VALUE FOR PUBLIC.S",
        ]);
    }

    [JavaOracleFact]
    public void CallUpdateAndQueryMetadataMatchTheReference()
    {
        SessionLocal session = new(new Database(new ConnectionInfo("jdbc:h2:mem:call_update_oracle")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = Values.CompareMode.GetInstance(null, 0),
        };
        List<string> port = [];
        try
        {
            foreach (string sql in new[] { "CALL 7", "CALL NULL", "CALL CAST(2 AS NUMERIC(10,2))" })
            {
                // Test H2 Call.update itself. ADO.NET ExecuteNonQuery on a result-set
                // command follows its established -1 convention, unlike JDBC executeUpdate.
                CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
                Prepared prepared = container.GetPreparedCommand();
                port.Add("=" + prepared.Update());
                using ResultInterface meta = prepared.QueryMeta()!;
                port.Add("=" + meta.GetVisibleColumnCount() + ":" + meta.GetColumnType(0).GetDeclaredTypeName());
            }
        }
        finally { session.Close(); }
        List<string> java = OracleRunner.Run([], """
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:call_update_oracle")) {
              java.sql.Statement s=c.createStatement();
              for(String sql:new String[]{"CALL 7","CALL NULL","CALL CAST(2 AS NUMERIC(10,2))"}) {
                System.out.println("="+s.executeUpdate(sql));
                java.sql.ResultSet r=s.executeQuery(sql);
                System.out.println("="+r.getMetaData().getColumnCount()+":"+r.getMetaData().getColumnTypeName(1));
              }
            }
            """);
        Assert.Equal(java, port);
    }

    [JavaOracleFact]
    public void SequenceCacheAndNextValueSurviveCloseRollbackAndJavaRead()
    {
        string path = Path.Combine(Path.GetTempPath(), "h2_sequence_lifecycle_" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
        try
        {
            using (H2Connection connection = new("jdbc:h2:file:" + path))
            {
                connection.Open();
                Execute(connection, "CREATE SEQUENCE S AS INTEGER START WITH 5 INCREMENT BY 2 CACHE 3");
                using DbCommand command = connection.CreateCommand();
                command.CommandText = "CALL NEXT VALUE FOR S";
                Assert.Equal(5, command.ExecuteScalar());
                using DbTransaction transaction = connection.BeginTransaction();
                command.Transaction = transaction;
                Assert.Equal(7, command.ExecuteScalar());
                transaction.Rollback();
            }
            using (H2Connection connection = new("jdbc:h2:file:" + path))
            {
                connection.Open();
                using DbCommand command = connection.CreateCommand();
                command.CommandText = "CALL NEXT VALUE FOR S";
                Assert.Equal(9, command.ExecuteScalar());
                command.CommandText = "SELECT CACHE,DATA_TYPE FROM INFORMATION_SCHEMA.SEQUENCES WHERE SEQUENCE_NAME='S'";
                using DbDataReader reader = command.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal(3L, reader.GetInt64(0));
                Assert.Equal("INTEGER", reader.GetString(1));
            }
            List<string> java = OracleRunner.Run([path], """
                try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:file:"+inputs[0])) {
                  java.sql.Statement s=c.createStatement();
                  java.sql.ResultSet r=s.executeQuery("CALL NEXT VALUE FOR S"); r.next(); System.out.println("="+r.getInt(1));
                  r=s.executeQuery("SELECT CACHE,DATA_TYPE FROM INFORMATION_SCHEMA.SEQUENCES WHERE SEQUENCE_NAME='S'");
                  r.next(); System.out.println("="+r.getLong(1)+":"+r.getString(2));
                }
                """, extraJavaArgs: ["-Duser.language=en"]);
            Assert.Equal(["=11", "=3:INTEGER"], java);
        }
        finally
        {
            foreach (string suffix in new[] { ".mv.db", ".lock.db", ".trace.db" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [JavaOracleFact]
    public void CreateSqlPreservesBoundsCycleCacheAndRestart()
    {
        Check([
            "CREATE SEQUENCE S AS SMALLINT START WITH 5 INCREMENT BY 2 MINVALUE 3 MAXVALUE 9 CYCLE CACHE 2",
            "CALL DB_OBJECT_SQL('SEQUENCE','PUBLIC','S')", "CALL NEXT VALUE FOR S", "CALL DB_OBJECT_SQL('SEQUENCE','PUBLIC','S')",
            "CREATE SEQUENCE E MINVALUE 1 MAXVALUE 2 NO CACHE", "CALL NEXT VALUE FOR E", "CALL NEXT VALUE FOR E",
            "CALL DB_OBJECT_SQL('SEQUENCE','PUBLIC','E')",
        ]);
    }

    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Check(string[] statements)
    {
        List<string> port = [];
        using H2Connection connection = new("jdbc:h2:mem:call_sequence_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        for (int i = 0; i < statements.Length; i++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statements[i];
            try
            {
                if (statements[i].StartsWith("CALL", StringComparison.Ordinal))
                {
                    using DbDataReader reader = command.ExecuteReader();
                    port.Add($"={i}:C:{reader.FieldCount}");
                    while (reader.Read())
                    {
                        List<string> cells = [];
                        for (int j = 0; j < reader.FieldCount; j++) cells.Add(reader.IsDBNull(j) ? "NULL" : reader.GetString(j));
                        port.Add($"={i}:R:" + string.Join('|', cells));
                    }
                }
                else port.Add($"={i}:U:{command.ExecuteNonQuery()}");
            }
            catch (H2SharpException e) { port.Add($"={i}:E:{e.H2ErrorCode}"); }
        }
        List<string> java = OracleRunner.Run(statements, """
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:call_sequence")) {
              java.sql.Statement s=c.createStatement();
              for(int i=0;i<inputs.length;i++) {
                try {
                  if(!s.execute(inputs[i])) { System.out.println("="+i+":U:"+s.getUpdateCount()); continue; }
                  java.sql.ResultSet r=s.getResultSet(); int count=r.getMetaData().getColumnCount();
                  System.out.println("="+i+":C:"+count);
                  while(r.next()) {
                    StringBuilder b=new StringBuilder("="+i+":R:");
                    for(int j=1;j<=count;j++) { if(j>1)b.append('|'); String v=r.getString(j); b.append(v==null?"NULL":v); }
                    System.out.println(b);
                  }
                } catch(java.sql.SQLException e) { System.out.println("="+i+":E:"+e.getErrorCode()); }
              }
            }
            """, extraJavaArgs: ["-Duser.language=en"]);
        Assert.True(java.SequenceEqual(port), "java:\n" + string.Join("\n", java) + "\nport:\n" + string.Join("\n", port));
    }
}
