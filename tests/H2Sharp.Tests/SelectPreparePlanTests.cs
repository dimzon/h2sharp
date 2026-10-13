/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What planning a select answers, checked against the reference driving its own.
 *
 * Both sides build the same select over the same keyed table — one filter, one ready-made
 * index condition — and run the same public preparePlan. The masks on the filter and the
 * cost on the select are the whole observable plan: the same numbers mean the same index
 * won for the same reason on both sides. Turning a WHERE clause into the condition is the
 * expression layer's own turn, not this one's.
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
using H2Sharp.Tables;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SelectPreparePlanTests
{
    private static SessionLocal NewSession()
    {
        return new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:select_plan")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

    private const string JavaBody = """
        java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:select_plan_e2e");
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
        org.h2.table.TableFilter f = new org.h2.table.TableFilter(session, table, "T", true, null, 0, null);
        org.h2.expression.ExpressionColumn ec = new org.h2.expression.ExpressionColumn(
            session.getDatabase(), null, "T", "ID");
        ec.mapColumns(f, 0, org.h2.expression.Expression.MAP_INITIAL);
        f.addIndexCondition(org.h2.index.IndexCondition.get(
            org.h2.expression.condition.Comparison.EQUAL, ec,
            org.h2.expression.ValueExpression.get(org.h2.value.ValueInteger.get(5))));
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
        int[] masks = f.getMasks();
        System.out.println("=masks:" + masks[0] + "," + masks[1]);
        System.out.println("=cost:" + (long) select.getCost());
        conn.close();
        """;

    [JavaOracleFact]
    public void PreparePlanPricesTheLookup()
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
                0, "T", [id, name], store, persistData: false);
            foreach (Column column in table.GetColumns())
            {
                column.SetTable(table, column.GetColumnId());
            }

            table.AddIndex(session, "I", 7, [new IndexColumn(id, ascending: true)], 1,
                IndexType.CreatePrimaryKey(persistent: false, hash: false), create: true, indexComment: null);

            TableFilter f = new(session, table, "T", rightsChecked: true, null, 0, null);
            var ec = new ExpressionColumn(id, f, 0);
            f.AddIndexCondition(IndexCondition.Get(
                Comparison.Equal, ec, ValueExpression.Get(ValueInteger.Get(5))));
            Select select = new(session);
            select.AddTableFilter(f, isTop: true);
            select.SetExpressions(
            [
                new ExpressionColumn(id, f, 0),
                new ExpressionColumn(name, f, 1),
            ]);
            select.PreparePlan(session, targetTableFilter: null);

            int[] masks = f.GetMasks()!;
            List<string> actual =
            [
                "=masks:" + masks[0] + "," + masks[1],
                "=cost:" + select.GetCost(),
            ];
            List<string> java = OracleRunner.Run([], JavaBody, null, null, null);
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }

    /// <summary>
    /// A WHERE clause becomes the index condition on its own: the same masks and the same
    /// cost as the hand-built condition, through the expression layer rather than around it.
    /// </summary>
    [JavaOracleFact]
    public void WhereBecomesTheIndexCondition()
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
                0, "T", [id, name], store, persistData: false);
            foreach (Column column in table.GetColumns())
            {
                column.SetTable(table, column.GetColumnId());
            }

            table.AddIndex(session, "I", 7, [new IndexColumn(id, ascending: true)], 1,
                IndexType.CreatePrimaryKey(persistent: false, hash: false), create: true, indexComment: null);

            TableFilter f = new(session, table, "T", rightsChecked: true, null, 0, null);
            var ec = new ExpressionColumn(id, f, 0);
            var cond = new Comparison(
                Comparison.Equal, ec, ValueExpression.Get(ValueInteger.Get(5)), whenOperand: false);
            Select select = new(session);
            select.AddTableFilter(f, isTop: true);
            select.AddCondition(cond);
            select.SetExpressions(
            [
                new ExpressionColumn(id, f, 0),
                new ExpressionColumn(name, f, 1),
            ]);
            select.PreparePlan(session, targetTableFilter: null);

            int[] masks = f.GetMasks()!;
            List<string> actual =
            [
                "=masks:" + masks[0] + "," + masks[1],
                "=cost:" + select.GetCost(),
            ];

            const string javaBody = """
                java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:select_where");
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
                org.h2.table.TableFilter f = new org.h2.table.TableFilter(session, table, "T", true, null, 0, null);
                org.h2.expression.ExpressionColumn ec = new org.h2.expression.ExpressionColumn(
                    session.getDatabase(), null, "T", "ID");
                ec.mapColumns(f, 0, org.h2.expression.Expression.MAP_INITIAL);
                org.h2.expression.condition.Comparison cond = new org.h2.expression.condition.Comparison(
                    org.h2.expression.condition.Comparison.EQUAL, ec,
                    org.h2.expression.ValueExpression.get(org.h2.value.ValueInteger.get(5)), false);
                org.h2.command.query.Select select = new org.h2.command.query.Select(session, null);
                select.addTableFilter(f, true);
                select.addCondition(cond);
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
                int[] masks = f.getMasks();
                System.out.println("=masks:" + masks[0] + "," + masks[1]);
                System.out.println("=cost:" + (long) select.getCost());
                conn.close();
                """;
            List<string> java = OracleRunner.Run([], javaBody, null, null, null);
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }
}
