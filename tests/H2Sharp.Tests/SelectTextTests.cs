/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What a flat SELECT text answers, checked against the reference running the same text.
 *
 * Both sides read the same statement: this side parses it into the engine operator and runs
 * it over a registered table, the reference runs it through JDBC over the same rows created
 * by the same DDL. No operator is hand-built on either side, so a row that differs means the
 * text took a different road, not that two constructions disagreed.
 *
 * Identifiers are written in the case the columns were created with: the text path does not
 * fold case yet, and a lowercase name that the reference folds to upper would resolve there
 * and not here. Refusals are compared by error code where the reference refuses too, and
 * pinned as not-ported where only this side refuses.
 */
using System.Collections.Generic;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using H2Sharp.MvStore.Tx;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SelectTextTests
{
    private static SessionLocal NewSession(out TransactionStore store, out Schemas.Schema schema)
    {
        SessionLocal session = new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:select_text")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };

        store = new TransactionStore(MVStore.Open(null!));
        store.Init();

        schema = new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false);
        session.GetDatabase().AddSchema(schema);

        Column id = new("ID", TypeInfo.TypeInteger, null, 0);
        id.SetNullable(false);
        Column name = new("NAME", TypeInfo.TypeVarchar, null, 1);
        MVTable table = new(session.GetDatabase(), schema, 0, "T", [id, name], store, persistData: false);
        foreach (Column column in table.GetColumns())
        {
            column.SetTable(table, column.GetColumnId());
        }

        table.AddIndex(session, "I", 7, [new IndexColumn(id, ascending: true)], 1,
            IndexType.CreatePrimaryKey(persistent: false, hash: false), create: true, indexComment: null);
        table.AddRow(session, new DefaultRow([ValueInteger.Get(3), ValueVarchar.Get("c")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(1), ValueVarchar.Get("b")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(2), ValueVarchar.Get("b")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(4), ValueNull.Instance]));
        schema.Add(table);
        return session;
    }

    private static List<string> RunText(SessionLocal session, string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        ResultInterface result = container.Query(-1);
        List<string> rows = [];
        while (result.Next())
        {
            Value?[] row = result.CurrentRow();
            List<string> cells = [];
            foreach (Value? value in row)
            {
                cells.Add(value is null or ValueNull ? "-" : value.GetString() ?? "-");
            }

            rows.Add("=" + string.Join(":", cells));
        }

        return rows;
    }

    private static List<string> RunJava(string dbName, string sql)
    {
        string setup = """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:{DB}");
            java.sql.Statement stat = conn.createStatement();
            stat.execute("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR)");
            stat.execute("INSERT INTO T VALUES (3, 'c'), (1, 'b'), (2, 'b'), (4, NULL)");
            """.Replace("{DB}", dbName);
        string read = """
            java.sql.ResultSet rs = stat.executeQuery(inputs[0]);
            int columns = rs.getMetaData().getColumnCount();
            while (rs.next()) {
                StringBuilder line = new StringBuilder("=");
                for (int i = 1; i <= columns; i++) {
                    if (i > 1) {
                        line.append(':');
                    }
                    String s = rs.getString(i);
                    line.append(rs.wasNull() ? "-" : s);
                }
                System.out.println(line);
            }
            conn.close();
            """;
        return OracleRunner.Run(
            [sql],
            setup + read,
            numbers: null,
            extraJavaArgs: ["-Duser.language=en"]);
    }

    private static void CompareText(string dbName, string sql, List<string>? expected = null)
    {
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            List<string> actual = RunText(session, sql);
            List<string> java = RunJava(dbName, sql);
            if (expected is not null)
            {
                Assert.Equal(expected, actual);
            }

            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void StarSelectReadsAllColumns()
    {
        CompareText("select_text_star", "SELECT * FROM T", ["=1:b", "=2:b", "=3:c", "=4:-"]);
    }

    [JavaOracleFact]
    public void ColumnListInWrittenOrder()
    {
        CompareText("select_text_cols", "SELECT NAME, ID FROM T", ["=b:1", "=b:2", "=c:3", "=-:4"]);
    }

    [JavaOracleFact]
    public void WhereFiltersRows()
    {
        CompareText("select_text_where", "SELECT ID, NAME FROM T WHERE ID > 1", ["=2:b", "=3:c", "=4:-"]);
    }

    [JavaOracleFact]
    public void WhereAndIsNull()
    {
        CompareText("select_text_and", "SELECT ID FROM T WHERE ID > 1 AND NAME IS NOT NULL", ["=2", "=3"]);
    }

    [JavaOracleFact]
    public void OrderByNameDescendingWithPositionalTiebreak()
    {
        CompareText(
            "select_text_order",
            "SELECT ID, NAME FROM T ORDER BY NAME DESC, 1",
            ["=3:c", "=1:b", "=2:b", "=4:-"]);
    }

    [JavaOracleFact]
    public void DistinctNameCollapsesDuplicates()
    {
        CompareText("select_text_distinct", "SELECT DISTINCT NAME FROM T", null);
    }

    [JavaOracleFact]
    public void AliasOrderResolves()
    {
        // The reference sorts this fully: the NAME order matches no index, and with the
        // default LOW null ordering the null row comes first on ASC. A hand-built select
        // that never runs init answers the ID index order instead, on both sides.
        CompareText(
            "select_text_alias",
            "SELECT ID AS I, NAME AS N FROM T ORDER BY N",
            ["=4:-", "=1:b", "=2:b", "=3:c"]);
    }

    [JavaOracleFact]
    public void TableAliasAndQualifiedNames()
    {
        CompareText(
            "select_text_qual",
            "SELECT X.ID FROM T X WHERE X.ID > 2 ORDER BY X.ID DESC",
            ["=4", "=3"]);
    }

    [JavaOracleFact]
    public void LimitReadsFirstRows()
    {
        CompareText("select_text_limit", "SELECT ID FROM T ORDER BY ID LIMIT 2", ["=1", "=2"]);
    }

    [JavaOracleFact]
    public void LimitWithCommaOffset()
    {
        CompareText("select_text_limoff", "SELECT ID FROM T ORDER BY ID LIMIT 1, 2", ["=2", "=3"]);
    }

    [JavaOracleFact]
    public void LimitWithOffsetWord()
    {
        CompareText(
            "select_text_limoffw", "SELECT ID FROM T ORDER BY ID LIMIT 2 OFFSET 1", ["=2", "=3"]);
    }

    [JavaOracleFact]
    public void OffsetAloneSkipsRows()
    {
        CompareText("select_text_offset", "SELECT ID FROM T ORDER BY ID OFFSET 2", ["=3", "=4"]);
    }

    [JavaOracleFact]
    public void OffsetWithRowWord()
    {
        CompareText("select_text_offsetr", "SELECT ID FROM T ORDER BY ID OFFSET 2 ROW", ["=3", "=4"]);
    }

    [JavaOracleFact]
    public void FetchFirstRowsOnly()
    {
        CompareText(
            "select_text_fetch", "SELECT ID FROM T ORDER BY ID FETCH FIRST 2 ROWS ONLY", ["=1", "=2"]);
    }

    [JavaOracleFact]
    public void FetchSingleRow()
    {
        CompareText(
            "select_text_fetch1", "SELECT ID FROM T ORDER BY ID FETCH FIRST ROW ONLY", ["=1"]);
    }

    [JavaOracleFact]
    public void LimitZeroReadsNothing()
    {
        CompareText("select_text_limit0", "SELECT ID FROM T ORDER BY ID LIMIT 0", []);
    }

    [Fact]
    public void FetchWithTiesWaitsForTies()
    {
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            TODOException e = Assert.Throws<TODOException>(
                () => session.PrepareCommand("SELECT ID FROM T ORDER BY ID FETCH FIRST 2 ROWS WITH TIES"));
            Assert.Contains("WITH TIES", e.Message);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void FetchPercentWaitsForPercent()
    {
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            TODOException e = Assert.Throws<TODOException>(
                () => session.PrepareCommand("SELECT ID FROM T ORDER BY ID FETCH FIRST 50 PERCENT ROWS ONLY"));
            Assert.Contains("PERCENT", e.Message);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void LimitExpressionWaitsForExpressions()
    {
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            TODOException e = Assert.Throws<TODOException>(
                () => session.PrepareCommand("SELECT ID FROM T ORDER BY ID LIMIT ?"));
            Assert.Contains("integer literal", e.Message);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void UnknownTableIsRefusedOnBothSides()
    {
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            H2SharpException csharp = Assert.Throws<H2SharpException>(
                () => session.PrepareCommand("SELECT ID FROM NOPE"));

            List<string> java = OracleRunner.Run(
                [],
                """
                java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:select_text_nope");
                try {
                    java.sql.Statement stat = conn.createStatement();
                    stat.execute("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR)");
                    stat.executeQuery("SELECT ID FROM NOPE");
                    System.out.println("OK");
                } catch (java.sql.SQLException e) {
                    System.out.println("ERR " + e.getErrorCode());
                }
                conn.close();
                """,
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"]);
            Assert.Equal(["ERR " + csharp.H2ErrorCode], java);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void GroupByWaitsForGrouping()
    {
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            TODOException e = Assert.Throws<TODOException>(
                () => session.PrepareCommand("SELECT ID FROM T GROUP BY ID"));
            Assert.Contains("not ported", e.Message);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void CountStarWaitsForAggregates()
    {
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            TODOException e = Assert.Throws<TODOException>(
                () => session.PrepareCommand("SELECT COUNT(*) FROM T"));
            Assert.Contains("not ported", e.Message);
        }
        finally
        {
            store.Dispose();
        }
    }
}
