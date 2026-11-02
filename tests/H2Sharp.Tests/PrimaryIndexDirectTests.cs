/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The members of the primary index a SQL statement never reaches: the no-op lifecycle, the
 * bulk-load and map refusals, the delete and update paths that answer only when a row is
 * missing, and the two-session race for the same primary key.
 */
using System;
using System.Linq;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.MvStore.Db;
using H2Sharp.Result;
using Xunit;

namespace H2Sharp.Tests;

public sealed class PrimaryIndexDirectTests
{
    private readonly Database database;
    private readonly SessionLocal session;
    private readonly MVPrimaryIndex index;

    public PrimaryIndexDirectTests()
    {
        database = new Database(
            ConnectionInfo.Memory("primary_direct_" + Guid.NewGuid().ToString("N")));
        session = new SessionLocal(database, "sa");
        Execute("CREATE TABLE T(ID INT PRIMARY KEY, V VARCHAR(20))");
        Execute("INSERT INTO T VALUES (1, 'one'), (2, 'two')");
        index = database.GetSchema("PUBLIC").GetTableOrView(null, "T").GetIndexes()
            .OfType<MVPrimaryIndex>().Single();
    }

    [Fact]
    public void TheLifecycleAnswersAreFixed()
    {
        index.Close(session);
        Assert.False(index.NeedRebuild());
        Assert.Null(index.GetCreateSql());
        Assert.True(index.IsRowIdIndex());
    }

    [Fact]
    public void TheBulkLoadAndTheMapAreTodos()
    {
        Assert.Throws<TODOException>(() => index.GetMVMap());
        Assert.Throws<TODOException>(() => index.AddRowsToBuffer([], "buffer"));
        Assert.Throws<TODOException>(() => index.AddBufferedRows([]));
    }

    [Fact]
    public void TheCursorOnlyGoesForward()
    {
        Cursor? cursor = index.GetCursor(session, null, null, false);
        Assert.NotNull(cursor);
        Assert.True(cursor!.Next());
        Assert.NotNull(cursor.Get());
        Assert.True(cursor.Next());
        Assert.False(cursor.Next());

        H2SharpException unsupported = Assert.Throws<H2SharpException>(() => cursor.Previous());
        Assert.Equal(Api.ErrorCode.FeatureNotSupported1, unsupported.H2ErrorCode);
    }

    [Fact]
    public void DeletingWhatIsNotThereIsRefused()
    {
        Row row = (Row)database.GetSchema("PUBLIC").GetTableOrView(null, "T")
            .GetRow(session, 1)!;
        row.SetKey(999);

        H2SharpException e = Assert.Throws<H2SharpException>(() => index.Remove(session, row));
        Assert.Equal(Api.ErrorCode.RowNotFoundWhenDeleting1, e.H2ErrorCode);
    }

    [Fact]
    public void UpdatingWhatIsNotThereIsRefused()
    {
        MVTable table = (MVTable)database.GetSchema("PUBLIC").GetTableOrView(null, "T");
        Row oldRow = (Row)table.GetRow(session, 1)!;
        oldRow.SetKey(999);
        Row newRow = (Row)table.GetRow(session, 2)!;
        newRow.SetKey(999);

        H2SharpException e = Assert.Throws<H2SharpException>(
            () => index.Update(session, oldRow, newRow));
        Assert.Equal(Api.ErrorCode.RowNotFoundInPrimaryIndex, e.H2ErrorCode);
    }

    [Fact]
    public void UpdatingAcrossKeysIsAnInternalError()
    {
        MVTable table = (MVTable)database.GetSchema("PUBLIC").GetTableOrView(null, "T");
        Row oldRow = (Row)table.GetRow(session, 1)!;
        Row newRow = (Row)table.GetRow(session, 2)!;

        // The reference raises getInternalError(key + " != " + key), whose message is the
        // detail alone.
        Exception e = Assert.ThrowsAny<Exception>(() => index.Update(session, oldRow, newRow));
        Assert.Equal("1 != 2", e.Message);
    }

    [Fact]
    public void TwoSessionsRaceForTheSamePrimaryKey()
    {
        Execute("SET AUTOCOMMIT FALSE");
        Execute("INSERT INTO T VALUES (7, 'seven')");

        // The neighbor is uncommitted: the loser reports a concurrent update rather than a
        // duplicate.
        SessionLocal second = new(database, "sa");
        H2SharpException concurrent = Assert.Throws<H2SharpException>(
            () => Update(second, "INSERT INTO T VALUES (7, 'other')"));
        Assert.Equal(Api.ErrorCode.ConcurrentUpdate1, concurrent.H2ErrorCode);

        Execute("COMMIT");

        // Committed now: the same insert is a plain duplicate.
        H2SharpException duplicate = Assert.Throws<H2SharpException>(
            () => Update(second, "INSERT INTO T VALUES (7, 'other')"));
        Assert.Equal(Api.ErrorCode.DuplicateKey1, duplicate.H2ErrorCode);
    }

    private void Execute(string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        _ = container.ExecuteUpdate(false);
    }

    private static void Update(SessionLocal target, string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(target.PrepareCommand(sql));
        _ = container.ExecuteUpdate(false);
    }
}
