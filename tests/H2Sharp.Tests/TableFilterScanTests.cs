/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What a filter walk answers, checked against the reference mirror to mirror.
 *
 * Both sides hand-build the same table and walk it through the same filter flow: plan,
 * plan item, prepare, start, next. The scan has preset keys out of insertion order, so key
 * order is observable rather than implied; the lookup fixes one integer key through a
 * delegate built by addIndex, which is the first walk that prices below a scan.
 */
using System;
using System.Collections.Generic;
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
public sealed class TableFilterScanTests
{
    private static SessionLocal NewSession(string name)
    {
        return new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:" + name)), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

    private static MVTable NewTable(SessionLocal session, TransactionStore store, int id)
    {
        Column colId = new("ID", TypeInfo.TypeInteger, null, 0);
        Column colName = new("NAME", TypeInfo.TypeVarchar, null, 1);
        MVTable table = new(
            session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
            id, "T", [colId, colName], store, persistData: false);
        foreach (Column column in table.GetColumns())
        {
            column.SetTable(table, column.GetColumnId());
        }

        return table;
    }

    private static DefaultRow Row(int id, string name, long key)
    {
        DefaultRow row = new([ValueInteger.Get(id), ValueVarchar.Get(name)]);
        row.SetKey(key);
        return row;
    }

    private const string JavaSetup = """
        java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:filter_scan");
        org.h2.jdbc.JdbcConnection c = (org.h2.jdbc.JdbcConnection) conn;
        org.h2.engine.SessionLocal session = (org.h2.engine.SessionLocal) c.getSession();
        org.h2.engine.Database db = session.getDatabase();
        org.h2.command.ddl.CreateTableData data = new org.h2.command.ddl.CreateTableData();
        data.schema = db.getSchema("PUBLIC");
        data.id = 0;
        data.tableName = "T";
        data.columns.add(new org.h2.table.Column("ID", org.h2.value.TypeInfo.TYPE_INTEGER));
        data.columns.add(new org.h2.table.Column("NAME", org.h2.value.TypeInfo.TYPE_VARCHAR));
        data.persistIndexes = false;
        data.persistData = false;
        org.h2.mvstore.db.MVTable table = new org.h2.mvstore.db.MVTable(data, db.getStore());
        """;

    private const string JavaWalk = """
        org.h2.table.TableFilter f = new org.h2.table.TableFilter(session, table, "T", true, null, 0, null);
        CONDS
        org.h2.table.PlanItem item = f.getBestPlanItem(session,
            new org.h2.table.TableFilter[]{ f }, 0, null, true);
        f.setPlanItem(item);
        f.prepare();
        f.startQuery(session);
        while (f.next()) {
            System.out.println("=" + f.getValue(table.getColumn("ID")) + ":"
                + f.getValue(table.getColumn("NAME")));
        }
        conn.close();
        """;

    [JavaOracleFact]
    public void AScanReadsRowsInKeyOrder()
    {
        SessionLocal session = NewSession("filter_scan");
        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewTable(session, store, 0);
            table.AddRow(session, Row(3, "c", 30));
            table.AddRow(session, Row(1, "a", 10));
            table.AddRow(session, Row(2, "b", 20));

            List<string> actual = Walk(table, session, null);

            string javaWalk = JavaWalk.Replace("CONDS", string.Empty);
            List<string> java = OracleRunner.Run([], JavaSetup + JavaRows + javaWalk, null, null, "org.h2.table");
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    [JavaOracleFact]
    public void AnEqualityLookupReadsItsRow()
    {
        SessionLocal session = NewSession("filter_lookup");
        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewTable(session, store, 0);
            foreach (Column column in table.GetColumns())
            {
                if (column.GetName() == "ID")
                {
                    column.SetNullable(false);
                }
            }

            table.AddIndex(session, "I", 7, [new IndexColumn(table.GetColumn(0), ascending: true)], 1,
                IndexType.CreatePrimaryKey(persistent: false, hash: false), create: true, indexComment: null);
            table.AddRow(session, Row(3, "c", 0));
            table.AddRow(session, Row(1, "a", 0));
            table.AddRow(session, Row(2, "b", 0));

            TableFilter f = NewFilter(session, table);
            var ec = new ExpressionColumn(table.GetColumn(0), f, 0);
            var two = ValueExpression.Get(ValueInteger.Get(2));
            f.AddIndexCondition(IndexCondition.Get(Comparison.Equal, ec, two));
            List<string> actual = WalkPlanned(f, session);

            const string javaConds = """
                org.h2.expression.ExpressionColumn ec = new org.h2.expression.ExpressionColumn(
                    session.getDatabase(), null, "T", "ID");
                ec.mapColumns(f, 0, org.h2.expression.Expression.MAP_INITIAL);
                org.h2.expression.Expression two = org.h2.expression.ValueExpression.get(
                    org.h2.value.ValueInteger.get(2));
                f.addIndexCondition(org.h2.index.IndexCondition.get(
                    org.h2.expression.condition.Comparison.EQUAL, ec, two));
                """;
            string javaWalk = JavaWalk.Replace("CONDS", javaConds);
            List<string> java = OracleRunner.Run(
                [], JavaSetup + JavaNotNull + JavaRowsPk + JavaAddIndex + javaWalk, null, null, "org.h2.table");
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    private const string JavaRows = """
        org.h2.result.Row r1 = new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(3), org.h2.value.ValueVarchar.get("c") });
        r1.setKey(30);
        table.addRow(session, r1);
        org.h2.result.Row r2 = new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(1), org.h2.value.ValueVarchar.get("a") });
        r2.setKey(10);
        table.addRow(session, r2);
        org.h2.result.Row r3 = new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(2), org.h2.value.ValueVarchar.get("b") });
        r3.setKey(20);
        table.addRow(session, r3);
        """;

    private const string JavaNotNull = """
        table.getColumn("ID").setNullable(false);
        """;

    private const string JavaRowsPk = """
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(3), org.h2.value.ValueVarchar.get("c") }));
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(1), org.h2.value.ValueVarchar.get("a") }));
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(2), org.h2.value.ValueVarchar.get("b") }));
        """;

    private const string JavaAddIndex = """
        org.h2.table.IndexColumn[] cols = new org.h2.table.IndexColumn[]{
            new org.h2.table.IndexColumn(table.getColumn("ID")) };
        table.addIndex(session, "I", 7, cols, 1,
            org.h2.index.IndexType.createPrimaryKey(false, false), true, null);
        """;

    private static TableFilter NewFilter(SessionLocal session, MVTable table)
        => new(session, table, "T", rightsChecked: true, null, 0, null);

    private static List<string> Walk(MVTable table, SessionLocal session, Action<TableFilter>? shape)
    {
        TableFilter f = NewFilter(session, table);
        shape?.Invoke(f);
        return WalkPlanned(f, session);
    }

    private static List<string> WalkPlanned(TableFilter f, SessionLocal session)
    {
        PlanItem plan = f.GetBestPlanItem(session, [f], 0, null, isSelectCommand: true);
        f.SetPlanItem(plan);
        f.Prepare();
        f.StartQuery(session);
        List<string> rows = [];
        while (f.Next())
        {
            rows.Add("=" + f.GetValue(ColumnByName(f, "ID")) + ":" + f.GetValue(ColumnByName(f, "NAME")));
        }

        return rows;
    }

    private static Column ColumnByName(TableFilter f, string name)
    {
        foreach (Column column in f.GetTable().GetColumns())
        {
            if (column.GetName() == name)
            {
                return column;
            }
        }

        throw new InvalidOperationException("no column " + name);
    }
}
