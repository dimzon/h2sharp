/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The members of the spatial index a SQL statement never reaches: the bulk-load refusals,
 * the cursor that only walks forward, the counts, and the bounds read directly off the
 * R-tree.
 */
using System;
using System.Linq;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.MvStore.Db;
using H2Sharp.Result;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class SpatialIndexDirectTests
{
    private readonly SessionLocal session;
    private readonly Database database;
    private readonly MVSpatialIndex index;

    public SpatialIndexDirectTests()
    {
        database = new Database(
            ConnectionInfo.Memory("spatial_direct_" + Guid.NewGuid().ToString("N")));
        session = new SessionLocal(database, "sa");
        Execute("CREATE TABLE G(ID INT PRIMARY KEY, THE_GEOM GEOMETRY)");
        Execute("INSERT INTO G VALUES (1, 'POINT (1 1)'), (2, 'POINT (50 50)')");
        Execute("CREATE SPATIAL INDEX IDX_S ON G(THE_GEOM)");
        index = database.GetSchema("PUBLIC").GetTableOrView(null, "G").GetIndexes()
            .OfType<MVSpatialIndex>().Single();
    }

    /// <summary>
    /// The reference has no bulk-load path for an R-tree: both buffer calls refuse with an
    /// internal error, which this port raises as a plain exception.
    /// </summary>
    [Fact]
    public void TheRowBuffersAreRefused()
    {
        Exception add = Assert.ThrowsAny<Exception>(() => index.AddRowsToBuffer([], "buffer"));
        Assert.Equal("Unexpected code path", add.Message);

        Exception apply = Assert.ThrowsAny<Exception>(() => index.AddBufferedRows([]));
        Assert.Equal("Unexpected code path", apply.Message);
    }

    [Fact]
    public void ClosingReleasesNothing()
    {
        index.Close(session);
    }

    [Fact]
    public void TheMvMapIsATodo()
    {
        TODOException e = Assert.Throws<TODOException>(() => index.GetMVMap());
        Assert.Contains("object-typed", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCursorOnlyGoesForward()
    {
        Cursor? cursor = index.GetCursor(session, null, null, false);
        Assert.NotNull(cursor);
        Assert.True(cursor!.Next());
        Assert.NotNull(cursor.GetSearchRow());
        Assert.NotNull(cursor.Get());
        Assert.True(cursor.Next());
        Assert.False(cursor.Next());

        H2SharpException unsupported = Assert.Throws<H2SharpException>(() => cursor.Previous());
        Assert.Equal(Api.ErrorCode.FeatureNotSupported1, unsupported.H2ErrorCode);
    }

    [Fact]
    public void FindByGeometryWithoutAnIntersectionIsThePlainScan()
    {
        Cursor? cursor = index.FindByGeometry(session, null, null, false, null);
        Assert.NotNull(cursor);
        Assert.True(cursor!.Next());
        Assert.True(cursor.Next());
        Assert.False(cursor.Next());
    }

    [Fact]
    public void TheCountsAnswer()
    {
        Assert.Equal(2L, index.GetRowCount(session));
        Assert.True(index.GetRowCountApproximation(session) >= 2L);
    }

    [Fact]
    public void TheBoundsEncloseTheData()
    {
        // Later keys extend the running envelope in every direction, so both the exact walk
        // and the root-page estimate see the full spread.
        Execute("INSERT INTO G VALUES (3, 'POINT (5 5)'), (4, 'POINT (0 0)')");

        Assert.StartsWith("POLYGON", index.GetBounds(session)!.GetString(), StringComparison.Ordinal);
        Assert.Contains("0", index.GetBounds(session)!.GetString(), StringComparison.Ordinal);
        Assert.Contains("50", index.GetBounds(session)!.GetString(), StringComparison.Ordinal);
        Assert.StartsWith("POLYGON", index.GetEstimatedBounds(session)!.GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyIndexHasNoBounds()
    {
        Execute("CREATE TABLE E(ID INT PRIMARY KEY, THE_GEOM GEOMETRY)");
        Execute("CREATE SPATIAL INDEX IDX_E ON E(THE_GEOM)");
        MVSpatialIndex empty = database.GetSchema("PUBLIC").GetTableOrView(null, "E").GetIndexes()
            .OfType<MVSpatialIndex>().Single();

        Assert.Same(ValueNull.Instance, empty.GetBounds(session));
        Assert.Same(ValueNull.Instance, empty.GetEstimatedBounds(session));
    }

    [Fact]
    public void AUniqueSpatialIndexSeesUncommittedNeighbors()
    {
        SessionLocal second = new(database, "sa");
        Execute("CREATE TABLE U(ID INT PRIMARY KEY, THE_GEOM GEOMETRY)");
        Execute("CREATE UNIQUE NULLS DISTINCT INDEX IDX_U ON U USING RTREE (THE_GEOM)");
        Execute("SET AUTOCOMMIT FALSE");
        Execute("INSERT INTO U VALUES (1, 'POINT (1 1)')");

        // The neighbor is uncommitted: the loser drops its own key and reports a concurrent
        // update rather than a duplicate.
        H2SharpException concurrent = Assert.Throws<H2SharpException>(
            () => Update(second, "INSERT INTO U VALUES (2, 'POINT (1 1)')"));
        Assert.Equal(Api.ErrorCode.ConcurrentUpdate1, concurrent.H2ErrorCode);

        Execute("COMMIT");

        // Committed now: the same insert is a plain duplicate.
        H2SharpException duplicate = Assert.Throws<H2SharpException>(
            () => Update(second, "INSERT INTO U VALUES (2, 'POINT (1 1)')"));
        Assert.Equal(Api.ErrorCode.DuplicateKey1, duplicate.H2ErrorCode);
    }

    [Fact]
    public void RemovingARowWithoutGeometryIsNothing()
    {
        Execute("INSERT INTO G VALUES (3, NULL)");
        MVTable table = (MVTable)database.GetSchema("PUBLIC").GetTableOrView(null, "G");
        Row? row = table.GetRow(session, 3) as Row;

        Assert.NotNull(row);
        index.Remove(session, row!);
    }

    private static void Update(SessionLocal target, string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(target.PrepareCommand(sql));
        _ = container.ExecuteUpdate(false);
    }

    private void Execute(string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        _ = container.ExecuteUpdate(false);
    }
}
