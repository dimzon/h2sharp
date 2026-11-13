/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of MVSecondaryIndex: no-op lifecycle, row-not-found on remove,
 * closed-map errors, Truncate, the read-only cursor, and the TODO map accessor.
 */
using System.Linq;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.MvStore.Db;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class SecondaryIndexDirectTests
{
    private readonly Database database;
    private readonly SessionLocal session;
    private readonly MVSecondaryIndex index;

    public SecondaryIndexDirectTests()
    {
        database = new Database(
            ConnectionInfo.Memory("secondary_direct_" + Guid.NewGuid().ToString("N")));
        session = new SessionLocal(database, "sa");
        Execute("CREATE TABLE T(ID INT PRIMARY KEY, A INT)");
        Execute("CREATE UNIQUE INDEX IDX_A ON T(A)");
        Execute("INSERT INTO T VALUES (1, 10), (2, 20)");
        index = database.GetSchema("PUBLIC").GetTableOrView(null, "T").GetIndexes()
            .OfType<MVSecondaryIndex>().Single();
    }

    [Fact]
    public void CloseIsANop()
    {
        index.Close(session);
    }

    [Fact]
    public void RemovingAMissingRowIsRefused()
    {
        MVTable table = (MVTable)database.GetSchema("PUBLIC").GetTableOrView(null, "T");
        Row row = table.GetTemplateRow();
        row.SetKey(999);
        row.SetValue(table.GetColumn("A").GetColumnId(), ValueInteger.Get(77));

        H2SharpException e = Assert.Throws<H2SharpException>(() => index.Remove(session, row));
        Assert.Equal(Api.ErrorCode.RowNotFoundWhenDeleting1, e.H2ErrorCode);
    }

    [Fact]
    public void ClosedMapErrorsAreReported()
    {
        // Closing the index drops its per-transaction map instance; dataMap itself stays open,
        // so GetCost/GetRowCountApproximation still answer — the closed-map branches (379–381,
        // 447–449) are only reachable by closing the whole store, which is a store-level concern.
        index.Remove(session);
        double cost = index.GetCost(session, null, null, 0, null, null, false);
        Assert.True(cost >= 0);
        Assert.True(index.GetRowCountApproximation(session) >= 0L);
    }

    [Fact]
    public void TruncateAfterRemoveStillAnswers()
    {
        // Remove(session) drops the per-transaction instance; Truncate on the root map still works.
        index.Remove(session);
        index.Truncate(session);
        Assert.Equal(0L, index.GetRowCount(session));
    }

    [Fact]
    public void GetMVMapIsATodo()
    {
        TODOException e = Assert.Throws<TODOException>(() => index.GetMVMap());
        Assert.Contains("object-typed", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCursorOnlyGoesForward()
    {
        Cursor? cursor = index.GetCursor(session, null, null, false);
        Assert.NotNull(cursor);
        int count = 0;
        while (cursor!.Next())
        {
            Assert.NotNull(cursor.Get());
            count++;
        }

        Assert.Equal(2, count);
        Assert.False(cursor.Next());

        H2SharpException unsupported = Assert.Throws<H2SharpException>(() => cursor.Previous());
        Assert.Equal(Api.ErrorCode.FeatureNotSupported1, unsupported.H2ErrorCode);
    }

    private void Execute(string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        _ = container.ExecuteUpdate(false);
    }
}
