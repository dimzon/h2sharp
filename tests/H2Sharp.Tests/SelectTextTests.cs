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
        => NewSeededSession(out store, out schema, seed: true);

    private static SessionLocal NewEmptySession(out TransactionStore store)
        => NewSeededSession(out store, out _, seed: false);

    private static void CompareDup(string dbName, string sql, List<string>? expected = null)
    {
        const string Setup = """
            stat.execute("CREATE TABLE T3(ID INT PRIMARY KEY, VAL INT)");
            stat.execute("INSERT INTO T3 VALUES (1, 5), (2, 5), (3, 7), (4, NULL)");
            """;
        SessionLocal session = NewDupSession(out TransactionStore store);
        try
        {
            List<string> actual = RunText(session, sql);
            List<string> java = RunJavaWithSetup(dbName, sql, Setup);
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

    private static SessionLocal NewDupSession(out TransactionStore store)
    {
        SessionLocal session = new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:select_dup")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };

        store = new TransactionStore(MVStore.Open(null!));
        store.Init();

        Schemas.Schema schema = new(session.GetDatabase(), 1, "PUBLIC", false);
        session.GetDatabase().AddSchema(schema);

        Column id = new("ID", TypeInfo.TypeInteger, null, 0);
        id.SetNullable(false);
        Column val = new("VAL", TypeInfo.TypeInteger, null, 1);
        MVTable table = new(session.GetDatabase(), schema, 0, "T3", [id, val], store, persistData: false);
        foreach (Column column in table.GetColumns())
        {
            column.SetTable(table, column.GetColumnId());
        }

        table.AddIndex(session, "I", 7, [new IndexColumn(id, ascending: true)], 1,
            IndexType.CreatePrimaryKey(persistent: false, hash: false), create: true, indexComment: null);
        table.AddRow(session, new DefaultRow([ValueInteger.Get(1), ValueInteger.Get(5)]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(2), ValueInteger.Get(5)]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(3), ValueInteger.Get(7)]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(4), ValueNull.Instance]));
        schema.Add(table);
        return session;
    }

    private static SessionLocal NewLongSession(out TransactionStore store)
    {
        SessionLocal session = new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:select_long")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };

        store = new TransactionStore(MVStore.Open(null!));
        store.Init();

        Schemas.Schema schema = new(session.GetDatabase(), 1, "PUBLIC", false);
        session.GetDatabase().AddSchema(schema);

        Column id = new("ID", TypeInfo.TypeInteger, null, 0);
        id.SetNullable(false);
        Column bigval = new("BIGVAL", TypeInfo.TypeBigint, null, 1);
        MVTable table = new(session.GetDatabase(), schema, 0, "T2", [id, bigval], store, persistData: false);
        foreach (Column column in table.GetColumns())
        {
            column.SetTable(table, column.GetColumnId());
        }

        table.AddIndex(session, "I", 7, [new IndexColumn(id, ascending: true)], 1,
            IndexType.CreatePrimaryKey(persistent: false, hash: false), create: true, indexComment: null);
        table.AddRow(session, new DefaultRow([ValueInteger.Get(1), ValueBigint.Get(9223372036854775800L)]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(2), ValueBigint.Get(-5)]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(3), ValueNull.Instance]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(4), ValueBigint.Get(7)]));
        schema.Add(table);
        return session;
    }

    private static SessionLocal NewSeededSession(out TransactionStore store, out Schemas.Schema schema, bool seed)
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
        if (seed)
        {
            table.AddRow(session, new DefaultRow([ValueInteger.Get(3), ValueVarchar.Get("c")]));
            table.AddRow(session, new DefaultRow([ValueInteger.Get(1), ValueVarchar.Get("b")]));
            table.AddRow(session, new DefaultRow([ValueInteger.Get(2), ValueVarchar.Get("b")]));
            table.AddRow(session, new DefaultRow([ValueInteger.Get(4), ValueNull.Instance]));
        }

        schema.Add(table);
        return session;
    }

    private static List<string> RunText(SessionLocal session, string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        ResultInterface result = container.Query(-1);
        // Like JDBC's getColumnCount loop: hidden ORDER BY, HAVING and GROUP BY columns
        // ride along inside the rows for the sort and the grouping, but only the visible
        // ones are the answer.
        int visible = result.GetVisibleColumnCount();
        List<string> rows = [];
        while (result.Next())
        {
            Value?[] row = result.CurrentRow();
            List<string> cells = [];
            for (int i = 0; i < visible; i++)
            {
                Value? value = row[i];
                cells.Add(value is null or ValueNull ? "-" : value.GetString() ?? "-");
            }

            rows.Add("=" + string.Join(":", cells));
        }

        return rows;
    }

    private static List<string> RunJava(string dbName, string sql)
        => RunJavaWithSetup(dbName, sql, """
            stat.execute("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR)");
            stat.execute("INSERT INTO T VALUES (3, 'c'), (1, 'b'), (2, 'b'), (4, NULL)");
            """);

    private static List<string> RunJavaWithSetup(string dbName, string sql, string setup)
    {
        string head = """
            java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:{DB}");
            java.sql.Statement stat = conn.createStatement();
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
            head + setup + read,
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

    private static void CompareDual(string dbName, string sql, List<string>? expected = null)
    {
        SessionLocal session = new(
            new Database(new ConnectionInfo("jdbc:h2:mem:select_dual")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        List<string> actual = RunText(session, sql);
        List<string> java = RunJavaWithSetup(dbName, sql, string.Empty);
        if (expected is not null)
        {
            Assert.Equal(expected, actual);
        }

        Assert.Equal(java, actual);
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

    [JavaOracleFact]
    public void CountStarCountsRows()
    {
        CompareText("select_text_count", "SELECT COUNT(*) FROM T", ["=4"]);
    }

    [JavaOracleFact]
    public void CountNotNullColumnCountsRows()
    {
        // ID is NOT NULL, so every row counts: the reference answers this without walking
        // rows, and so does this side.
        CompareText("select_text_countcol", "SELECT COUNT(ID) FROM T", ["=4"]);
    }

    [JavaOracleFact]
    public void CountNullableColumnCountsNonNull()
    {
        // NAME is nullable: the row count is not the answer, the grouped walk is.
        CompareText("select_text_countnull", "SELECT COUNT(NAME) FROM T", ["=3"]);
    }

    [JavaOracleFact]
    public void CountFilteredRows()
    {
        CompareText("select_text_countwhere", "SELECT COUNT(*) FROM T WHERE ID > 1", ["=3"]);
    }

    [JavaOracleFact]
    public void GroupByNameCounts()
    {
        CompareText("select_text_group", "SELECT NAME, COUNT(*) FROM T GROUP BY NAME", ["=-:1", "=b:2", "=c:1"]);
    }

    [JavaOracleFact]
    public void GroupByIdCounts()
    {
        CompareText("select_text_groupid", "SELECT ID, COUNT(*) FROM T GROUP BY ID", ["=1:1", "=2:1", "=3:1", "=4:1"]);
    }

    [JavaOracleFact]
    public void GroupByWithWhere()
    {
        CompareText(
            "select_text_groupwhere",
            "SELECT NAME, COUNT(*) FROM T WHERE ID > 1 GROUP BY NAME",
            ["=-:1", "=b:1", "=c:1"]);
    }

    [JavaOracleFact]
    public void GroupByHaving()
    {
        CompareText(
            "select_text_grouphaving",
            "SELECT NAME, COUNT(*) FROM T GROUP BY NAME HAVING COUNT(*) > 1",
            ["=b:2"]);
    }

    [JavaOracleFact]
    public void GroupByOrderBy()
    {
        CompareText(
            "select_text_grouporder",
            "SELECT NAME, COUNT(*) FROM T GROUP BY NAME ORDER BY NAME DESC",
            ["=c:1", "=b:2", "=-:1"]);
    }

    [JavaOracleFact]
    public void SumIds()
    {
        CompareText("select_text_sum", "SELECT SUM(ID) FROM T", ["=10"]);
    }

    [JavaOracleFact]
    public void MinMaxName()
    {
        CompareText("select_text_minmax", "SELECT MIN(NAME), MAX(NAME) FROM T", ["=b:c"]);
    }

    [JavaOracleFact]
    public void MinMaxId()
    {
        CompareText("select_text_minmaxid", "SELECT MIN(ID), MAX(ID) FROM T", ["=1:4"]);
    }

    [JavaOracleFact]
    public void SumGroupBy()
    {
        CompareText(
            "select_text_sumgroup", "SELECT NAME, SUM(ID) FROM T GROUP BY NAME", ["=-:4", "=b:3", "=c:3"]);
    }

    [Fact]
    public void SumWrongTypeIsRefused()
    {
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            H2SharpException e = Assert.Throws<H2SharpException>(
                () => session.PrepareCommand("SELECT SUM(NAME) FROM T"));
            Assert.Equal(90015, e.H2ErrorCode);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void SumDistinct()
    {
        CompareText("select_text_sumdistinct", "SELECT SUM(DISTINCT ID) FROM T", ["=10"]);
    }

    [JavaOracleFact]
    public void AvgDistinct()
    {
        CompareText("select_text_avgdistinct", "SELECT AVG(DISTINCT ID) FROM T", ["=2.5"]);
    }

    [JavaOracleFact]
    public void CountDistinctName()
    {
        CompareText("select_text_countdistinct", "SELECT COUNT(DISTINCT NAME) FROM T", ["=2"]);
    }

    [JavaOracleFact]
    public void MinMaxDistinct()
    {
        CompareText(
            "select_text_minmaxdistinct", "SELECT MIN(DISTINCT ID), MAX(DISTINCT NAME) FROM T", ["=1:c"]);
    }

    [JavaOracleFact]
    public void GroupedCountDistinct()
    {
        CompareText(
            "select_text_groupcountdistinct",
            "SELECT NAME, COUNT(DISTINCT ID) FROM T GROUP BY NAME",
            ["=-:1", "=b:2", "=c:1"]);
    }

    [JavaOracleFact]
    public void SumDistinctDedups()
    {
        CompareDup("select_text_sumdistinctdup", "SELECT SUM(DISTINCT VAL) FROM T3", ["=12"]);
    }

    [JavaOracleFact]
    public void CountDistinctDedups()
    {
        CompareDup("select_text_countdistinctdup", "SELECT COUNT(DISTINCT VAL) FROM T3", ["=2"]);
    }

    [JavaOracleFact]
    public void AvgDistinctDedups()
    {
        CompareDup("select_text_avgdistinctdup", "SELECT AVG(DISTINCT VAL) FROM T3", ["=6.0"]);
    }

    [JavaOracleFact]
    public void EmptyCountDistinctIsZero()
    {
        CompareText(
            "select_text_countdistinctempty", "SELECT COUNT(DISTINCT NAME) FROM T WHERE ID > 100", ["=0"]);
    }

    [JavaOracleFact]
    public void EmptySumDistinctIsNull()
    {
        CompareText(
            "select_text_sumdistinctempty", "SELECT SUM(DISTINCT ID) FROM T WHERE ID > 100", ["=-"]);
    }

    [JavaOracleFact]
    public void EmptyAvgDistinctIsNull()
    {
        CompareText(
            "select_text_avgdistinctempty", "SELECT AVG(DISTINCT ID) FROM T WHERE ID > 100", ["=-"]);
    }

    [JavaOracleFact]
    public void SumBigints()
    {
        SessionLocal session = NewLongSession(out TransactionStore store);
        try
        {
            List<string> actual = RunText(session, "SELECT SUM(BIGVAL) FROM T2");
            List<string> java = RunJavaWithSetup(
                "select_text_sumlong",
                "SELECT SUM(BIGVAL) FROM T2",
                """
                stat.execute("CREATE TABLE T2(ID INT PRIMARY KEY, BIGVAL BIGINT)");
                stat.execute("INSERT INTO T2 VALUES (1, 9223372036854775800), (2, -5), (3, NULL), (4, 7)");
                """);
            Assert.Equal(["=9223372036854775802"], actual);
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void MinMaxBigints()
    {
        SessionLocal session = NewLongSession(out TransactionStore store);
        try
        {
            List<string> actual = RunText(session, "SELECT MIN(BIGVAL), MAX(BIGVAL) FROM T2");
            List<string> java = RunJavaWithSetup(
                "select_text_minmaxlong",
                "SELECT MIN(BIGVAL), MAX(BIGVAL) FROM T2",
                """
                stat.execute("CREATE TABLE T2(ID INT PRIMARY KEY, BIGVAL BIGINT)");
                stat.execute("INSERT INTO T2 VALUES (1, 9223372036854775800), (2, -5), (3, NULL), (4, 7)");
                """);
            Assert.Equal(["=-5:9223372036854775800"], actual);
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void GroupByPositionWaitsForMode()
    {
        // A bare number groups by position only where the mode says so, and the flag is
        // not ported: in regular mode it would group by a constant instead, which is a
        // guess this reader refuses to make either way.
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            TODOException e = Assert.Throws<TODOException>(
                () => session.PrepareCommand("SELECT NAME, COUNT(*) FROM T GROUP BY 1"));
            Assert.Contains("groupByColumnIndex", e.Message);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void CountStarOverEmptyTable()
    {
        // No rows and no groups is still one empty group: COUNT(*) answers 0, and a
        // GROUP BY over nothing answers no rows at all.
        SessionLocal session = NewEmptySession(out TransactionStore store);
        try
        {
            Assert.Equal(["=0"], RunText(session, "SELECT COUNT(*) FROM T"));
            Assert.Equal([], RunText(session, "SELECT NAME, COUNT(*) FROM T GROUP BY NAME"));
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void CountStarWithAlias()
    {
        CompareText("select_text_countas", "SELECT COUNT(*) AS C FROM T", ["=4"]);
    }

    [JavaOracleFact]
    public void CountStarWithLimit()
    {
        CompareText("select_text_countlim", "SELECT COUNT(*) FROM T LIMIT 1", ["=4"]);
    }

    [JavaOracleFact]
    public void CountStarWithOffsetReadsNothing()
    {
        CompareText("select_text_countoff", "SELECT COUNT(*) FROM T OFFSET 1", []);
    }

    [JavaOracleFact]
    public void CountDistinct()
    {
        CompareText("select_text_countdistinctid", "SELECT COUNT(DISTINCT ID) FROM T", ["=4"]);
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

    [JavaOracleFact]
    public void VarPop()
    {
        CompareText("select_text_varpop", "SELECT VAR_POP(ID) FROM T", ["=1.25"]);
    }

    [JavaOracleFact]
    public void VarSamp()
    {
        CompareText("select_text_varsamp", "SELECT VAR_SAMP(ID) FROM T", ["=1.6666666666666667"]);
    }

    [JavaOracleFact]
    public void StddevPop()
    {
        CompareText("select_text_stddevpop", "SELECT STDDEV_POP(ID) FROM T", ["=1.118033988749895"]);
    }

    [JavaOracleFact]
    public void StddevSamp()
    {
        CompareText("select_text_stddevsamp", "SELECT STDDEV_SAMP(ID) FROM T", ["=1.2909944487358056"]);
    }

    [JavaOracleFact]
    public void StddevVarAliases()
    {
        CompareText(
            "select_text_stddevalias",
            "SELECT STDDEV(ID), VARIANCE(ID), STDDEVP(ID), VARP(ID) FROM T",
            ["=1.2909944487358056:1.6666666666666667:1.118033988749895:1.25"]);
    }

    [JavaOracleFact]
    public void SingleVarSampIsNull()
    {
        CompareText("select_text_varsampsingle", "SELECT VAR_SAMP(ID) FROM T WHERE ID = 3", ["=-"]);
    }

    [JavaOracleFact]
    public void SingleVarPopIsZero()
    {
        CompareText("select_text_varpopsingle", "SELECT VAR_POP(ID) FROM T WHERE ID = 3", ["=0.0"]);
    }

    [JavaOracleFact]
    public void EmptyStddevIsNull()
    {
        CompareText("select_text_stddevempty", "SELECT STDDEV_POP(ID) FROM T WHERE ID > 100", ["=-"]);
    }

    [JavaOracleFact]
    public void VarPopDistinct()
    {
        CompareText("select_text_varpopdistinct", "SELECT VAR_POP(DISTINCT ID) FROM T", ["=1.25"]);
    }

    [JavaOracleFact]
    public void EveryAllTrue()
    {
        CompareText("select_text_everytrue", "SELECT EVERY(ID > 0) FROM T", ["=TRUE"]);
    }

    [JavaOracleFact]
    public void EveryOneFalse()
    {
        CompareText("select_text_everyfalse", "SELECT EVERY(ID > 2) FROM T", ["=FALSE"]);
    }

    [JavaOracleFact]
    public void AnyOneTrue()
    {
        CompareText("select_text_anytrue", "SELECT ANY(ID > 3) FROM T", ["=TRUE"]);
    }

    [JavaOracleFact]
    public void AnyNoneTrue()
    {
        CompareText("select_text_anyfalse", "SELECT ANY(ID > 100) FROM T", ["=FALSE"]);
    }

    [JavaOracleFact]
    public void SomeAlias()
    {
        CompareText("select_text_somealias", "SELECT SOME(ID > 3) FROM T", ["=TRUE"]);
    }

    [JavaOracleFact]
    public void BoolAndOr()
    {
        CompareText(
            "select_text_boolandor", "SELECT BOOL_AND(ID > 0), BOOL_OR(ID > 100) FROM T", ["=TRUE:FALSE"]);
    }

    [JavaOracleFact]
    public void EveryDistinct()
    {
        CompareText("select_text_everydistinct", "SELECT EVERY(DISTINCT ID > 0) FROM T", ["=TRUE"]);
    }

    [JavaOracleFact]
    public void GroupedEvery()
    {
        CompareText(
            "select_text_groupevery",
            "SELECT NAME, EVERY(ID > 1) FROM T GROUP BY NAME",
            ["=-:TRUE", "=b:FALSE", "=c:TRUE"]);
    }

    [JavaOracleFact]
    public void EmptyEveryIsNull()
    {
        CompareText("select_text_everyempty", "SELECT EVERY(ID > 0) FROM T WHERE ID > 100", ["=-"]);
    }

    [JavaOracleFact]
    public void EmptyAnyIsNull()
    {
        CompareText("select_text_anyempty", "SELECT ANY(ID > 0) FROM T WHERE ID > 100", ["=-"]);
    }

    [JavaOracleFact]
    public void EmptyMinMaxQuickIsNull()
    {
        SessionLocal session = NewEmptySession(out TransactionStore store);
        try
        {
            List<string> actual = RunText(session, "SELECT MIN(ID), MAX(ID) FROM T");
            List<string> java = RunJavaWithSetup(
                "select_text_minmaxquickempty",
                "SELECT MIN(ID), MAX(ID) FROM T",
                """stat.execute("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR)");""");
            Assert.Equal(["=-:-"], actual);
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void MinMaxWithWhereStaysGrouped()
    {
        CompareText(
            "select_text_minmaxwhere", "SELECT MIN(ID), MAX(ID) FROM T WHERE ID > 1", ["=2:4"]);
    }

    [JavaOracleFact]
    public void MedianIds()
    {
        CompareText("select_text_median", "SELECT MEDIAN(ID) FROM T", ["=2.5"]);
    }

    [JavaOracleFact]
    public void MedianOddCount()
    {
        CompareText("select_text_medianodd", "SELECT MEDIAN(ID) FROM T WHERE ID > 1", ["=3"]);
    }

    [JavaOracleFact]
    public void MedianName()
    {
        CompareText("select_text_medianname", "SELECT MEDIAN(NAME) FROM T", ["=b"]);
    }

    [JavaOracleFact]
    public void MedianGroupBy()
    {
        CompareText(
            "select_text_mediangroup", "SELECT NAME, MEDIAN(ID) FROM T GROUP BY NAME", ["=-:4", "=b:1.5", "=c:3"]);
    }

    [JavaOracleFact]
    public void MedianDistinct()
    {
        CompareText("select_text_mediandistinct", "SELECT MEDIAN(DISTINCT ID) FROM T", ["=2.5"]);
    }

    [JavaOracleFact]
    public void MedianEmptyIsNull()
    {
        CompareText("select_text_medianempty", "SELECT MEDIAN(ID) FROM T WHERE ID > 100", ["=-"]);
    }

    [JavaOracleFact]
    public void AnyValueSingle()
    {
        CompareText("select_text_anyvaluesingle", "SELECT ANY_VALUE(NAME) FROM T WHERE ID = 3", ["=c"]);
    }

    [JavaOracleFact]
    public void AnyValueEmptyIsNull()
    {
        CompareText("select_text_anyvalueempty", "SELECT ANY_VALUE(ID) FROM T WHERE ID > 100", ["=-"]);
    }

    [JavaOracleFact]
    public void AnyValueGroupBy()
    {
        // Every group holds one value (NULLs are never collected), so the
        // arbitrary pick is pinned down and the oracle means something.
        CompareText(
            "select_text_anyvaluegroup",
            "SELECT NAME, ANY_VALUE(NAME) FROM T GROUP BY NAME",
            ["=-:-", "=b:b", "=c:c"]);
    }

    [JavaOracleFact]
    public void AnyValueDistinctSingle()
    {
        CompareText(
            "select_text_anyvaluedistinct", "SELECT ANY_VALUE(DISTINCT NAME) FROM T WHERE ID = 3", ["=c"]);
    }

    [JavaOracleFact]
    public void PercentileContHalf()
    {
        CompareText(
            "select_text_pctcont",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY ID) FROM T",
            ["=2.5"]);
    }

    [JavaOracleFact]
    public void PercentileDiscHalf()
    {
        CompareText(
            "select_text_pctdisc",
            "SELECT PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY ID) FROM T",
            ["=2"]);
    }

    [JavaOracleFact]
    public void PercentileContQuarter()
    {
        CompareText(
            "select_text_pctcontq",
            "SELECT PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY ID) FROM T",
            ["=1.75"]);
    }

    [JavaOracleFact]
    public void PercentileDiscThreeQuarters()
    {
        CompareText(
            "select_text_pctdiscq",
            "SELECT PERCENTILE_DISC(0.75) WITHIN GROUP (ORDER BY ID) FROM T",
            ["=3"]);
    }

    [JavaOracleFact]
    public void PercentileContDesc()
    {
        CompareText(
            "select_text_pctcontdesc",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY ID DESC) FROM T",
            ["=2.5"]);
    }

    [JavaOracleFact]
    public void PercentileContOne()
    {
        CompareText(
            "select_text_pctcontone",
            "SELECT PERCENTILE_CONT(1) WITHIN GROUP (ORDER BY ID) FROM T",
            ["=4"]);
    }

    [JavaOracleFact]
    public void PercentileEmptyIsNull()
    {
        CompareText(
            "select_text_pctempty",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY ID) FROM T WHERE ID > 100",
            ["=-"]);
    }

    [Fact]
    public void PercentileBadFractionIsRefused()
    {
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            CommandContainer container = Assert.IsType<CommandContainer>(
                session.PrepareCommand("SELECT PERCENTILE_CONT(2) WITHIN GROUP (ORDER BY ID) FROM T"));
            H2SharpException e = Assert.Throws<H2SharpException>(() => container.Query(-1));
            Assert.Equal(90008, e.H2ErrorCode);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void DualInt()
    {
        CompareDual("select_dual_int", "SELECT 1 AS C1", ["=1"]);
    }

    [JavaOracleFact]
    public void DualLiterals()
    {
        CompareDual("select_dual_literals", "SELECT 'a' AS C1, TRUE AS C2, NULL AS C3", ["=a:TRUE:-"]);
    }

    [JavaOracleFact]
    public void DualWhereFalse()
    {
        CompareDual("select_dual_wherefalse", "SELECT 1 AS C1 WHERE 1 = 0", []);
    }

    [JavaOracleFact]
    public void DualCastVarchar()
    {
        CompareDual("select_dual_castvarchar", "SELECT CAST(1 AS VARCHAR) AS C1", ["=1"]);
    }

    [JavaOracleFact]
    public void DualCastDouble()
    {
        CompareDual("select_dual_castdouble", "SELECT CAST(1 AS DOUBLE) AS C1", ["=1.0"]);
    }

    [JavaOracleFact]
    public void DualCastNull()
    {
        CompareDual("select_dual_castnull", "SELECT CAST(NULL AS INT) AS C1", ["=-"]);
    }

    [JavaOracleFact]
    public void DualCastDate()
    {
        CompareDual("select_dual_castdate", "SELECT CAST('2017-06-15' AS DATE) AS C1", ["=2017-06-15"]);
    }

    [JavaOracleFact]
    public void DualNegative()
    {
        CompareDual("select_dual_negative", "SELECT -1 AS C1", ["=-1"]);
    }

    [JavaOracleFact]
    public void ListaggNames()
    {
        CompareText("select_text_listagg", "SELECT LISTAGG(NAME) FROM T", ["=b,b,c"]);
    }

    [JavaOracleFact]
    public void ListaggSeparator()
    {
        CompareText("select_text_listaggsep", "SELECT LISTAGG(NAME, ';') FROM T", ["=b;b;c"]);
    }

    [JavaOracleFact]
    public void ListaggIds()
    {
        CompareText("select_text_listaggids", "SELECT LISTAGG(ID, '-') FROM T", ["=1-2-3-4"]);
    }

    [JavaOracleFact]
    public void ListaggDistinct()
    {
        CompareText("select_text_listaggdistinct", "SELECT LISTAGG(DISTINCT NAME) FROM T", ["=b,c"]);
    }

    [JavaOracleFact]
    public void ListaggSingle()
    {
        CompareText("select_text_listaggsingle", "SELECT LISTAGG(NAME) FROM T WHERE ID = 3", ["=c"]);
    }

    [JavaOracleFact]
    public void ListaggEmptyIsNull()
    {
        CompareText("select_text_listaggempty", "SELECT LISTAGG(NAME) FROM T WHERE ID > 100", ["=-"]);
    }

    [JavaOracleFact]
    public void ListaggGrouped()
    {
        CompareText(
            "select_text_listagggroup",
            "SELECT NAME, LISTAGG(ID, ',') FROM T GROUP BY NAME",
            ["=-:4", "=b:1,2", "=c:3"]);
    }

    [JavaOracleFact]
    public void GroupConcatAlias()
    {
        CompareText("select_text_groupconcat", "SELECT GROUP_CONCAT(NAME) FROM T", ["=b,b,c"]);
    }

    [JavaOracleFact]
    public void GroupConcatSeparator()
    {
        CompareText(
            "select_text_groupconcatsep", "SELECT GROUP_CONCAT(NAME SEPARATOR ';') FROM T", ["=b;b;c"]);
    }

    [JavaOracleFact]
    public void StringAggAlias()
    {
        CompareText("select_text_stringagg", "SELECT STRING_AGG(NAME, ';') FROM T", ["=b;b;c"]);
    }

    [JavaOracleFact]
    public void RegrCount()
    {
        // T3 carries a NULL VAL: the pair with the NULL never reaches the counter.
        CompareDup("select_text_regrcount", "SELECT REGR_COUNT(VAL, ID) FROM T3", ["=3"]);
    }

    [JavaOracleFact]
    public void UnionDistinct()
    {
        CompareText("select_text_uniondistinct", "SELECT ID FROM T UNION SELECT ID FROM T", ["=1", "=2", "=3", "=4"]);
    }

    [JavaOracleFact]
    public void UnionAll()
    {
        CompareText("select_text_unionall", "SELECT ID FROM T UNION ALL SELECT ID FROM T");
    }

    [JavaOracleFact]
    public void UnionNames()
    {
        CompareText("select_text_unionnames", "SELECT NAME FROM T UNION SELECT NAME FROM T");
    }

    [JavaOracleFact]
    public void BetweenIds()
    {
        CompareText(
            "select_text_betweenids", "SELECT ID FROM T WHERE ID BETWEEN 2 AND 3", ["=2", "=3"]);
    }

    [JavaOracleFact]
    public void NotBetweenIds()
    {
        CompareText(
            "select_text_notbetweenids", "SELECT ID FROM T WHERE ID NOT BETWEEN 2 AND 3", ["=1", "=4"]);
    }

    [JavaOracleFact]
    public void BetweenNames()
    {
        CompareText(
            "select_text_betweennames", "SELECT ID FROM T WHERE NAME BETWEEN 'a' AND 'b'", ["=1", "=2"]);
    }

    [JavaOracleFact]
    public void BetweenSingle()
    {
        // The reference rewrites a degenerate frame into a plain equality.
        CompareText("select_text_betweensingle", "SELECT ID FROM T WHERE ID BETWEEN 2 AND 2", ["=2"]);
    }

    [JavaOracleFact]
    public void BetweenNullLowIsEmpty()
    {
        CompareText("select_text_betweenulownull", "SELECT ID FROM T WHERE ID BETWEEN NULL AND 3");
    }

    [JavaOracleFact]
    public void BetweenNullHighIsEmpty()
    {
        CompareText("select_text_betweenuhighnull", "SELECT ID FROM T WHERE ID BETWEEN 2 AND NULL");
    }

    [JavaOracleFact]
    public void BetweenReversedIsEmpty()
    {
        CompareText(
            "select_text_betweenreversed", "SELECT ID FROM T WHERE NAME BETWEEN 'b' AND 'a'");
    }

    [JavaOracleFact]
    public void BetweenSymmetric()
    {
        CompareText(
            "select_text_betweensymmetric", "SELECT ID FROM T WHERE ID BETWEEN SYMMETRIC 3 AND 2", ["=2", "=3"]);
    }

    [JavaOracleFact]
    public void BetweenAsymmetric()
    {
        CompareText(
            "select_text_betweenasymmetric", "SELECT ID FROM T WHERE ID BETWEEN ASYMMETRIC 2 AND 3", ["=2", "=3"]);
    }

    [JavaOracleFact]
    public void RegrAvgx()
    {
        CompareDup("select_text_regravgx", "SELECT REGR_AVGX(VAL, ID) FROM T3", ["=2.0"]);
    }

    [JavaOracleFact]
    public void RegrAvgy()
    {
        CompareDup("select_text_regravgy", "SELECT REGR_AVGY(VAL, ID) FROM T3", ["=5.666666666666667"]);
    }

    [JavaOracleFact]
    public void RegrSxx()
    {
        CompareDup("select_text_regrsxx", "SELECT REGR_SXX(VAL, ID) FROM T3", ["=2.0"]);
    }

    [JavaOracleFact]
    public void RegrSyy()
    {
        CompareDup("select_text_regrsyy", "SELECT REGR_SYY(VAL, ID) FROM T3", ["=2.666666666666666"]);
    }

    [JavaOracleFact]
    public void RegrCountEmptyIsZero()
    {
        // Unlike every other empty aggregate, a count of no pairs is zero, not NULL.
        CompareDup(
            "select_text_regrcountempty", "SELECT REGR_COUNT(VAL, ID) FROM T3 WHERE ID > 100", ["=0"]);
    }

    [JavaOracleFact]
    public void RegrAvgEmptyIsNull()
    {
        CompareDup(
            "select_text_regravgempty", "SELECT REGR_AVGY(VAL, ID) FROM T3 WHERE ID > 100", ["=-"]);
    }

    [Fact]
    public void RegrAvgxWrongTypeIsRefused()
    {
        // The x argument is the gated one for REGR_AVGX: a VARCHAR there refuses.
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            H2SharpException e = Assert.Throws<H2SharpException>(
                () => session.PrepareCommand("SELECT REGR_AVGX(ID, NAME) FROM T"));
            Assert.Equal(90015, e.H2ErrorCode);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void RegrAvgyWrongTypeIsRefused()
    {
        // The y argument is the gated one for REGR_AVGY: a VARCHAR there refuses.
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            H2SharpException e = Assert.Throws<H2SharpException>(
                () => session.PrepareCommand("SELECT REGR_AVGY(NAME, ID) FROM T"));
            Assert.Equal(90015, e.H2ErrorCode);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void RegrDistinctIsRefused()
    {
        // The reference reads no DISTINCT here either: it fails the same way.
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            H2SharpException e = Assert.Throws<H2SharpException>(
                () => session.PrepareCommand("SELECT REGR_COUNT(DISTINCT ID, ID) FROM T"));
            Assert.Equal(42001, e.H2ErrorCode);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void CovarPop()
    {
        CompareDup("select_text_covar_pop", "SELECT COVAR_POP(VAL, ID) FROM T3", ["=0.6666666666666666"]);
    }

    [JavaOracleFact]
    public void CovarSamp()
    {
        CompareDup("select_text_covar_samp", "SELECT COVAR_SAMP(VAL, ID) FROM T3", ["=1.0"]);
    }

    [JavaOracleFact]
    public void Corr()
    {
        CompareDup("select_text_corr", "SELECT CORR(VAL, ID) FROM T3", ["=0.8660254037844387"]);
    }

    [JavaOracleFact]
    public void RegrSlope()
    {
        CompareDup("select_text_regr_slope", "SELECT REGR_SLOPE(VAL, ID) FROM T3", ["=1.0"]);
    }

    [JavaOracleFact]
    public void RegrIntercept()
    {
        CompareDup("select_text_regr_intercept", "SELECT REGR_INTERCEPT(VAL, ID) FROM T3", ["=3.666666666666667"]);
    }

    [JavaOracleFact]
    public void RegrR2()
    {
        CompareDup("select_text_regr_r2", "SELECT REGR_R2(VAL, ID) FROM T3", ["=0.7500000000000002"]);
    }

    [JavaOracleFact]
    public void RegrSxy()
    {
        CompareDup("select_text_regr_sxy", "SELECT REGR_SXY(VAL, ID) FROM T3", ["=2.0"]);
    }

    [JavaOracleFact]
    public void CovarPopEmptyIsNull()
    {
        CompareDup(
            "select_text_covar_popempty", "SELECT COVAR_POP(VAL, ID) FROM T3 WHERE ID > 100", ["=-"]);
    }

    [JavaOracleFact]
    public void CorrEmptyIsNull()
    {
        CompareDup(
            "select_text_corrempty", "SELECT CORR(VAL, ID) FROM T3 WHERE ID > 100", ["=-"]);
    }

    [JavaOracleFact]
    public void CovarPopSingle()
    {
        // One pair is enough for the population form: the spread around one point is zero.
        CompareDup(
            "select_text_covar_popsingle", "SELECT COVAR_POP(VAL, ID) FROM T3 WHERE ID = 1", ["=0.0"]);
    }

    [JavaOracleFact]
    public void CovarSampSingleIsNull()
    {
        // But not for the sample form: it divides by count - 1.
        CompareDup(
            "select_text_covar_sampsingle", "SELECT COVAR_SAMP(VAL, ID) FROM T3 WHERE ID = 1", ["=-"]);
    }

    [JavaOracleFact]
    public void CorrConstXIsNull()
    {
        CompareDup("select_text_corrconstx", "SELECT CORR(VAL, 1) FROM T3", ["=-"]);
    }

    [JavaOracleFact]
    public void RegrSlopeConstXIsNull()
    {
        CompareDup("select_text_regr_slopecx", "SELECT REGR_SLOPE(VAL, 1) FROM T3", ["=-"]);
    }

    [JavaOracleFact]
    public void RegrSlopeConstYIsZero()
    {
        CompareDup("select_text_regr_slopecy", "SELECT REGR_SLOPE(1, ID) FROM T3", ["=0.0"]);
    }

    [JavaOracleFact]
    public void RegrR2ConstYIsOne()
    {
        // A flat line explains itself perfectly.
        CompareDup("select_text_regr_r2cy", "SELECT REGR_R2(1, ID) FROM T3", ["=1.0"]);
    }

    [JavaOracleFact]
    public void CorrConstYIsNull()
    {
        CompareDup("select_text_corrcy", "SELECT CORR(1, ID) FROM T3", ["=-"]);
    }

    [Fact]
    public void CovarDistinctIsRefused()
    {
        // Same two-argument reader as the single-row regressions: no DISTINCT there either.
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            H2SharpException e = Assert.Throws<H2SharpException>(
                () => session.PrepareCommand("SELECT COVAR_POP(DISTINCT ID, ID) FROM T"));
            Assert.Equal(42001, e.H2ErrorCode);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void ArrayAgg()
    {
        CompareText("select_text_arrayagg", "SELECT ARRAY_AGG(NAME) FROM T", ["=[b, b, c, null]"]);
    }

    [JavaOracleFact]
    public void ArrayAggDistinct()
    {
        CompareText("select_text_arrayaggdistinct", "SELECT ARRAY_AGG(DISTINCT NAME) FROM T", ["=[null, b, c]"]);
    }

    [JavaOracleFact]
    public void ArrayAggIds()
    {
        CompareText("select_text_arrayaggids", "SELECT ARRAY_AGG(ID) FROM T", ["=[1, 2, 3, 4]"]);
    }

    [JavaOracleFact]
    public void ArrayAggEmptyIsNull()
    {
        CompareText("select_text_arrayaggempty", "SELECT ARRAY_AGG(NAME) FROM T WHERE ID > 100", ["=-"]);
    }

    [Fact]
    public void ArrayAggOrderByWaitsForOrdering()
    {
        // ARRAY_AGG itself runs; only the ORDER BY inside the parentheses waits.
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            TODOException e = Assert.Throws<TODOException>(
                () => session.PrepareCommand("SELECT ARRAY_AGG(NAME ORDER BY NAME) FROM T"));
            Assert.Contains("ORDER BY", e.Message);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void BitAndAgg()
    {
        CompareDup("select_text_bitandagg", "SELECT BIT_AND_AGG(VAL) FROM T3", ["=5"]);
    }

    [JavaOracleFact]
    public void BitOrAgg()
    {
        CompareDup("select_text_bitoragg", "SELECT BIT_OR_AGG(VAL) FROM T3", ["=7"]);
    }

    [JavaOracleFact]
    public void BitXorAgg()
    {
        CompareDup("select_text_bitxoragg", "SELECT BIT_XOR_AGG(VAL) FROM T3", ["=7"]);
    }

    [JavaOracleFact]
    public void BitNandAgg()
    {
        CompareDup("select_text_bitnandagg", "SELECT BIT_NAND_AGG(VAL) FROM T3", ["=-6"]);
    }

    [JavaOracleFact]
    public void BitNorAgg()
    {
        CompareDup("select_text_bitnoragg", "SELECT BIT_NOR_AGG(VAL) FROM T3", ["=-8"]);
    }

    [JavaOracleFact]
    public void BitXnorAgg()
    {
        CompareDup("select_text_bitxnoragg", "SELECT BIT_XNOR_AGG(VAL) FROM T3", ["=-8"]);
    }

    [JavaOracleFact]
    public void BitAndAlias()
    {
        CompareDup("select_text_bitandalias", "SELECT BIT_AND(VAL) FROM T3", ["=5"]);
    }

    [JavaOracleFact]
    public void BitAndAggEmptyIsNull()
    {
        CompareDup(
            "select_text_bitandaggempty", "SELECT BIT_AND_AGG(VAL) FROM T3 WHERE ID > 100", ["=-"]);
    }

    [Fact]
    public void BitAndAggWrongTypeIsRefused()
    {
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            H2SharpException e = Assert.Throws<H2SharpException>(
                () => session.PrepareCommand("SELECT BIT_AND_AGG(NAME) FROM T"));
            Assert.Equal(90008, e.H2ErrorCode);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void ModeName()
    {
        CompareText("select_text_modename", "SELECT MODE(NAME) FROM T", ["=b"]);
    }

    [JavaOracleFact]
    public void ModeIds()
    {
        // Every id appears once; the mode keeps the first one in value order.
        CompareText("select_text_modeids", "SELECT MODE(ID) FROM T", ["=1"]);
    }

    [JavaOracleFact]
    public void StatsModeAlias()
    {
        CompareText("select_text_statsmode", "SELECT STATS_MODE(NAME) FROM T", ["=b"]);
    }

    [JavaOracleFact]
    public void ModeDesc()
    {
        CompareText("select_text_modedesc", "SELECT MODE(NAME ORDER BY NAME DESC) FROM T", ["=b"]);
    }

    [JavaOracleFact]
    public void ModeAsc()
    {
        CompareText("select_text_modeasc", "SELECT MODE(NAME ORDER BY NAME) FROM T", ["=b"]);
    }

    [JavaOracleFact]
    public void ModeEmptyIsNull()
    {
        CompareText("select_text_modeempty", "SELECT MODE(ID) FROM T WHERE ID > 100", ["=-"]);
    }

    [JavaOracleFact]
    public void HistogramName()
    {
        CompareText(
            "select_text_histogramname",
            "SELECT HISTOGRAM(NAME) FROM T",
            ["=[ROW (null, 1), ROW (b, 2), ROW (c, 1)]"]);
    }

    [JavaOracleFact]
    public void HistogramIds()
    {
        CompareText(
            "select_text_histogramids",
            "SELECT HISTOGRAM(ID) FROM T",
            ["=[ROW (1, 1), ROW (2, 1), ROW (3, 1), ROW (4, 1)]"]);
    }

    [JavaOracleFact]
    public void HistogramEmpty()
    {
        CompareText("select_text_histogramempty", "SELECT HISTOGRAM(ID) FROM T WHERE ID > 100", ["=[]"]);
    }

    [JavaOracleFact]
    public void JsonArrayAggName()
    {
        CompareText("select_text_jsonarrayagg", "SELECT JSON_ARRAYAGG(NAME) FROM T", ["=[\"b\",\"b\",\"c\"]"]);
    }

    [JavaOracleFact]
    public void JsonArrayAggIds()
    {
        CompareText("select_text_jsonarrayaggids", "SELECT JSON_ARRAYAGG(ID) FROM T", ["=[1,2,3,4]"]);
    }

    [JavaOracleFact]
    public void JsonArrayAggDistinct()
    {
        CompareText(
            "select_text_jsonarrayaggdistinct", "SELECT JSON_ARRAYAGG(DISTINCT NAME) FROM T", ["=[\"b\",\"c\"]"]);
    }

    [JavaOracleFact]
    public void JsonArrayAggNullOnNull()
    {
        CompareText(
            "select_text_jsonarrayaggnull", "SELECT JSON_ARRAYAGG(NAME NULL ON NULL) FROM T",
            ["=[\"b\",\"b\",\"c\",null]"]);
    }

    [JavaOracleFact]
    public void JsonArrayAggEmptyIsNull()
    {
        CompareText(
            "select_text_jsonarrayaggempty", "SELECT JSON_ARRAYAGG(ID) FROM T WHERE ID > 100", ["=-"]);
    }

    [JavaOracleFact]
    public void JsonObjectAgg()
    {
        CompareText(
            "select_text_jsonobjectagg",
            "SELECT JSON_OBJECTAGG(KEY ID VALUE NAME) FROM T",
            ["={\"1\":\"b\",\"2\":\"b\",\"3\":\"c\",\"4\":null}"]);
    }

    [JavaOracleFact]
    public void JsonObjectAggColon()
    {
        CompareText(
            "select_text_jsonobjectaggcolon",
            "SELECT JSON_OBJECTAGG(ID : NAME) FROM T",
            ["={\"1\":\"b\",\"2\":\"b\",\"3\":\"c\",\"4\":null}"]);
    }

    [JavaOracleFact]
    public void JsonObjectAggAbsentOnNull()
    {
        CompareText(
            "select_text_jsonobjectaggabsent",
            "SELECT JSON_OBJECTAGG(KEY ID VALUE NAME ABSENT ON NULL) FROM T",
            ["={\"1\":\"b\",\"2\":\"b\",\"3\":\"c\"}"]);
    }

    [JavaOracleFact]
    public void JsonObjectAggEmptyIsNull()
    {
        CompareText(
            "select_text_jsonobjectaggempty",
            "SELECT JSON_OBJECTAGG(KEY ID VALUE NAME) FROM T WHERE ID > 100",
            ["=-"]);
    }

    [Fact]
    public void JsonObjectAggNullKeyIsRefused()
    {
        // A NULL key is refused while the rows are gathered, before the value's
        // ABSENT ON NULL can matter.
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            CommandContainer container = Assert.IsType<CommandContainer>(
                session.PrepareCommand("SELECT JSON_OBJECTAGG(KEY NAME VALUE ID) FROM T"));
            H2SharpException e = Assert.Throws<H2SharpException>(() => container.Query(-1));
            Assert.Equal(90008, e.H2ErrorCode);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void GcdAggWaitsForAggregates()
    {
        // HISTOGRAM, MODE and the JSON aggregates run now; the rest still refuse by name
        // rather than misreading as a syntax error.
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            TODOException e = Assert.Throws<TODOException>(
                () => session.PrepareCommand("SELECT GCD_AGG(NAME) FROM T"));
            Assert.Contains("GCD_AGG", e.Message);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void ListaggOverflowWaitsForTruncate()
    {
        // LISTAGG itself runs; only the ON OVERFLOW clause waits.
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            TODOException e = Assert.Throws<TODOException>(
                () => session.PrepareCommand("SELECT LISTAGG(NAME ON OVERFLOW ERROR) FROM T"));
            Assert.Contains("ON OVERFLOW", e.Message);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void ListaggWithinGroupWaitsForOrdering()
    {
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            TODOException e = Assert.Throws<TODOException>(
                () => session.PrepareCommand("SELECT LISTAGG(NAME) WITHIN GROUP (ORDER BY NAME) FROM T"));
            Assert.Contains("WITHIN GROUP", e.Message);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void AvgIds()
    {
        CompareText("select_text_avg", "SELECT AVG(ID) FROM T", ["=2.5"]);
    }

    [JavaOracleFact]
    public void AvgGroupBy()
    {
        CompareText(
            "select_text_avggroup", "SELECT NAME, AVG(ID) FROM T GROUP BY NAME", ["=-:4.0", "=b:1.5", "=c:3.0"]);
    }

    [JavaOracleFact]
    public void AvgEmptyIsNull()
    {
        CompareText("select_text_avgempty", "SELECT AVG(ID) FROM T WHERE ID > 100", ["=-"]);
    }

    [JavaOracleFact]
    public void EmptyCountIsZero()
    {
        CompareText("select_text_countempty", "SELECT COUNT(*) FROM T WHERE ID > 100", ["=0"]);
    }

    [Fact]
    public void AvgWrongTypeIsRefused()
    {
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            H2SharpException e = Assert.Throws<H2SharpException>(
                () => session.PrepareCommand("SELECT AVG(NAME) FROM T"));
            Assert.Equal(90015, e.H2ErrorCode);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void AvgBigints()
    {
        SessionLocal session = NewLongSession(out TransactionStore store);
        try
        {
            List<string> actual = RunText(session, "SELECT AVG(BIGVAL) FROM T2");
            List<string> java = RunJavaWithSetup(
                "select_text_avglong",
                "SELECT AVG(BIGVAL) FROM T2",
                """
                stat.execute("CREATE TABLE T2(ID INT PRIMARY KEY, BIGVAL BIGINT)");
                stat.execute("INSERT INTO T2 VALUES (1, 9223372036854775800), (2, -5), (3, NULL), (4, 7)");
                """);
            Assert.Equal(["=3074457345618258600.6666666667"], actual);
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }
}
