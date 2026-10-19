// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/datatypes.html — boolean and numeric data types, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Datatypes;

public sealed class NumericTypeTests
{
    /// <summary>docs/datatypes.html:407 — BOOLEAN</summary>
    [Fact]
    public void Boolean_AcceptsLogicalValues() // TYP-0407
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT CAST(1 AS BOOLEAN)"));
        Assert.True(connection.QuerySingle<bool>("SELECT CAST('TRUE' AS BOOLEAN)"));
    }

    /// <summary>docs/datatypes.html:425 — TINYINT</summary>
    [Fact]
    public void TinyInt_HoldsOneByte() // TYP-0425
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal((sbyte)1, connection.QuerySingle<sbyte>("SELECT CAST(1 AS TINYINT)"));
    }

    /// <summary>docs/datatypes.html:443 — SMALLINT</summary>
    [Fact]
    public void SmallInt_HoldsTwoBytes() // TYP-0443
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal((short)1, connection.QuerySingle<short>("SELECT CAST(1 AS SMALLINT)"));
    }

    /// <summary>docs/datatypes.html:461 — INTEGER | INT</summary>
    [Fact]
    public void Integer_SupportsBothSpellings() // TYP-0461
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT CAST(1 AS INTEGER)"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT CAST(1 AS INT)"));
    }

    /// <summary>docs/datatypes.html:479 — BIGINT</summary>
    [Fact]
    public void BigInt_HoldsEightBytes() // TYP-0479
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1L, connection.QuerySingle<long>("SELECT CAST(1 AS BIGINT)"));
    }

    /// <summary>docs/datatypes.html:497 — { NUMERIC | DECIMAL | DEC } [ ( precisionInt [ , scaleInt ] ) ]</summary>
    [Fact]
    public void Numeric_SupportsAllSpellingsAndPrecision() // TYP-0497
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1.25m, connection.QuerySingle<decimal>("SELECT CAST(1.25 AS NUMERIC(3, 2))"));
        Assert.Equal(1m, connection.QuerySingle<decimal>("SELECT CAST(1 AS DECIMAL)"));
        Assert.Equal(1m, connection.QuerySingle<decimal>("SELECT CAST(1 AS DEC)"));
    }

    /// <summary>docs/datatypes.html:515 — REAL | FLOAT ( precisionInt )</summary>
    [Fact]
    public void Real_SupportsFloatSpelling() // TYP-0515
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1.5, connection.QuerySingle<double>("SELECT CAST(1.5 AS REAL)"), 10);
        Assert.Equal(1.5, connection.QuerySingle<double>("SELECT CAST(1.5 AS FLOAT(24))"), 10);
    }

    /// <summary>docs/datatypes.html:533 — DOUBLE PRECISION | FLOAT [ ( precisionInt ) ]</summary>
    [Fact]
    public void DoublePrecision_SupportsFloatSpelling() // TYP-0533
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1.5, connection.QuerySingle<double>("SELECT CAST(1.5 AS DOUBLE PRECISION)"), 10);
        Assert.Equal(1.5, connection.QuerySingle<double>("SELECT CAST(1.5 AS FLOAT)"), 10);
        Assert.Equal(1.5, connection.QuerySingle<double>("SELECT CAST(1.5 AS FLOAT(53))"), 10);
    }

    /// <summary>docs/datatypes.html:551 — DECFLOAT [ ( precisionInt ) ]</summary>
    [Fact]
    public void DecFloat_HoldsDecimalValues() // TYP-0551
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT CAST(1.25 AS DECFLOAT) = 1.25"));
        Assert.True(connection.QuerySingle<bool>("SELECT CAST(1.25 AS DECFLOAT(10)) = 1.25"));
    }
}
