// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/datatypes.html — other data types, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Datatypes;

public sealed class OtherTypeTests
{
    /// <summary>docs/datatypes.html:687 — { JAVA_OBJECT | OBJECT | OTHER } [ ( lengthInt ) ]</summary>
    [Fact]
    public void JavaObject_SupportsAllSpellings() // TYP-0687
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE TABLE TEST(A JAVA_OBJECT, B OBJECT, C OTHER)");

        Assert.Equal(3, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TEST'"));
    }

    /// <summary>docs/datatypes.html:705 — ENUM (string [, ... ])</summary>
    [Fact]
    public void Enum_HoldsTheListedValues() // TYP-0705
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(A ENUM('a', 'b'))");
        _ = connection.Execute("INSERT INTO TEST(A) VALUES ('a')");

        Assert.Equal("a", connection.QuerySingle<string>("SELECT A FROM TEST"));
    }

    /// <summary>docs/datatypes.html:723 — GEOMETRY</summary>
    [Fact]
    public void Geometry_HoldsGeometries() // TYP-0723
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT CAST('POINT(1 2)' AS GEOMETRY) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:724 — [({ GEOMETRY |</summary>
    [Fact]
    public void Geometry_WithGeometrySubtype() // TYP-0724
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT CAST('POINT(1 2)' AS GEOMETRY(GEOMETRY)) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:725 — { POINT</summary>
    [Fact]
    public void Geometry_WithPointSubtype() // TYP-0725
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT CAST('POINT(1 2)' AS GEOMETRY(POINT)) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:726 — | LINESTRING</summary>
    [Fact]
    public void Geometry_WithLineStringSubtype() // TYP-0726
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>(
            "SELECT CAST('LINESTRING(1 2, 3 4)' AS GEOMETRY(LINESTRING)) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:727 — | POLYGON</summary>
    [Fact]
    public void Geometry_WithPolygonSubtype() // TYP-0727
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>(
            "SELECT CAST('POLYGON((0 0, 1 0, 1 1, 0 0))' AS GEOMETRY(POLYGON)) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:728 — | MULTIPOINT</summary>
    [Fact]
    public void Geometry_WithMultiPointSubtype() // TYP-0728
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>(
            "SELECT CAST('MULTIPOINT((1 2), (3 4))' AS GEOMETRY(MULTIPOINT)) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:729 — | MULTILINESTRING</summary>
    [Fact]
    public void Geometry_WithMultiLineStringSubtype() // TYP-0729
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>(
            "SELECT CAST('MULTILINESTRING((1 2, 3 4))' AS GEOMETRY(MULTILINESTRING)) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:730 — | MULTIPOLYGON</summary>
    [Fact]
    public void Geometry_WithMultiPolygonSubtype() // TYP-0730
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>(
            "SELECT CAST('MULTIPOLYGON(((0 0, 1 0, 1 1, 0 0)))' AS GEOMETRY(MULTIPOLYGON)) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:731 — | GEOMETRYCOLLECTION } [Z|M|ZM]}</summary>
    [Fact]
    public void Geometry_WithCollectionAndZSubtypes() // TYP-0731
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>(
            "SELECT CAST('GEOMETRYCOLLECTION(POINT(1 2))' AS GEOMETRY(GEOMETRYCOLLECTION)) IS NOT NULL"));
        Assert.True(connection.QuerySingle<bool>(
            "SELECT CAST('POINT Z(1 2 3)' AS GEOMETRY(POINT Z)) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:732 — [, sridInt] )]</summary>
    [Fact]
    public void Geometry_WithSrid() // TYP-0732
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>(
            "SELECT CAST('POINT(1 2)' AS GEOMETRY(POINT, 4326)) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:759 — JSON [(lengthInt)]</summary>
    [Fact]
    public void Json_HoldsJsonValues() // TYP-0759
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT CAST('{\"a\":1}' AS JSON) IS NOT NULL"));
        Assert.True(connection.QuerySingle<bool>("SELECT CAST('[]' AS JSON(100)) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:777 — UUID</summary>
    [Fact]
    public void Uuid_HoldsUuids() // TYP-0777
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT CAST(RANDOM_UUID() AS UUID) IS NOT NULL"));
        Assert.True(connection.QuerySingle<bool>(
            "SELECT CAST('12345678-1234-1234-1234-123456789012' AS UUID) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:795 — baseDataType ARRAY [ '[' maximumCardinalityInt ']' ]</summary>
    [Fact]
    public void Array_WithAndWithoutMaximumCardinality() // TYP-0795
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT CARDINALITY(CAST(ARRAY[1, 2] AS INT ARRAY))"));
        Assert.True(connection.QuerySingle<bool>("SELECT CAST(ARRAY[1, 2] AS INT ARRAY[5]) IS NOT NULL"));
    }

    /// <summary>docs/datatypes.html:813 — ROW (fieldName dataType [,...])</summary>
    [Fact]
    public void Row_HoldsNamedFields() // TYP-0813
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(A ROW(B INT, C VARCHAR(10)))");
        _ = connection.Execute("INSERT INTO TEST(A) VALUES (ROW(1, 'a'))");

        Assert.True(connection.QuerySingle<bool>("SELECT A IS NOT NULL FROM TEST"));
    }
}
