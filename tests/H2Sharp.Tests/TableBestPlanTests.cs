/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What an unfixed query costs, checked against the reference rather than derived here.
 *
 * When no mask fixes a column the planner never leaves the scan: the scan is priced first
 * and the loop over the other indexes is skipped. The number below is what the reference
 * answers for the same empty table, probed on the JVM, and it travels as raw bits because
 * Java and .NET print doubles differently while agreeing on what they mean.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
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
        return new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:best_plan_tests")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

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
                3, "T", [id, name], store, persistData: false);
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
}
