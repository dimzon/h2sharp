/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What an unfixed query costs, checked against the reference rather than derived here.
 *
 * When no mask fixes a column the planner never leaves the scan: the scan is priced first
 * and the loop over the other indexes is skipped. When one equality fixes a column the masks
 * are built from the conditions and the heavily-fixed table is discounted earlier. The numbers
 * below are what the reference answers for the same empty table, probed on the JVM, and they
 * travel as raw bits because Java and .NET print doubles differently while agreeing on what
 * they mean.
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
public sealed class TableBestPlanTests
{
    private static SessionLocal NewSession()
    {
        return new SessionLocal(new Database(ConnectionInfo.Memory("best_plan_tests")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

    private const string JavaSetup = """
        java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:filter_plan");
        java.sql.Statement stat = conn.createStatement();
        stat.execute("CREATE TABLE T(ID INT, NAME VARCHAR(255))");
        org.h2.jdbc.JdbcConnection c = (org.h2.jdbc.JdbcConnection) conn;
        org.h2.engine.SessionLocal session = (org.h2.engine.SessionLocal) c.getSession();
        org.h2.table.Table table = session.getDatabase().getSchema("PUBLIC").getTableOrView(session, "T");
        org.h2.table.TableFilter f = new org.h2.table.TableFilter(session, table, "T", true, null, 0, null);
        """;

    private const string JavaBody = """
        java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:best_plan");
        java.sql.Statement stat = conn.createStatement();
        stat.execute("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(255))");
        org.h2.jdbc.JdbcConnection c = (org.h2.jdbc.JdbcConnection) conn;
        org.h2.engine.SessionLocal session = (org.h2.engine.SessionLocal) c.getSession();
        org.h2.table.Table table = session.getDatabase().getSchema("PUBLIC").getTableOrView(session, "T");
        org.h2.table.PlanItem item = table.getBestPlanItem(session, null, null, 0, null, null, true);
        System.out.println("=" + Double.doubleToLongBits(item.cost));
        conn.close();
        """;

    [JavaOracleFact]
    public void AnUnfixedQueryKeepsTheScan()
    {
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            Column id = new("ID", TypeInfo.TypeInteger, null, 0);
            Column name = new("NAME", TypeInfo.TypeVarchar, null, 1);
            MVTable table = new(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T", [id, name], persistIndexes: false, persistData: false);
            foreach (Column column in table.GetColumns())
            {
                column.SetTable(table, column.GetColumnId());
            }

            PlanItem plan = table.GetBestPlanItem(session, null, null, 0, null, null, isSelectCommand: true);

            // The scan priced first is never displaced: with no masks the reference skips the
            // loop over the other indexes, so the plan holds the scan whatever else exists.
            Assert.Same(table.GetScanIndex(session), plan.GetIndex());

            // The cost lives in package-private PlanItem.cost on the Java side, which is why
            // the snippet runs inside org.h2.table rather than the default package.
            List<string> java = OracleRunner.Run([], JavaBody, null, null, "org.h2.table");
            Assert.Equal(java, ["=" + BitConverter.DoubleToInt64Bits(plan.Cost).ToString()]);
        }
        finally
        {
            store.Dispose();
        }
    }

    private static MVTable NewTable(SessionLocal session, TransactionStore store, out Column id)
    {
        id = new("ID", TypeInfo.TypeInteger, null, 0);
        Column name = new("NAME", TypeInfo.TypeVarchar, null, 1);
        MVTable table = new(
            session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
            3, "T", [id, name], persistIndexes: false, persistData: false);
        foreach (Column column in table.GetColumns())
        {
            column.SetTable(table, column.GetColumnId());
        }

        return table;
    }

    [JavaOracleFact]
    public void AFilterWithNoConditionsKeepsTheScan()
    {
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewTable(session, store, out _);
            TableFilter f = new(session, table, "T", rightsChecked: true, null, 0, null);

            PlanItem plan = f.GetBestPlanItem(session, [f], 0, null, isSelectCommand: true);

            // Nothing is fixed, so the scan-only fallback stands: the mask-priced plan prices
            // the same scan and cannot beat it.
            Assert.Same(table.GetScanIndex(session), plan.GetIndex());

            const string javaBody = JavaSetup + """
                org.h2.table.PlanItem item = f.getBestPlanItem(
                    session, new org.h2.table.TableFilter[]{ f }, 0, null, true);
                System.out.println("=" + Double.doubleToLongBits(item.cost));
                conn.close();
                """;
            List<string> java = OracleRunner.Run([], javaBody, null, null, "org.h2.table");
            Assert.Equal(java, ["=" + BitConverter.DoubleToInt64Bits(plan.Cost).ToString()]);
        }
        finally
        {
            store.Dispose();
        }
    }

    /// <summary>
    /// A fixed column on a single-index table: the masks are built but the scan still stands.
    /// </summary>
    /// <remarks>
    /// No primary key on either side, on purpose. With a primary key the reference's table
    /// holds two indexes — the scan plus the delegate <c>addIndex</c> wraps the key in — and
    /// the delegate wins the fixed column. That path waits for <c>addIndex</c>, which is not
    /// ported; what is proved here is everything before it: the mask says EQUALITY on column
    /// 0 on both sides, the single index is the scan and is skipped, and the discount for the
    /// one condition lands on the same bits.
    /// </remarks>
    [JavaOracleFact]
    public void AFixedColumnOnASingleIndexTableStillScans()
    {
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            MVTable table = NewTable(session, store, out Column id);
            TableFilter f = new(session, table, "T", rightsChecked: true, null, 0, null);

            var column = new ExpressionColumn(id, f, 0);
            var five = ValueExpression.Get(ValueInteger.Get(5));
            f.AddIndexCondition(IndexCondition.Get(Comparison.Equal, column, five));

            PlanItem plan = f.GetBestPlanItem(session, [f], 0, null, isSelectCommand: true);

            // The equality fixed column 0 and nothing else; the only index is the scan, so it
            // stands, discounted for the one condition.
            Assert.Equal([1, 0], plan.GetMasks()!);
            Assert.Same(table.GetScanIndex(session), plan.GetIndex());

            const string javaBody = JavaSetup + """
                org.h2.expression.ExpressionColumn ec = new org.h2.expression.ExpressionColumn(
                    session.getDatabase(), table.getColumn("ID"));
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
            List<string> java = OracleRunner.Run([], javaBody, null, null, "org.h2.table");
            Assert.Equal(java, ["=" + BitConverter.DoubleToInt64Bits(plan.Cost).ToString()]);
        }
        finally
        {
            store.Dispose();
        }
    }
}
