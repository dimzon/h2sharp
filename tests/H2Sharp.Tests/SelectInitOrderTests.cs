/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What resolving ORDER BY names to select-list positions answers, checked against the
 * reference running its own init.
 *
 * Both sides build the same select over the same keyed table, set the same order with
 * name-form items the way a parser leaves them — a bare name, a table-qualified name, an
 * alias — run init, and then prepare and read. The resolved positions are compared
 * directly through the built sort, and the rows line by line: a name that resolved to the
 * wrong column sorts by that column on one side only.
 *
 * Two shapes are asserted without a reference run. A constant order item is dropped
 * before any sort is built, which no row can show. A second init is refused, which is a
 * harness guard both sides have by construction. A DISTINCT query ordering by an
 * unrelated expression waits for the mode machinery: the reference reads its flag off
 * the mode and this side refuses reading it, so that fact pins the refusal and names
 * what is missing.
 */
using System.Collections.Generic;
using H2Sharp;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Expressions;
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
public sealed class SelectInitOrderTests
{
    private static SessionLocal NewSession()
    {
        return new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:select_init")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

    private const string JavaSession = """
        java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:select_init_e2e");
        org.h2.jdbc.JdbcConnection c = (org.h2.jdbc.JdbcConnection) conn;
        org.h2.engine.SessionLocal session = (org.h2.engine.SessionLocal) c.getSession();
        org.h2.engine.Database db = session.getDatabase();
        """;

    private const string JavaTable = """
        org.h2.command.ddl.CreateTableData data = new org.h2.command.ddl.CreateTableData();
        data.schema = db.getSchema("PUBLIC");
        data.id = 0;
        data.tableName = "T";
        org.h2.table.Column idCol = new org.h2.table.Column("ID", org.h2.value.TypeInfo.TYPE_INTEGER);
        idCol.setNullable(false);
        data.columns.add(idCol);
        data.columns.add(new org.h2.table.Column("NAME", org.h2.value.TypeInfo.TYPE_VARCHAR));
        data.persistIndexes = false;
        data.persistData = false;
        org.h2.mvstore.db.MVTable table = new org.h2.mvstore.db.MVTable(data, db.getStore());
        org.h2.table.IndexColumn[] cols = new org.h2.table.IndexColumn[]{
            new org.h2.table.IndexColumn(table.getColumn("ID")) };
        table.addIndex(session, "I", 7, cols, 1,
            org.h2.index.IndexType.createPrimaryKey(false, false), true, null);
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(3), org.h2.value.ValueVarchar.get("c") }));
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(1), org.h2.value.ValueVarchar.get("b") }));
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(2), org.h2.value.ValueVarchar.get("a") }));
        """;

    private const string JavaSelect = """
        org.h2.table.TableFilter f = new org.h2.table.TableFilter(session, table, "T", true, null, 0, null);
        org.h2.command.query.Select select = new org.h2.command.query.Select(session, null);
        select.addTableFilter(f, true);
        java.util.ArrayList<org.h2.expression.Expression> exprs = new java.util.ArrayList<>();
        org.h2.expression.ExpressionColumn idEc = new org.h2.expression.ExpressionColumn(
            session.getDatabase(), null, "T", "ID");
        idEc.mapColumns(f, 0, org.h2.expression.Expression.MAP_INITIAL);
        org.h2.expression.ExpressionColumn nameEc = new org.h2.expression.ExpressionColumn(
            session.getDatabase(), null, "T", "NAME");
        nameEc.mapColumns(f, 0, org.h2.expression.Expression.MAP_INITIAL);
        exprs.add(idEc);
        exprs.add(nameEc);
        select.setExpressions(exprs);
        """;

    private const string JavaPrepare = """
        select.init();
        select.prepareExpressions();
        select.preparePlan();
        System.out.println("sort:" + java.util.Arrays.toString(
            select.getSortOrder().getQueryColumnIndexes()));
        org.h2.result.ResultInterface r = select.query(-1);
        """;

    private const string JavaPrint = """
        while (r.next()) {
            org.h2.value.Value[] row = r.currentRow();
            System.out.println("=" + row[0] + ":" + row[1].getString());
        }
        conn.close();
        """;

    private static MVTable NewKeyedTable(SessionLocal session, TransactionStore store)
    {
        Column id = new("ID", TypeInfo.TypeInteger, null, 0);
        id.SetNullable(false);
        Column name = new("NAME", TypeInfo.TypeVarchar, null, 1);
        MVTable table = new(
            session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
            0, "T", [id, name], store, persistData: false);
        foreach (Column column in table.GetColumns())
        {
            column.SetTable(table, column.GetColumnId());
        }

        table.AddIndex(session, "I", 7, [new IndexColumn(id, ascending: true)], 1,
            IndexType.CreatePrimaryKey(persistent: false, hash: false), create: true, indexComment: null);
        table.AddRow(session, new DefaultRow([ValueInteger.Get(3), ValueVarchar.Get("c")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(1), ValueVarchar.Get("b")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(2), ValueVarchar.Get("a")]));
        return table;
    }

    private static Select NewSelect(SessionLocal session, MVTable table, out TableFilter filter)
    {
        filter = new(session, table, "T", rightsChecked: true, null, 0, null);
        Select select = new(session);
        select.AddTableFilter(filter, isTop: true);
        select.SetExpressions(
        [
            new ExpressionColumn(table.GetColumn(0), filter, 0),
            new ExpressionColumn(table.GetColumn(1), filter, 1),
        ]);
        return select;
    }

    private static List<string> Print(ResultInterface result)
    {
        List<string> rows = [];
        while (result.Next())
        {
            Value?[] row = result.CurrentRow();
            rows.Add("=" + row[0] + ":" + row[1]!.GetString());
        }

        return rows;
    }

    private static string JavaNameOrder(string? tableAlias, string column)
        => "java.util.ArrayList<org.h2.command.query.QueryOrderBy> order ="
            + " new java.util.ArrayList<>();\n"
            + "org.h2.command.query.QueryOrderBy ob = new org.h2.command.query.QueryOrderBy();\n"
            + "ob.expression = new org.h2.expression.ExpressionColumn(\n"
            + $"    session.getDatabase(), null, {(tableAlias is null ? "null" : $"\"{tableAlias}\"")}, \"{column}\");\n"
            + "order.add(ob);\n"
            + "select.setOrder(order);\n";

    private static void RunInitOrdered(
        List<QueryOrderBy> order, string javaOrder, int[] expectedIndexes, List<string> expectedRows)
    {
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewKeyedTable(session, store);
            Select select = NewSelect(session, table, out _);
            select.SetOrder(order);
            select.Init();
            select.PrepareExpressions();
            select.PreparePlan(session, targetTableFilter: null);

            int[] actualIndexes = select.GetSortOrder()!.GetQueryColumnIndexes();
            List<string> actual = Print(select.Run(-1));

            List<string> java = OracleRunner.Run(
                [], JavaSession + JavaTable + JavaSelect + javaOrder + JavaPrepare + JavaPrint,
                null, null, null);
            Assert.Equal(expectedIndexes, actualIndexes);
            Assert.Equal(expectedRows, actual);
            Assert.Equal("sort:[" + string.Join(", ", expectedIndexes) + "]", java[0]);
            Assert.Equal(java[1..], actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    private static QueryOrderBy NameOrder(string? tableAlias, string column, int sortType = SortOrder.Ascending)
        => new() { expression = new ExpressionColumn(column, tableAlias, 0), sortType = sortType };

    [JavaOracleFact]
    public void BareNameResolvesToItsColumn()
    {
        RunInitOrdered(
            [NameOrder(null, "NAME")],
            JavaNameOrder(null, "NAME"),
            [1],
            ["=2:a", "=1:b", "=3:c"]);
    }

    [JavaOracleFact]
    public void QualifiedNameResolvesToItsColumn()
    {
        RunInitOrdered(
            [NameOrder("T", "NAME")],
            JavaNameOrder("T", "NAME"),
            [1],
            ["=2:a", "=1:b", "=3:c"]);
    }

    [JavaOracleFact]
    public void AliasNameResolvesToItsColumn()
    {
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewKeyedTable(session, store);
            TableFilter f = new(session, table, "T", rightsChecked: true, null, 0, null);
            Select select = new(session);
            select.AddTableFilter(f, isTop: true);
            select.SetExpressions(
            [
                new ExpressionColumn(table.GetColumn(0), f, 0),
                new Alias(new ExpressionColumn(table.GetColumn(1), f, 1), "N", true),
            ]);
            select.SetOrder([NameOrder(null, "N")]);
            select.Init();
            select.PrepareExpressions();
            select.PreparePlan(session, targetTableFilter: null);

            int[] actualIndexes = select.GetSortOrder()!.GetQueryColumnIndexes();
            List<string> actual = Print(select.Run(-1));

            const string javaAliasSelect = """
                org.h2.table.TableFilter f = new org.h2.table.TableFilter(session, table, "T", true, null, 0, null);
                org.h2.command.query.Select select = new org.h2.command.query.Select(session, null);
                select.addTableFilter(f, true);
                java.util.ArrayList<org.h2.expression.Expression> exprs = new java.util.ArrayList<>();
                org.h2.expression.ExpressionColumn idEc = new org.h2.expression.ExpressionColumn(
                    session.getDatabase(), null, "T", "ID");
                idEc.mapColumns(f, 0, org.h2.expression.Expression.MAP_INITIAL);
                org.h2.expression.ExpressionColumn nameEc = new org.h2.expression.ExpressionColumn(
                    session.getDatabase(), null, "T", "NAME");
                nameEc.mapColumns(f, 0, org.h2.expression.Expression.MAP_INITIAL);
                exprs.add(idEc);
                exprs.add(new org.h2.expression.Alias(nameEc, "N", true));
                select.setExpressions(exprs);
                """;
            List<string> java = OracleRunner.Run(
                [],
                JavaSession + JavaTable + javaAliasSelect
                    + JavaNameOrder(null, "N") + JavaPrepare + JavaPrint,
                null, null, null);
            Assert.Equal([1], actualIndexes);
            Assert.Equal(["=2:a", "=1:b", "=3:c"], actual);
            Assert.Equal("sort:[1]", java[0]);
            Assert.Equal(java[1..], actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void UnrelatedColumnIsAppendedPastTheVisibleOnes()
    {
        // ORDER BY a column that is not selected: it joins the select list past the visible
        // columns, sorts by it, and never shows. The appended item is already mapped, as the
        // mapping pass that would map a name-form one is not ported yet.
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewKeyedTable(session, store);
            TableFilter f = new(session, table, "T", rightsChecked: true, null, 0, null);
            Select select = new(session);
            select.AddTableFilter(f, isTop: true);
            select.SetExpressions([new ExpressionColumn(table.GetColumn(0), f, 0)]);
            select.SetOrder(
            [
                new QueryOrderBy
                {
                    expression = new ExpressionColumn(table.GetColumn(1), f, 1),
                    sortType = SortOrder.Ascending,
                },
            ]);
            select.Init();
            select.PrepareExpressions();
            select.PreparePlan(session, targetTableFilter: null);

            Assert.Equal(1, select.GetColumnCount());
            Assert.Equal(2, select.GetExpressions().Length);
            int[] actualIndexes = select.GetSortOrder()!.GetQueryColumnIndexes();
            // Only the visible column prints: the appended one sorts but never shows.
            ResultInterface result = select.Run(-1);
            List<string> actual = [];
            while (result.Next())
            {
                actual.Add("=" + result.CurrentRow()[0]);
            }

            const string javaNarrowSelect = """
                org.h2.table.TableFilter f = new org.h2.table.TableFilter(session, table, "T", true, null, 0, null);
                org.h2.command.query.Select select = new org.h2.command.query.Select(session, null);
                select.addTableFilter(f, true);
                java.util.ArrayList<org.h2.expression.Expression> exprs = new java.util.ArrayList<>();
                org.h2.expression.ExpressionColumn idEc = new org.h2.expression.ExpressionColumn(
                    session.getDatabase(), null, "T", "ID");
                idEc.mapColumns(f, 0, org.h2.expression.Expression.MAP_INITIAL);
                exprs.add(idEc);
                select.setExpressions(exprs);
                java.util.ArrayList<org.h2.command.query.QueryOrderBy> order =
                    new java.util.ArrayList<>();
                org.h2.command.query.QueryOrderBy ob = new org.h2.command.query.QueryOrderBy();
                ob.expression = new org.h2.expression.ExpressionColumn(
                    session.getDatabase(), null, "T", "NAME");
                order.add(ob);
                select.setOrder(order);
                """;
            List<string> java = OracleRunner.Run(
                [],
                JavaSession + JavaTable + javaNarrowSelect + JavaPrepare + JavaPrintNarrow,
                null, null, null);
            Assert.Equal([1], actualIndexes);
            Assert.Equal(["=2", "=1", "=3"], actual);
            Assert.Equal("sort:[1]", java[0]);
            Assert.Equal(java[1..], actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    private const string JavaPrintNarrow = """
        while (r.next()) {
            org.h2.value.Value[] row = r.currentRow();
            System.out.println("=" + row[0]);
        }
        conn.close();
        """;

    [JavaOracleFact]
    public void ConstantOrderIsDropped()
    {
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewKeyedTable(session, store);
            Select select = NewSelect(session, table, out _);
            select.SetOrder(
            [
                new QueryOrderBy
                {
                    expression = ValueExpression.Get(ValueInteger.Get(1)),
                    sortType = SortOrder.Ascending,
                },
            ]);
            select.Init();
            select.PrepareExpressions();
            select.PreparePlan(session, targetTableFilter: null);

            Assert.Null(select.GetSortOrder());
            List<string> actual = Print(select.Run(-1));

            const string javaConstOrder = """
                java.util.ArrayList<org.h2.command.query.QueryOrderBy> order =
                    new java.util.ArrayList<>();
                org.h2.command.query.QueryOrderBy ob = new org.h2.command.query.QueryOrderBy();
                ob.expression = org.h2.expression.ValueExpression.get(
                    org.h2.value.ValueInteger.get(1));
                order.add(ob);
                select.setOrder(order);
                """;
            List<string> java = OracleRunner.Run(
                [],
                JavaSession + JavaTable + JavaSelect + javaConstOrder
                    + JavaPrepareNoSort + JavaPrint,
                null, null, null);
            Assert.Equal(["=1:b", "=2:a", "=3:c"], actual);
            Assert.Equal("sort:null", java[0]);
            Assert.Equal(java[1..], actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    private const string JavaPrepareNoSort = """
        select.init();
        select.prepareExpressions();
        select.preparePlan();
        System.out.println("sort:" + select.getSortOrder());
        org.h2.result.ResultInterface r = select.query(-1);
        """;

    [Fact]
    public void SecondInitIsRefused()
    {
        // No reference run: a harness guard both sides have by construction, the reference
        // through its own checkInit.
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewKeyedTable(session, store);
            Select select = NewSelect(session, table, out _);
            select.SetOrder([NameOrder(null, "NAME")]);
            select.Init();

            _ = Assert.Throws<System.Exception>(select.Init);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void DistinctWithUnrelatedOrderWaitsForTheMode()
    {
        // The reference reads its allow-flag off the mode here; the mode is not ported and
        // reading it refuses, so this pins the refusal and names what is missing rather than
        // comparing a refusal that is not the reference's.
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewKeyedTable(session, store);
            TableFilter f = new(session, table, "T", rightsChecked: true, null, 0, null);
            Select select = new(session);
            select.AddTableFilter(f, isTop: true);
            select.SetExpressions([new ExpressionColumn(table.GetColumn(0), f, 0)]);
            select.SetDistinct(true);
            select.SetOrder(
            [
                new QueryOrderBy
                {
                    expression = new ExpressionColumn(table.GetColumn(1), f, 1),
                    sortType = SortOrder.Ascending,
                },
            ]);

            TODOException e = Assert.Throws<TODOException>(select.Init);
            Assert.Contains("getMode", e.Message);
        }
        finally
        {
            store.Dispose();
        }
    }
}
