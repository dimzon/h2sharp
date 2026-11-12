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
using H2Sharp.Tables;
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
    public void AUniqueIndexOnGeometrySeesUncommittedNeighbors()
    {
        // Not a spatial index: H2 2.5's CREATE UNIQUE INDEX builds IndexType.createUnique,
        // which carries no spatial flag, so this is the regular unique index over geometry.
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

    [Fact]
    public void TheUnsupportedConfigurationsAreRefused()
    {
        Execute("CREATE TABLE BAD(ID INT PRIMARY KEY, G GEOMETRY, H GEOMETRY)");
        Assert.Throws<H2SharpException>(
            () => Execute("CREATE SPATIAL INDEX IDX_BAD ON BAD(G, H)"));
        Assert.Throws<H2SharpException>(
            () => Execute("CREATE SPATIAL INDEX IDX_DESC ON G(THE_GEOM DESC)"));
        Assert.Throws<H2SharpException>(
            () => Execute("CREATE SPATIAL INDEX IDX_NF ON G(THE_GEOM NULLS FIRST)"));
        Assert.Throws<H2SharpException>(
            () => Execute("CREATE SPATIAL INDEX IDX_NL ON G(THE_GEOM NULLS LAST)"));
        Assert.Throws<H2SharpException>(
            () => Execute("CREATE SPATIAL INDEX IDX_NG ON G(ID)"));
    }

    /// <summary>
    /// The constructor guards themselves: the SQL parser rejects some of these forms before the
    /// index is built, so the messages that prove the guard ran are checked here directly.
    /// </summary>
    [Fact]
    public void TheConstructorGuardsRefuseUnsupportedColumns()
    {
        MVTable table = (MVTable)database.GetSchema("PUBLIC").GetTableOrView(null, "G");
        Column geom = table.GetColumn("THE_GEOM");
        Column id = table.GetColumn("ID");
        IndexType type = IndexType.CreateNonUnique(persistent: false, hash: false, spatial: true);

        H2SharpException many = Assert.Throws<H2SharpException>(() => new MVSpatialIndex(
            database, table, 900001, "DIRECT_MANY",
            new[] { new IndexColumn(geom), new IndexColumn(id) }, 0, type));
        Assert.Contains("Can only index one column", many.Message, StringComparison.Ordinal);

        H2SharpException descending = Assert.Throws<H2SharpException>(() => new MVSpatialIndex(
            database, table, 900002, "DIRECT_DESC",
            new[] { new IndexColumn(geom, SortOrder.Descending) }, 0, type));
        Assert.Contains("Cannot index in descending order", descending.Message, StringComparison.Ordinal);

        H2SharpException nullsFirst = Assert.Throws<H2SharpException>(() => new MVSpatialIndex(
            database, table, 900003, "DIRECT_NF",
            new[] { new IndexColumn(geom, SortOrder.NullsFirst) }, 0, type));
        Assert.Contains("Nulls first is not supported", nullsFirst.Message, StringComparison.Ordinal);

        H2SharpException nullsLast = Assert.Throws<H2SharpException>(() => new MVSpatialIndex(
            database, table, 900004, "DIRECT_NL",
            new[] { new IndexColumn(geom, SortOrder.NullsLast) }, 0, type));
        Assert.Contains("Nulls last is not supported", nullsLast.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A unique spatial index only exists when it is built directly: H2 2.5's CREATE UNIQUE
    /// INDEX never sets the spatial flag, so this path has no SQL form. The reference's
    /// contained-key search is strict (a key equal to the filter is not "inside" it), so the
    /// duplicate branches are unreachable; the walk over a containing neighbor is not.
    /// </summary>
    [Fact]
    public void AUniqueSpatialIndexWalksItsNeighbors()
    {
        Execute("CREATE TABLE US(ID INT PRIMARY KEY, G GEOMETRY)");
        Execute("INSERT INTO US VALUES (1, 'POINT (1 1)')");
        MVTable table = (MVTable)database.GetSchema("PUBLIC").GetTableOrView(null, "US");
        MVSpatialIndex unique = NewUniqueSpatialIndex(table, 900011);

        Row point = (Row)table.GetRow(session, 1)!;
        unique.Add(session, point);
        session.Commit(false);

        // A polygon strictly containing the stored point is a neighbor the contained-key
        // cursor yields; it is not equal, so the loop walks past it and the insert succeeds.
        Execute("INSERT INTO US VALUES (2, 'POLYGON ((0 0, 0 2, 2 2, 2 0, 0 0))')");
        Row polygon = (Row)table.GetRow(session, 2)!;
        unique.Add(session, polygon);

        Assert.Equal(2L, unique.GetRowCount(session));
    }

    [Fact]
    public void AddingToAClosedSpatialIndexReportsTheStoreError()
    {
        Execute("CREATE TABLE CA(ID INT PRIMARY KEY, G GEOMETRY)");
        Execute("INSERT INTO CA VALUES (1, 'POINT (1 1)')");
        MVTable table = (MVTable)database.GetSchema("PUBLIC").GetTableOrView(null, "CA");
        MVSpatialIndex closing = NewUniqueSpatialIndex(table, 900012);
        Row row = (Row)table.GetRow(session, 1)!;

        closing.Remove(session);
        Assert.ThrowsAny<H2SharpException>(() => closing.Add(session, row));
    }

    [Fact]
    public void RemovingFromAClosedSpatialIndexReportsTheStoreError()
    {
        Execute("CREATE TABLE CR(ID INT PRIMARY KEY, G GEOMETRY)");
        Execute("INSERT INTO CR VALUES (1, 'POINT (1 1)')");
        MVTable table = (MVTable)database.GetSchema("PUBLIC").GetTableOrView(null, "CR");
        MVSpatialIndex closing = NewUniqueSpatialIndex(table, 900013);
        Row row = (Row)table.GetRow(session, 1)!;

        closing.Remove(session);
        Assert.ThrowsAny<H2SharpException>(() => closing.Remove(session, row));
    }

    [Fact]
    public void RemovingAMissingKeyReportsRowNotFound()
    {
        MVTable table = (MVTable)database.GetSchema("PUBLIC").GetTableOrView(null, "G");
        Row row = table.GetTemplateRow();
        row.SetKey(999);
        row.SetValue(table.GetColumn("THE_GEOM").GetColumnId(), ValueGeometry.Get("POINT (2 2)"));

        H2SharpException notFound = Assert.Throws<H2SharpException>(() => index.Remove(session, row));
        Assert.Equal(Api.ErrorCode.RowNotFoundWhenDeleting1, notFound.H2ErrorCode);
    }

    [Fact]
    public void FindByGeometryWithAnIntersectionFiltersRows()
    {
        MVTable table = (MVTable)database.GetSchema("PUBLIC").GetTableOrView(null, "G");
        Row intersection = table.GetTemplateRow();
        intersection.SetValue(table.GetColumn("THE_GEOM").GetColumnId(),
            ValueGeometry.Get("POLYGON ((0 0, 0 10, 10 10, 10 0, 0 0))"));

        Cursor? cursor = index.FindByGeometry(session, null, null, false, intersection);
        Assert.NotNull(cursor);
        Assert.True(cursor!.Next());
        Assert.NotNull(cursor.Get());
        Assert.False(cursor.Next());
    }

    [Fact]
    public void TruncateEmptiesTheIndex()
    {
        Execute("TRUNCATE TABLE G");
        Assert.Equal(0L, index.GetRowCount(session));
    }

    private MVSpatialIndex NewUniqueSpatialIndex(MVTable table, int id)
    {
        Column geom = table.GetColumn("G");
        return new MVSpatialIndex(database, table, id, "DIRECT_" + id,
            new[] { new IndexColumn(geom) }, 1,
            IndexType.CreateNonUnique(persistent: false, hash: false, spatial: true));
    }

    [Fact]
    public void TheRowCountApproximationAnswers()
    {
        Assert.True(index.GetRowCountApproximation(session) >= 2L);
    }

    private void Execute(string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        _ = container.ExecuteUpdate(false);
    }
}
