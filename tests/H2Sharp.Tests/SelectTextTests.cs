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

    [Fact]
    public void ListaggWaitsForAggregates()
    {
        // COUNT, SUM, MIN, MAX, AVG, STDDEV, VAR, ANY, EVERY and MEDIAN run since
        // their paths landed; every other aggregate refuses by name rather than
        // misreading as a syntax error.
        SessionLocal session = NewSession(out TransactionStore store, out _);
        try
        {
            TODOException e = Assert.Throws<TODOException>(
                () => session.PrepareCommand("SELECT LISTAGG(NAME) FROM T"));
            Assert.Contains("LISTAGG", e.Message);
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
