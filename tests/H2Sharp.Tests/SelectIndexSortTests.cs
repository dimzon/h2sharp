/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What serving ORDER BY from an index answers, checked against the reference running its
 * own plan.
 *
 * Both sides build the same keyed table, plan the same select, and read. What is compared
 * is which index the filter walks — the key's delegate for the key order, the scan for an
 * order no index serves — and the rows it reads, in both directions. A plan that chose
 * the wrong index reads the wrong order on one side only, or sorts what the other side
 * walks for free.
 *
 * The column counts behind the choice are not asserted: nothing outside the run observes
 * them, and the rows prove the contract the counts serve. What is not covered here is
 * deliberate: a partial sort needs a multi-column index, and secondary indexes are still
 * a stub; the rowid special case needs a rowid in the select list, which the mapping
 * pass does not produce yet.
 */
using System.Collections.Generic;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Conditions;
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
public sealed class SelectIndexSortTests
{
    private static SessionLocal NewSession()
    {
        return new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:select_index_sort")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

    private const string JavaSession = """
        java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:select_index_sort_e2e");
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
        org.h2.index.Index pk = table.addIndex(session, "I", 7, cols, 1,
            org.h2.index.IndexType.createPrimaryKey(false, false), true, null);
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(3), org.h2.value.ValueVarchar.get("c") }));
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(1), org.h2.value.ValueVarchar.get("b") }));
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(2), org.h2.value.ValueVarchar.get("a") }));
        """;

    private const string JavaSelect = """
        org.h2.command.query.Select select = new org.h2.command.query.Select(session, null);
        org.h2.table.TableFilter f = new org.h2.table.TableFilter(session, table, "T", true, select, 0, null);
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
        System.out.println("index:" + (f.getIndex() == pk) + ":"
            + f.getIndex().getClass().getSimpleName());
        org.h2.result.ResultInterface r = select.query(-1);
        """;

    private const string JavaPrint = """
        while (r.next()) {
            org.h2.value.Value[] row = r.currentRow();
            System.out.println("=" + row[0] + ":" + row[1].getString());
        }
        conn.close();
        """;

    private static MVTable NewKeyedTable(SessionLocal session, TransactionStore store, out Indexes.Index keys)
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

        keys = table.AddIndex(session, "I", 7, [new IndexColumn(id, ascending: true)], 1,
            IndexType.CreatePrimaryKey(persistent: false, hash: false), create: true, indexComment: null);
        table.AddRow(session, new DefaultRow([ValueInteger.Get(3), ValueVarchar.Get("c")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(1), ValueVarchar.Get("b")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(2), ValueVarchar.Get("a")]));
        return table;
    }

    private static string JavaPositional(int oneBased, int sortType)
        => "java.util.ArrayList<org.h2.command.query.QueryOrderBy> order ="
            + " new java.util.ArrayList<>();\n"
            + "org.h2.command.query.QueryOrderBy ob = new org.h2.command.query.QueryOrderBy();\n"
            + "ob.columnIndexExpr = org.h2.expression.ValueExpression.get(\n"
            + $"    org.h2.value.ValueInteger.get({oneBased}));\n"
            + $"ob.sortType = {sortType};\n"
            + "order.add(ob);\n"
            + "select.setOrder(order);\n";

    private static void RunPlanned(
        int oneBased, int sortType, bool throughKeys, List<string> expectedRows, bool withCondition)
    {
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewKeyedTable(session, store, out Indexes.Index keys);
            // The filter knows its select, as the parser-built ones do: only then does the
            // costing see the sort and price the key index for it.
            Select select = new(session);
            TableFilter f = new(session, table, "T", rightsChecked: true, select, 0, null);
            select.AddTableFilter(f, isTop: true);
            select.SetExpressions(
            [
                new ExpressionColumn(table.GetColumn(0), f, 0),
                new ExpressionColumn(table.GetColumn(1), f, 0),
            ]);
            string javaCondition = string.Empty;
            if (withCondition)
            {
                // A range over the key: the optimizer walks the key index for it, and the
                // sort block then confirms the walk serves the order.
                ExpressionColumn idEc = new(table.GetColumn(0), f, 0);
                select.AddCondition(new Comparison(
                    Comparison.Bigger, idEc, ValueExpression.Get(ValueInteger.Get(0)), whenOperand: false));
                javaCondition = """
                    org.h2.expression.ExpressionColumn condEc = new org.h2.expression.ExpressionColumn(
                        session.getDatabase(), null, "T", "ID");
                    condEc.mapColumns(f, 0, org.h2.expression.Expression.MAP_INITIAL);
                    org.h2.expression.condition.Comparison cond =
                        new org.h2.expression.condition.Comparison(
                            org.h2.expression.condition.Comparison.BIGGER, condEc,
                            org.h2.expression.ValueExpression.get(org.h2.value.ValueInteger.get(0)), false);
                    select.addCondition(cond);
                    """;
            }

            select.SetOrder(
            [
                new QueryOrderBy
                {
                    columnIndexExpr = ValueExpression.Get(ValueInteger.Get(oneBased)),
                    sortType = sortType,
                },
            ]);
            select.PrepareExpressions();
            select.PreparePlan(session, targetTableFilter: null);

            Indexes.Index? chosen = f.GetIndex();
            List<string> actual = [$"index:{ReferenceEquals(keys, chosen).ToString().ToLowerInvariant()}:{chosen?.GetType().Name}"];
            ResultInterface result = select.Run(-1);
            while (result.Next())
            {
                Value?[] row = result.CurrentRow();
                actual.Add("=" + row[0] + ":" + row[1]!.GetString());
            }

            List<string> java = OracleRunner.Run(
                [],
                JavaSession + JavaTable + JavaSelect + javaCondition + JavaPositional(oneBased, sortType)
                    + JavaPrepare + JavaPrint,
                null, null, null);
            Assert.Equal(throughKeys, ReferenceEquals(keys, chosen));
            Assert.Equal(expectedRows, actual[1..]);
            Assert.Equal(actual, java);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void KeyOrderReadsThroughTheKeyIndex()
    {
        RunPlanned(1, SortOrder.Ascending, true, ["=1:b", "=2:a", "=3:c"], withCondition: true);
    }

    [JavaOracleFact]
    public void KeyOrderDescendingReadsThroughTheKeyIndexBackwards()
    {
        RunPlanned(1, SortOrder.Descending, true, ["=3:c", "=2:a", "=1:b"], withCondition: true);
    }

    [JavaOracleFact]
    public void OrderWithoutIndexFallsBackToTheScan()
    {
        RunPlanned(2, SortOrder.Ascending, false, ["=2:a", "=1:b", "=3:c"], withCondition: false);
    }
}
