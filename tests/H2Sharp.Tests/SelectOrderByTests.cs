/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What a sorted flat select answers, checked against the reference running its own.
 *
 * Both sides build the same select over the same keyed table, set the same order, prepare
 * the expressions and the plan, and run to a materialized result. The corpus has a tied
 * sort key and a null in it on purpose: ties keep their read order on both sides, because
 * both sorts are stable, and the null sits where the default null ordering puts it, which
 * the passing runs pin down rather than assume. What is not compared here is deliberate:
 * a tie straddling a limit boundary is decided by a random pivot in the reference itself,
 * so no such shape is compared.
 *
 * The order items are built the way the parser and the init leave them: a positional
 * order carries only its column index expression, an expression order carries the index
 * expression of its select-list position next to the expression itself. Resolving names
 * to positions is the init's doing and is not ported yet.
 */
using System.Collections.Generic;
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
public sealed class SelectOrderByTests
{
    private static SessionLocal NewSession()
    {
        return new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:select_order")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

    private const string JavaSession = """
        java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:select_order_e2e");
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
            org.h2.value.ValueInteger.get(2), org.h2.value.ValueVarchar.get("b") }));
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(4), org.h2.value.ValueNull.INSTANCE }));
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
        select.prepareExpressions();
        select.preparePlan();
        org.h2.result.ResultInterface r = select.query(-1);
        """;

    private const string JavaPrint = """
        while (r.next()) {
            org.h2.value.Value[] row = r.currentRow();
            System.out.println("=" + row[0] + ":"
                + (row[1] == org.h2.value.ValueNull.INSTANCE ? "-" : row[1].getString()));
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
            0, "T", [id, name], persistIndexes: false, persistData: false);
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
            new ExpressionColumn(table.GetColumn(1), filter, 0),
        ]);
        return select;
    }

    private static Select NewSelect(SessionLocal session, MVTable table)
        => NewSelect(session, table, out _);

    private static List<string> Print(ResultInterface result)
    {
        List<string> rows = [];
        while (result.Next())
        {
            Value?[] row = result.CurrentRow();
            rows.Add("=" + row[0] + ":" + (row[1] is ValueNull ? "-" : row[1]!.GetString()));
        }

        return rows;
    }

    private static string JavaPositional(int oneBased, int sortType)
        => "java.util.ArrayList<org.h2.command.query.QueryOrderBy> order ="
            + " new java.util.ArrayList<>();\n"
            + "org.h2.command.query.QueryOrderBy ob = new org.h2.command.query.QueryOrderBy();\n"
            + "ob.columnIndexExpr = org.h2.expression.ValueExpression.get(\n"
            + $"    org.h2.value.ValueInteger.get({oneBased}));\n"
            + $"ob.sortType = {sortType};\n"
            + "order.add(ob);\n";

    private const string JavaSetOrder = "select.setOrder(order);\n";

    private static void RunOrdered(
        List<QueryOrderBy> order, string javaOrder, long limit, long offset, List<string>? expected)
    {
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewKeyedTable(session, store);
            Select select = NewSelect(session, table);
            select.SetOrder(order);
            select.PrepareExpressions();
            select.PreparePlan(session, targetTableFilter: null);
            if (limit >= 0)
            {
                select.SetLimit(limit);
            }

            if (offset > 0)
            {
                select.SetOffset(offset);
            }

            List<string> actual = Print(select.Run(-1));

            string javaLimits = (limit >= 0
                    ? "select.setFetch(org.h2.expression.ValueExpression.get(\n"
                        + $"    org.h2.value.ValueInteger.get({limit})));\n"
                    : string.Empty)
                + (offset > 0
                    ? "select.setOffset(org.h2.expression.ValueExpression.get(\n"
                        + $"    org.h2.value.ValueInteger.get({offset})));\n"
                    : string.Empty);
            List<string> java = OracleRunner.Run(
                [], JavaSession + JavaTable + JavaSelect + javaOrder + javaLimits + JavaPrepare + JavaPrint,
                null, null, null);
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

    private static QueryOrderBy Positional(int oneBased, int sortType)
        => new() { columnIndexExpr = ValueExpression.Get(ValueInteger.Get(oneBased)), sortType = sortType };

    [JavaOracleFact]
    public void OrderByIdAscending()
    {
        RunOrdered(
            [Positional(1, SortOrder.Ascending)],
            JavaPositional(1, 0) + JavaSetOrder,
            -1, 0,
            ["=1:b", "=2:b", "=3:c", "=4:-"]);
    }

    [JavaOracleFact]
    public void OrderByIdDescending()
    {
        RunOrdered(
            [Positional(1, SortOrder.Descending)],
            JavaPositional(1, 1) + JavaSetOrder,
            -1, 0,
            ["=4:-", "=3:c", "=2:b", "=1:b"]);
    }

    [JavaOracleFact]
    public void OrderByNameKeepsTiesInReadOrder()
    {
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewKeyedTable(session, store);
            Select select = NewSelect(session, table, out TableFilter f);
            ExpressionColumn nameEc = new(table.GetColumn(1), f, 0);
            select.SetOrder(
            [
                new QueryOrderBy
                {
                    expression = nameEc,
                    columnIndexExpr = ValueExpression.Get(ValueInteger.Get(2)),
                    sortType = SortOrder.Ascending,
                },
            ]);
            select.PrepareExpressions();
            select.PreparePlan(session, targetTableFilter: null);
            List<string> actual = Print(select.Run(-1));

            const string javaOrder = """
                org.h2.expression.ExpressionColumn nameOrder =
                    new org.h2.expression.ExpressionColumn(
                        session.getDatabase(), null, "T", "NAME");
                nameOrder.mapColumns(f, 0, org.h2.expression.Expression.MAP_INITIAL);
                java.util.ArrayList<org.h2.command.query.QueryOrderBy> nameOrders =
                    new java.util.ArrayList<>();
                org.h2.command.query.QueryOrderBy nameOb = new org.h2.command.query.QueryOrderBy();
                nameOb.expression = nameOrder;
                nameOb.columnIndexExpr = org.h2.expression.ValueExpression.get(
                    org.h2.value.ValueInteger.get(2));
                nameOrders.add(nameOb);
                select.setOrder(nameOrders);
                """;
            List<string> java = OracleRunner.Run(
                [], JavaSession + JavaTable + JavaSelect + javaOrder + JavaPrepare + JavaPrint,
                null, null, null);
            Assert.Equal(["=4:-", "=1:b", "=2:b", "=3:c"], actual);
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void OrderByNameDescendingWithIdTiebreak()
    {
        // The tiebreak only orders what the first key left tied; the null has no tie to break.
        RunOrdered(
            [
                Positional(2, SortOrder.Descending),
                Positional(1, SortOrder.Ascending),
            ],
            JavaPositional(2, 1) + JavaPositionalSecond(1, 0) + JavaSetOrder,
            -1, 0,
            ["=3:c", "=1:b", "=2:b", "=4:-"]);
    }

    [JavaOracleFact]
    public void OrderByWithLimitAndOffset()
    {
        RunOrdered(
            [Positional(1, SortOrder.Ascending)],
            JavaPositional(1, 0) + JavaSetOrder,
            2, 1,
            ["=2:b", "=3:c"]);
    }

    [JavaOracleFact]
    public void OrderByNameNullsLast()
    {
        RunOrdered(
            [Positional(2, SortOrder.Ascending | SortOrder.NullsLast)],
            JavaPositional(2, 4) + JavaSetOrder,
            -1, 0,
            ["=1:b", "=2:b", "=3:c", "=4:-"]);
    }

    [JavaOracleFact]
    public void OrderByNegativeIndexSortsDescending()
    {
        // A negative index is the descending form of its column, which is how a negated order
        // term arrives from the parser.
        RunOrdered(
            [new QueryOrderBy { columnIndexExpr = ValueExpression.Get(ValueInteger.Get(-1)) }],
            JavaNegative(1),
            -1, 0,
            ["=4:-", "=3:c", "=2:b", "=1:b"]);
    }

    [JavaOracleFact]
    public void OrderByManyTiesKeepsReadOrder()
    {
        // Enough tied rows to leave the small-array path: the sort is stable, so ties keep
        // the order the index walk read them in. List.Sort makes no such promise.
        const int count = 200;
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            Column id = new("ID", TypeInfo.TypeInteger, null, 0);
            id.SetNullable(false);
            Column group = new("G", TypeInfo.TypeVarchar, null, 1);
            MVTable table = new(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                0, "T", [id, group], persistIndexes: false, persistData: false);
            foreach (Column column in table.GetColumns())
            {
                column.SetTable(table, column.GetColumnId());
            }

            table.AddIndex(session, "I", 7, [new IndexColumn(id, ascending: true)], 1,
                IndexType.CreatePrimaryKey(persistent: false, hash: false), create: true, indexComment: null);
            List<string> expected = [];
            for (int i = 0; i < count; i++)
            {
                string g = "g" + (i % 5);
                table.AddRow(session, new DefaultRow([ValueInteger.Get(i), ValueVarchar.Get(g)]));
            }

            for (int k = 0; k < 5; k++)
            {
                for (int i = k; i < count; i += 5)
                {
                    expected.Add($"={i}:g{k}");
                }
            }

            TableFilter f = new(session, table, "T", rightsChecked: true, null, 0, null);
            Select select = new(session);
            select.AddTableFilter(f, isTop: true);
            select.SetExpressions(
            [
                new ExpressionColumn(table.GetColumn(0), f, 0),
                new ExpressionColumn(table.GetColumn(1), f, 0),
            ]);
            select.SetOrder([Positional(2, SortOrder.Ascending)]);
            select.PrepareExpressions();
            select.PreparePlan(session, targetTableFilter: null);
            List<string> actual = Print(select.Run(-1));

            const string javaSetup = """
                org.h2.command.ddl.CreateTableData data = new org.h2.command.ddl.CreateTableData();
                data.schema = db.getSchema("PUBLIC");
                data.id = 0;
                data.tableName = "T";
                org.h2.table.Column idCol = new org.h2.table.Column(
                    "ID", org.h2.value.TypeInfo.TYPE_INTEGER);
                idCol.setNullable(false);
                data.columns.add(idCol);
                data.columns.add(new org.h2.table.Column("G", org.h2.value.TypeInfo.TYPE_VARCHAR));
                data.persistIndexes = false;
                data.persistData = false;
                org.h2.mvstore.db.MVTable table = new org.h2.mvstore.db.MVTable(data, db.getStore());
                org.h2.table.IndexColumn[] cols = new org.h2.table.IndexColumn[]{
                    new org.h2.table.IndexColumn(table.getColumn("ID")) };
                table.addIndex(session, "I", 7, cols, 1,
                    org.h2.index.IndexType.createPrimaryKey(false, false), true, null);
                for (int i = 0; i < 200; i++) {
                    table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
                        org.h2.value.ValueInteger.get(i),
                        org.h2.value.ValueVarchar.get("g" + (i % 5)) }));
                }
                org.h2.table.TableFilter f = new org.h2.table.TableFilter(
                    session, table, "T", true, null, 0, null);
                org.h2.command.query.Select select = new org.h2.command.query.Select(session, null);
                select.addTableFilter(f, true);
                java.util.ArrayList<org.h2.expression.Expression> exprs = new java.util.ArrayList<>();
                org.h2.expression.ExpressionColumn idEc = new org.h2.expression.ExpressionColumn(
                    session.getDatabase(), null, "T", "ID");
                idEc.mapColumns(f, 0, org.h2.expression.Expression.MAP_INITIAL);
                org.h2.expression.ExpressionColumn gEc = new org.h2.expression.ExpressionColumn(
                    session.getDatabase(), null, "T", "G");
                gEc.mapColumns(f, 0, org.h2.expression.Expression.MAP_INITIAL);
                exprs.add(idEc);
                exprs.add(gEc);
                select.setExpressions(exprs);
                """;
            List<string> java = OracleRunner.Run(
                [],
                JavaSession + javaSetup + JavaPositional(2, 0) + JavaSetOrder + JavaPrepare + JavaPrint,
                null, null, null);
            Assert.Equal(expected, actual);
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void OrderByOutOfRangeIsRefused()
    {
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewKeyedTable(session, store);
            Select select = NewSelect(session, table);
            select.SetOrder([Positional(9, SortOrder.Ascending)]);

            H2SharpException csharp = Assert.Throws<H2SharpException>(select.PrepareExpressions);
            Assert.Equal(90068, csharp.H2ErrorCode);

            List<string> java = OracleRunner.Run(
                [],
                JavaSession + JavaTable + JavaSelect + JavaPositional(9, 0) + JavaSetOrder
                + "try { select.prepareExpressions(); System.out.println(\"OK\"); }\n"
                + "catch (org.h2.message.DbException e) {\n"
                + "    System.out.println(\"ERR \" + e.getErrorCode());\n"
                + "}\n"
                + "conn.close();\n",
                null, null, null);
            Assert.Equal(["ERR 90068"], java);
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void OrderByDuplicateKeyIsKeptOnce()
    {
        // No reference run: dropping a duplicated key is invisible in rows, so the built sort
        // itself is asserted. The select list holds real columns, because a constant would be
        // dropped with the duplicate.
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewKeyedTable(session, store);
            Select select = NewSelect(session, table);
            select.SetOrder([Positional(1, SortOrder.Ascending), Positional(1, SortOrder.Ascending)]);
            select.PrepareExpressions();

            Assert.Equal([0], select.GetSortOrder()!.GetQueryColumnIndexes());
        }
        finally
        {
            store.Dispose();
        }
    }

    [Fact]
    public void OrderByConstantSortsNothing()
    {
        // No reference run either: a sort over nothing but constants is dropped to null.
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
                ValueExpression.Get(ValueInteger.Get(42)),
            ]);
            select.SetOrder([Positional(2, SortOrder.Ascending)]);
            select.PrepareExpressions();

            Assert.Null(select.GetSortOrder());
        }
        finally
        {
            store.Dispose();
        }
    }

    private static string JavaPositionalSecond(int oneBased, int sortType)
        => "org.h2.command.query.QueryOrderBy ob2 = new org.h2.command.query.QueryOrderBy();\n"
            + "ob2.columnIndexExpr = org.h2.expression.ValueExpression.get(\n"
            + $"    org.h2.value.ValueInteger.get({oneBased}));\n"
            + $"ob2.sortType = {sortType};\n"
            + "order.add(ob2);\n";

    private static string JavaNegative(int oneBased)
        => "java.util.ArrayList<org.h2.command.query.QueryOrderBy> negOrder ="
            + " new java.util.ArrayList<>();\n"
            + "org.h2.command.query.QueryOrderBy negOb = new org.h2.command.query.QueryOrderBy();\n"
            + "negOb.columnIndexExpr = org.h2.expression.ValueExpression.get(\n"
            + $"    org.h2.value.ValueInteger.get(-{oneBased}));\n"
            + "negOrder.add(negOb);\n"
            + "select.setOrder(negOrder);\n";
}
