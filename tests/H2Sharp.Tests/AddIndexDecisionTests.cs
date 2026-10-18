/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Which key reuses the primary index, checked against the reference.
 *
 * addIndex wraps a single ascending integer primary key in a delegate over the primary
 * index and gives every other key its own map. The decision is this static: the column id
 * for one ascending integer key, the row id for everything else. The six lines below are
 * what the reference answers for the same shapes, probed on the JVM.
 */
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
public sealed class AddIndexDecisionTests
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
        java.sql.Connection conn = java.sql.DriverManager.getConnection("jdbc:h2:mem:add_index");
        java.sql.Statement stat = conn.createStatement();
        stat.execute("CREATE TABLE T(ID INT, N BIGINT, NAME VARCHAR(255))");
        org.h2.jdbc.JdbcConnection c = (org.h2.jdbc.JdbcConnection) conn;
        org.h2.engine.SessionLocal session = (org.h2.engine.SessionLocal) c.getSession();
        org.h2.table.Table table = session.getDatabase().getSchema("PUBLIC").getTableOrView(session, "T");
        org.h2.table.IndexColumn[] oneInt = new org.h2.table.IndexColumn[]{
            new org.h2.table.IndexColumn(table.getColumn("ID")) };
        org.h2.table.IndexColumn[] oneBigint = new org.h2.table.IndexColumn[]{
            new org.h2.table.IndexColumn(table.getColumn("N")) };
        org.h2.table.IndexColumn[] oneVarchar = new org.h2.table.IndexColumn[]{
            new org.h2.table.IndexColumn(table.getColumn("NAME")) };
        org.h2.table.IndexColumn[] two = new org.h2.table.IndexColumn[]{
            new org.h2.table.IndexColumn(table.getColumn("ID")),
            new org.h2.table.IndexColumn(table.getColumn("NAME")) };
        org.h2.table.IndexColumn desc = new org.h2.table.IndexColumn(table.getColumn("ID"));
        desc.sortType = org.h2.result.SortOrder.DESCENDING;
        org.h2.table.IndexColumn[] oneDesc = new org.h2.table.IndexColumn[]{ desc };
        org.h2.index.IndexType pk = org.h2.index.IndexType.createPrimaryKey(false, false);
        org.h2.index.IndexType nonUnique = org.h2.index.IndexType.createNonUnique(false);
        System.out.println("=" + org.h2.table.TableBase.getMainIndexColumn(pk, oneInt));
        System.out.println("=" + org.h2.table.TableBase.getMainIndexColumn(pk, oneBigint));
        System.out.println("=" + org.h2.table.TableBase.getMainIndexColumn(pk, oneVarchar));
        System.out.println("=" + org.h2.table.TableBase.getMainIndexColumn(pk, two));
        System.out.println("=" + org.h2.table.TableBase.getMainIndexColumn(nonUnique, oneInt));
        System.out.println("=" + org.h2.table.TableBase.getMainIndexColumn(pk, oneDesc));
        conn.close();
        """;

    [JavaOracleFact]
    public void OnlyASingleAscendingIntegerKeyReusesThePrimary()
    {
        SessionLocal session = NewSession();

        var store = new TransactionStore(MVStore.Open(null!));
        store.Init();
        try
        {
            Column id = new("ID", TypeInfo.TypeInteger, null, 0);
            Column n = new("N", TypeInfo.TypeBigint, null, 1);
            Column name = new("NAME", TypeInfo.TypeVarchar, null, 2);
            MVTable table = new(
                session.GetDatabase(), new Schemas.Schema(session.GetDatabase(), 1, "PUBLIC", false),
                3, "T", [id, n, name], persistIndexes: false, persistData: false);
            foreach (Column column in table.GetColumns())
            {
                column.SetTable(table, column.GetColumnId());
            }

            IndexType pk = IndexType.CreatePrimaryKey(persistent: false, hash: false);
            IndexType nonUnique = IndexType.CreateNonUnique(persistent: false);
            List<int> actual =
            [
                Table.GetMainIndexColumn(pk, [new IndexColumn(id, ascending: true)]),
                Table.GetMainIndexColumn(pk, [new IndexColumn(n, ascending: true)]),
                Table.GetMainIndexColumn(pk, [new IndexColumn(name, ascending: true)]),
                Table.GetMainIndexColumn(pk, [new IndexColumn(id, ascending: true), new IndexColumn(name, ascending: true)]),
                Table.GetMainIndexColumn(nonUnique, [new IndexColumn(id, ascending: true)]),
                Table.GetMainIndexColumn(pk, [new IndexColumn(id, ascending: false)]),
            ];

            List<string> java = OracleRunner.Run([], JavaBody, null, null, null);
            Assert.Equal(java, actual.ConvertAll(v => "=" + v));
        }
        finally
        {
            store.Dispose();
        }
    }
}
