// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// Spatial index creation and intersection scans over geometry points.
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class SpatialIndexCoverageTests
{
    private const string Box = "POLYGON((0 0, 2 0, 2 2, 0 2, 0 0))";

    [Fact]
    public void SpatialIndexAnswersBoundingBoxIntersection()
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenGeo(connection);

        _ = connection.Execute("CREATE SPATIAL INDEX IDXGEO ON GEO(G)");

        List<int> rows = connection.Query<int>(
            $"SELECT ID FROM GEO WHERE G && CAST('{Box}' AS GEOMETRY) ORDER BY ID").AsList();
        Assert.Equal([1], rows);
        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEXES WHERE INDEX_NAME = 'IDXGEO'"));
        string plan = connection.QuerySingle<string>(
            $"EXPLAIN PLAN FOR SELECT ID FROM GEO WHERE G && CAST('{Box}' AS GEOMETRY)");
        Assert.Contains("IDXGEO", plan, StringComparison.Ordinal);

        _ = connection.Execute("DELETE FROM GEO WHERE ID = 3");
        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM GEO"));
        _ = connection.Execute("TRUNCATE TABLE GEO");
        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM GEO"));
        _ = connection.Execute("DROP TABLE GEO");
        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEXES WHERE INDEX_NAME = 'IDXGEO'"));
    }

    [Fact]
    public void IntersectsFunctionUsesTheSameScan()
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenGeo(connection);

        _ = connection.Execute("CREATE SPATIAL INDEX IDXGEO ON GEO(G)");

        List<int> rows = connection.Query<int>(
            $"SELECT ID FROM GEO WHERE INTERSECTS(G, CAST('{Box}' AS GEOMETRY)) ORDER BY ID").AsList();
        Assert.Equal([1], rows);
        List<int> point = connection.Query<int>(
            "SELECT ID FROM GEO WHERE G && CAST('POINT(9 9)' AS GEOMETRY) ORDER BY ID").AsList();
        Assert.Equal([3], point);
    }

    private static void GivenGeo(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE GEO(ID INT PRIMARY KEY, G GEOMETRY)");
        _ = connection.Execute(
            "INSERT INTO GEO VALUES " +
            "(1, CAST('POINT(1 1)' AS GEOMETRY)), " +
            "(2, CAST('POINT(5 5)' AS GEOMETRY)), " +
            "(3, CAST('POINT(9 9)' AS GEOMETRY))");
    }
}
