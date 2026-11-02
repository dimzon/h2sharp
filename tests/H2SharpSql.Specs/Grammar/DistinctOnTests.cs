// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// DISTINCT ON(expression, ...): the parser, the distinct indexes and the preserved row.
using System.Collections.Generic;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class DistinctOnTests
{
    [Fact]
    public void DistinctOnKeepsTheFirstRowOfEachGroup()
    {
        using IDbConnection connection = TestDatabase.Open();

        List<(int X, int TenX)> rows = connection.Query<(int, int)>(
            "SELECT DISTINCT ON (X % 2) X, X * 10 FROM SYSTEM_RANGE(1, 5) ORDER BY X").AsList();

        Assert.Equal([(1, 10), (2, 20)], rows);
    }

    [Fact]
    public void DistinctOnUsesTheOrderToChooseThePreservedRow()
    {
        using IDbConnection connection = TestDatabase.Open();

        List<int> rows = connection.Query<int>(
            "SELECT DISTINCT ON (X % 2) X FROM SYSTEM_RANGE(1, 5) ORDER BY X DESC").AsList();

        Assert.Equal([5, 4], rows);
    }

    [Fact]
    public void DistinctOnWithoutOrderKeepsOneRowPerGroup()
    {
        using IDbConnection connection = TestDatabase.Open();

        List<int> rows = connection.Query<int>(
            "SELECT DISTINCT ON (X % 2) X FROM SYSTEM_RANGE(1, 5)").AsList();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, v => v % 2 == 1);
        Assert.Contains(rows, v => v % 2 == 0);
    }
}
