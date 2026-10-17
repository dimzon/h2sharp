/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What addIndex builds, checked against the reference mirror to mirror.
 *
 * Both sides hand-build the same table — id zero, one NOT NULL integer column in the key —
 * and call addIndex with the same arguments. Id zero matters: the reference itself exempts
 * such tables from the schema-registry write, so the meta table, which is not ported, stays
 * out of the path on both sides. What is proved is everything before it: the columns are
 * prepared, the key reuses the primary through a delegate, and the planner prices the fixed
 * column through it. The registry write on tables with an id waits for the meta table.
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
using H2Sharp.Tables;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class AddIndexTests
{
    private static SessionLocal NewSession()
    {
        return new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:add_index_tests")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

    private const string JavaBody = """
        java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:add_index_e2e");
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
        org.h2.index.Index added = table.addIndex(session, "I", 7, cols, 1,
            org.h2.index.IndexType.createPrimaryKey(false, false), true, null);
        System.out.println("=" + added.getClass().getSimpleName());
        System.out.println("=" + table.getIndexes().size());
        org.h2.table.TableFilter f = new org.h2.table.TableFilter(session, table, "T", true, null, 0, null);
        org.h2.expression.ExpressionColumn ec = new org.h2.expression.ExpressionColumn(db, table.getColumn("ID"));
        ec.mapColumns(f, 0, org.h2.expression.Expression.MAP_INITIAL);
        org.h2.expression.Expression five = org.h2.expression.ValueExpression.get(
            org.h2.value.ValueInteger.get(5));
        f.addIndexCondition(org.h2.index.IndexCondition.get(
            org.h2.expression.condition.Comparison.EQUAL, ec, five));
        org.h2.table.PlanItem item = f.getBestPlanItem(
            session, new org.h2.table.TableFilter[]{ f }, 0, null, true);
        System.out.println("=" + Double.doubleToLongBits(item.cost));
        conn.close();
        """;

    [JavaOracleFact]
    public void APrimaryKeyReusesThePrimaryThroughADelegate()
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
                0, "T", [id, name], persistData: false);
            foreach (Column column in table.GetColumns())
            {
                column.SetTable(table, column.GetColumnId());
            }

            H2Sharp.Indexes.Index added = table.AddIndex(
                session, "I", 7, [new IndexColumn(id, ascending: true)], 1,
                IndexType.CreatePrimaryKey(persistent: false, hash: false),
                create: true, indexComment: null);

            TableFilter f = new(session, table, "T", rightsChecked: true, null, 0, null);
            var ec = new ExpressionColumn(id, f, 0);
            var five = ValueExpression.Get(ValueInteger.Get(5));
            f.AddIndexCondition(IndexCondition.Get(Comparison.Equal, ec, five));
            PlanItem plan = f.GetBestPlanItem(session, [f], 0, null, isSelectCommand: true);

            List<string> actual =
            [
                "=" + added.GetType().Name,
                "=" + table.GetIndexes().Length,
                "=" + BitConverter.DoubleToInt64Bits(plan.Cost),
            ];
            List<string> java = OracleRunner.Run([], JavaBody, null, null, "org.h2.table");
            Assert.Equal(java, actual);
        }
        finally
        {
            store.Dispose();
        }
    }
}
