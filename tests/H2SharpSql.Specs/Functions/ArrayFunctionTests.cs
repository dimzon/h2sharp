// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions.html — array functions, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class ArrayFunctionTests
{
    /// <summary>docs/functions.html:3219 — ARRAY_GET(arrayExpression, indexExpression)</summary>
    [Fact]
    public void ArrayGet_ReadsTheElement() // FUN-3219
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT ARRAY_GET(ARRAY[1, 2, 3], 2)"));
    }

    /// <summary>docs/functions.html:3237 — { CARDINALITY | { ARRAY_LENGTH } } (arrayExpression)</summary>
    [Fact]
    public void Cardinality_CountsTheElements() // FUN-3237
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT CARDINALITY(ARRAY[1, 2, 3])"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT ARRAY_LENGTH(ARRAY[1, 2])"));
    }

    /// <summary>docs/functions.html:3255 — ARRAY_CONTAINS(arrayExpression, value)</summary>
    [Fact]
    public void ArrayContains_FindsTheValue() // FUN-3255
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT ARRAY_CONTAINS(ARRAY[1, 2], 2)"));
    }

    /// <summary>docs/functions.html:3273 — ARRAY_CAT(arrayExpression, arrayExpression)</summary>
    [Fact]
    public void ArrayCat_ConcatenatesTheArrays() // FUN-3273
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT ARRAY_GET(ARRAY_CAT(ARRAY[1], ARRAY[2]), 2)"));
    }

    /// <summary>docs/functions.html:3291 — ARRAY_APPEND(arrayExpression, value)</summary>
    [Fact]
    public void ArrayAppend_AddsTheValueAtTheEnd() // FUN-3291
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT ARRAY_GET(ARRAY_APPEND(ARRAY[1], 2), 2)"));
    }

    /// <summary>docs/functions.html:3309 — ARRAY_MAX_CARDINALITY(arrayExpression)</summary>
    [Fact]
    public void ArrayMaxCardinality_ReturnsTheLength() // FUN-3309
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT ARRAY_MAX_CARDINALITY(ARRAY[1, 2, 3])"));
    }

    /// <summary>docs/functions.html:3327 — TRIM_ARRAY(arrayExpression, int)</summary>
    [Fact]
    public void TrimArray_DropsTheLastElements() // FUN-3327
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT CARDINALITY(TRIM_ARRAY(ARRAY[1, 2, 3], 1))"));
    }

    /// <summary>docs/functions.html:3345 — ARRAY_SLICE(arrayExpression, lowerBoundInt, upperBoundInt)</summary>
    [Fact]
    public void ArraySlice_ReturnsTheSubArray() // FUN-3345
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT CARDINALITY(ARRAY_SLICE(ARRAY[1, 2, 3, 4], 2, 3))"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT ARRAY_GET(ARRAY_SLICE(ARRAY[1, 2, 3, 4], 2, 3), 1)"));
    }
}
