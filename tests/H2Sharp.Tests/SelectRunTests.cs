/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What running a flat select answers, checked against the reference running its own.
 *
 * Both sides build the same select over the same keyed table — two columns, one filter,
 * no clauses — plan it, and run it to a materialized result. The rows come out in key
 * order on both sides: the walk, the expressions and the trim agree, or nothing does.
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
public sealed class SelectRunTests
{
    private static SessionLocal NewSession()
    {
        return new SessionLocal(new Database(ConnectionInfo.Memory("select_run")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

    private const string JavaBody = """
        java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:select_run_e2e");
        org.h2.jdbc.JdbcConnection c = (org.h2.jdbc.JdbcConnection) conn;
        org.h2.engine.SessionLocal session = (org.h2.engine.SessionLocal) c.getSession();
        org.h2.engine.Database db = session.getDatabase();
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
            org.h2.value.ValueInteger.get(1), org.h2.value.ValueVarchar.get("a") }));
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(2), org.h2.value.ValueVarchar.get("b") }));
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
        select.preparePlan();
        org.h2.result.ResultInterface r = select.query(-1);
        while (r.next()) {
            org.h2.value.Value[] row = r.currentRow();
            System.out.println("=" + row[0] + ":" + row[1]);
        }
        conn.close();
        """;

    [JavaOracleFact]
    public void FlatSelectReadsRowsInKeyOrder()
    {
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
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
            table.AddRow(session, Row(3, "c"));
            table.AddRow(session, Row(1, "a"));
            table.AddRow(session, Row(2, "b"));

            TableFilter f = new(session, table, "T", rightsChecked: true, null, 0, null);
            Select select = new(session);
            select.AddTableFilter(f, isTop: true);
            select.SetExpressions(
            [
                new ExpressionColumn(id, f, 0),
                new ExpressionColumn(name, f, 0),
            ]);
            select.PreparePlan(session, targetTableFilter: null);
            ResultInterface result = select.Run(-1);

            List<string> actual = [];
            while (result.Next())
            {
                Value?[] row = result.CurrentRow();
                actual.Add("=" + row[0] + ":" + row[1]);
            }

            List<string> java = OracleRunner.Run([], JavaBody, null, null, null);
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    private static DefaultRow Row(int id, string name)
        => new([ValueInteger.Get(id), ValueVarchar.Get(name)]);

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
        table.AddRow(session, Row(3, "c"));
        table.AddRow(session, Row(1, "a"));
        table.AddRow(session, Row(2, "b"));
        return table;
    }

    private static Select NewSelect(SessionLocal session, MVTable table)
    {
        TableFilter f = new(session, table, "T", rightsChecked: true, null, 0, null);
        Select select = new(session);
        select.AddTableFilter(f, isTop: true);
        select.SetExpressions(
        [
            new ExpressionColumn(table.GetColumn(0), f, 0),
            new ExpressionColumn(table.GetColumn(1), f, 1),
        ]);
        select.PreparePlan(session, targetTableFilter: null);
        return select;
    }

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
            org.h2.value.ValueInteger.get(1), org.h2.value.ValueVarchar.get("a") }));
        table.addRow(session, new org.h2.result.DefaultRow(new org.h2.value.Value[]{
            org.h2.value.ValueInteger.get(2), org.h2.value.ValueVarchar.get("b") }));
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
        select.preparePlan();
        """;

    private static List<string> Print(ResultInterface result)
    {
        List<string> rows = [];
        while (result.Next())
        {
            Value?[] row = result.CurrentRow();
            rows.Add("=" + row[0] + ":" + row[1]);
        }

        return rows;
    }

    private const string JavaPrint = """
        while (r.next()) {
            org.h2.value.Value[] row = r.currentRow();
            System.out.println("=" + row[0] + ":" + row[1]);
        }
        conn.close();
        """;

    private const string JavaSession = """
        java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:select_run_e2e");
        org.h2.jdbc.JdbcConnection c = (org.h2.jdbc.JdbcConnection) conn;
        org.h2.engine.SessionLocal session = (org.h2.engine.SessionLocal) c.getSession();
        org.h2.engine.Database db = session.getDatabase();
        """;

    [JavaOracleFact]
    public void FilteredSelectReadsItsRow()
    {
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewKeyedTable(session, store);
            TableFilter f = new(session, table, "T", rightsChecked: true, null, 0, null);
            var ec = new ExpressionColumn(table.GetColumn(0), f, 0);
            var cond = new Comparison(
                Comparison.Equal, ec, ValueExpression.Get(ValueInteger.Get(2)), whenOperand: false);
            Select select = new(session);
            select.AddTableFilter(f, isTop: true);
            select.AddCondition(cond);
            select.SetExpressions(
            [
                new ExpressionColumn(table.GetColumn(0), f, 0),
                new ExpressionColumn(table.GetColumn(1), f, 0),
            ]);
            select.PreparePlan(session, targetTableFilter: null);
            List<string> actual = Print(select.Run(-1));

            const string javaConds = """
                org.h2.expression.ExpressionColumn ec = new org.h2.expression.ExpressionColumn(
                    session.getDatabase(), null, "T", "ID");
                ec.mapColumns(f, 0, org.h2.expression.Expression.MAP_INITIAL);
                org.h2.expression.condition.Comparison cond = new org.h2.expression.condition.Comparison(
                    org.h2.expression.condition.Comparison.EQUAL, ec,
                    org.h2.expression.ValueExpression.get(org.h2.value.ValueInteger.get(2)), false);
                select.addCondition(cond);
                """;
            List<string> java = OracleRunner.Run(
                [], JavaSession + JavaTable + JavaSelect + javaConds + JavaRun + JavaPrint, null, null, null);
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    private const string JavaRun = """
        org.h2.result.ResultInterface r = select.query(-1);
        """;

    [JavaOracleFact]
    public void LimitedSelectReadsItsRows()
    {
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewKeyedTable(session, store);
            Select select = NewSelect(session, table);
            select.PreparePlan(session, targetTableFilter: null);
            List<string> actual = Print(select.Run(2));

            List<string> java = OracleRunner.Run(
                [], JavaSession + JavaTable + JavaSelect + JavaRunLimit + JavaPrint, null, null, null);
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    private const string JavaRunLimit = """
        org.h2.result.ResultInterface r = select.query(2);
        """;
}
