/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What a delegate over the primary costs, checked against the reference.
 *
 * addIndex wraps a single integer primary key in a delegate rather than a map of its own;
 * the delegate prices the key as a range lookup over the primary's rows. The Java side
 * builds the delegate the way the engine does — a primary key through CREATE TABLE — while
 * this side builds it the way addIndex will: set the main column the decision chose, then
 * construct over it. The decision itself is proved in AddIndexDecisionTests; what is proved
 * here is the pricing and the lookup answers on the built delegate.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
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
public sealed class DelegateIndexTests
{
    private static SessionLocal NewSession()
    {
        return new SessionLocal(new Database(ConnectionInfo.Memory("delegate_tests")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
    }

    private const string JavaBody = """
        java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:delegate");
        java.sql.Statement stat = conn.createStatement();
        stat.execute("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(255))");
        org.h2.jdbc.JdbcConnection c = (org.h2.jdbc.JdbcConnection) conn;
        org.h2.engine.SessionLocal session = (org.h2.engine.SessionLocal) c.getSession();
        org.h2.table.Table table = session.getDatabase().getSchema("PUBLIC").getTableOrView(session, "T");
        for (org.h2.index.Index idx : table.getIndexes()) {
            if (idx instanceof org.h2.mvstore.db.MVDelegateIndex) {
                int[] masks = new int[table.getColumns().length];
                masks[0] = org.h2.index.IndexCondition.EQUALITY;
                double cost = idx.getCost(session, masks, null, 0, null, null, true);
                System.out.println("=" + Double.doubleToLongBits(cost));
                System.out.println("=" + idx.getColumnIndex(table.getColumn("ID")));
                System.out.println("=" + idx.getColumnIndex(table.getColumn("NAME")));
                System.out.println("=" + (idx.needRebuild() ? 1 : 0));
            }
        }
        conn.close();
        """;

    [JavaOracleFact]
    public void ADelegatePricesTheKeyAsARangeOverThePrimary()
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

            // What addIndex does with the decision: keep the key in the primary, delegate to it.
            // The storage primary is the scan index; GetPrimaryKey would throw for this
            // hand-built table, whose key is not a constraint yet.
            MVPrimaryIndex primary = (MVPrimaryIndex)table.GetScanIndex(session)!;
            primary.SetMainIndexColumn(0);
            MVDelegateIndex del = new(
                table, 7, "I", primary, IndexType.CreatePrimaryKey(persistent: false, hash: false));

            int[] masks = new int[table.GetColumns().Length];
            masks[0] = IndexCondition.Equality;
            double cost = del.GetCost(session, masks, null, 0, null, null, isSelectCommand: true);

            List<string> actual =
            [
                "=" + BitConverter.DoubleToInt64Bits(cost),
                "=" + del.GetColumnIndex(id),
                "=" + del.GetColumnIndex(name),
                "=" + (del.NeedRebuild() ? 1 : 0),
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
