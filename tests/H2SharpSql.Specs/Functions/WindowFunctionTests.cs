// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions-window.html — window functions, one test per checklist row.
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class WindowFunctionTests
{
    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, X INT, CATEGORY VARCHAR(10))");
        _ = connection.Execute("INSERT INTO TEST(ID, X, CATEGORY) VALUES (1, 10, 'a'), (2, 20, 'a'), (3, 30, 'b')");
    }

    /// <summary>docs/functions-window.html:259 — ROW_NUMBER() OVER windowNameOrSpecification</summary>
    [Fact]
    public void RowNumber_NumbersTheRows() // WIN-0259
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(new long[] { 1, 2, 3 },
            connection.Query<long>("SELECT ROW_NUMBER() OVER (ORDER BY ID) FROM TEST").AsList());
    }

    /// <summary>docs/functions-window.html:279 — RANK() OVER windowNameOrSpecification</summary>
    [Fact]
    public void Rank_RanksTheRows() // WIN-0279
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(new long[] { 1, 2, 3 },
            connection.Query<long>("SELECT RANK() OVER (ORDER BY X) FROM TEST").AsList());
    }

    /// <summary>docs/functions-window.html:297 — DENSE_RANK() OVER windowNameOrSpecification</summary>
    [Fact]
    public void DenseRank_RanksWithinThePartition() // WIN-0297
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(new long[] { 1, 2, 1 },
            connection.Query<long>("SELECT DENSE_RANK() OVER (PARTITION BY CATEGORY ORDER BY ID) FROM TEST").AsList());
    }

    /// <summary>docs/functions-window.html:315 — PERCENT_RANK() OVER windowNameOrSpecification</summary>
    [Fact]
    public void PercentRank_DistributesTheRanks() // WIN-0315
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        var ranks = connection.Query<double>("SELECT PERCENT_RANK() OVER (ORDER BY X) FROM TEST").AsList();

        Assert.Equal(0.0, ranks[0], 10);
        Assert.Equal(0.5, ranks[1], 10);
        Assert.Equal(1.0, ranks[2], 10);
    }

    /// <summary>docs/functions-window.html:333 — CUME_DIST() OVER windowNameOrSpecification</summary>
    [Fact]
    public void CumeDist_DistributesTheRows() // WIN-0333
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        var distances = connection.Query<double>("SELECT CUME_DIST() OVER (ORDER BY X) FROM TEST").AsList();

        Assert.Equal(1.0 / 3, distances[0], 10);
        Assert.Equal(2.0 / 3, distances[1], 10);
        Assert.Equal(1.0, distances[2], 10);
    }

    /// <summary>docs/functions-window.html:353 — LEAD(value [, offsetInt [, defaultValue]]) [{RESPECT|IGNORE} NULLS]</summary>
    [Fact]
    public void Lead_ReadsTheFollowingRow() // WIN-0353
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(new int?[] { 20, 30, null },
            connection.Query<int?>("SELECT LEAD(X) OVER (ORDER BY ID) FROM TEST").AsList());
        Assert.Equal(new int?[] { 30, 0, 0 },
            connection.Query<int?>("SELECT LEAD(X, 2, 0) IGNORE NULLS OVER (ORDER BY ID) FROM TEST").AsList());
    }

    /// <summary>docs/functions-window.html:354 — OVER windowNameOrSpecification</summary>
    [Fact]
    public void Lead_WithinThePartition() // WIN-0354
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(new int?[] { 20, null, null },
            connection.Query<int?>("SELECT LEAD(X) OVER (PARTITION BY CATEGORY ORDER BY ID) FROM TEST").AsList());
    }

    /// <summary>docs/functions-window.html:373 — LAG(value [, offsetInt [, defaultValue]]) [{RESPECT|IGNORE} NULLS]</summary>
    [Fact]
    public void Lag_ReadsThePrecedingRow() // WIN-0373
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(new int?[] { null, 10, 20 },
            connection.Query<int?>("SELECT LAG(X) OVER (ORDER BY ID) FROM TEST").AsList());
        Assert.Equal(new int?[] { 0, 0, 10 },
            connection.Query<int?>("SELECT LAG(X, 2, 0) IGNORE NULLS OVER (ORDER BY ID) FROM TEST").AsList());
    }

    /// <summary>docs/functions-window.html:374 — OVER windowNameOrSpecification</summary>
    [Fact]
    public void Lag_WithinThePartition() // WIN-0374
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(new int?[] { null, 10, null },
            connection.Query<int?>("SELECT LAG(X) OVER (PARTITION BY CATEGORY ORDER BY ID) FROM TEST").AsList());
    }

    /// <summary>docs/functions-window.html:395 — FIRST_VALUE(value) [{RESPECT|IGNORE} NULLS]</summary>
    [Fact]
    public void FirstValue_ReadsTheFirstRow() // WIN-0395
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(new int?[] { 10, 10, 10 },
            connection.Query<int?>("SELECT FIRST_VALUE(X) OVER (ORDER BY ID) FROM TEST").AsList());
        Assert.Equal(new int?[] { 10, 10, 10 },
            connection.Query<int?>("SELECT FIRST_VALUE(X) IGNORE NULLS OVER (ORDER BY ID) FROM TEST").AsList());
    }

    /// <summary>docs/functions-window.html:396 — OVER windowNameOrSpecification</summary>
    [Fact]
    public void FirstValue_WithinThePartition() // WIN-0396
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(new int?[] { 10, 10, 30 },
            connection.Query<int?>("SELECT FIRST_VALUE(X) OVER (PARTITION BY CATEGORY ORDER BY ID) FROM TEST").AsList());
    }

    /// <summary>docs/functions-window.html:415 — LAST_VALUE(value) [{RESPECT|IGNORE} NULLS]</summary>
    [Fact]
    public void LastValue_ReadsTheLastRow() // WIN-0415
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(new int?[] { 10, 20, 30 },
            connection.Query<int?>("SELECT LAST_VALUE(X) OVER (ORDER BY ID) FROM TEST").AsList());
        Assert.Equal(new int?[] { 30, 30, 30 },
            connection.Query<int?>(
                "SELECT LAST_VALUE(X) IGNORE NULLS OVER (ORDER BY ID " +
                "RANGE BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING) FROM TEST").AsList());
    }

    /// <summary>docs/functions-window.html:416 — OVER windowNameOrSpecification</summary>
    [Fact]
    public void LastValue_WithinThePartition() // WIN-0416
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(new int?[] { 20, 20, 30 },
            connection.Query<int?>(
                "SELECT LAST_VALUE(X) OVER (PARTITION BY CATEGORY ORDER BY ID " +
                "RANGE BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) FROM TEST").AsList());
    }

    /// <summary>docs/functions-window.html:435 — NTH_VALUE(value, nInt) [FROM {FIRST|LAST}] [{RESPECT|IGNORE} NULLS]</summary>
    [Fact]
    public void NthValue_ReadsTheGivenRow() // WIN-0435
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(new int?[] { 20, 20, 20 },
            connection.Query<int?>(
                "SELECT NTH_VALUE(X, 2) OVER (ORDER BY ID " +
                "RANGE BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) FROM TEST").AsList());
        Assert.Equal(new int?[] { 30, 30, 30 },
            connection.Query<int?>(
                "SELECT NTH_VALUE(X, 1 FROM LAST) IGNORE NULLS OVER (ORDER BY ID " +
                "RANGE BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) FROM TEST").AsList());
    }

    /// <summary>docs/functions-window.html:436 — OVER windowNameOrSpecification</summary>
    [Fact]
    public void NthValue_WithinThePartition() // WIN-0436
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(new int?[] { 10, 10, 30 },
            connection.Query<int?>(
                "SELECT NTH_VALUE(X, 1) OVER (PARTITION BY CATEGORY ORDER BY ID " +
                "RANGE BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) FROM TEST").AsList());
    }

    /// <summary>docs/functions-window.html:457 — NTILE(long) OVER windowNameOrSpecification</summary>
    [Fact]
    public void Ntile_SplitsTheRowsIntoBuckets() // WIN-0457
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(new long[] { 1, 2, 3 },
            connection.Query<long>("SELECT NTILE(3) OVER (ORDER BY ID) FROM TEST").AsList());
    }

    /// <summary>docs/functions-window.html:475 — RATIO_TO_REPORT(value)</summary>
    [Fact]
    public void RatioToReport_ComputesTheShare() // WIN-0475
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        var ratios = connection.Query<double>("SELECT RATIO_TO_REPORT(X) OVER () FROM TEST").AsList();

        Assert.Equal(1.0 / 6, ratios[0], 10);
        Assert.Equal(1.0 / 3, ratios[1], 10);
        Assert.Equal(0.5, ratios[2], 10);
    }

    /// <summary>docs/functions-window.html:476 — OVER windowNameOrSpecification</summary>
    [Fact]
    public void RatioToReport_WithinThePartition() // WIN-0476
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        var ratios = connection.Query<double>(
            "SELECT RATIO_TO_REPORT(X) OVER (PARTITION BY CATEGORY) FROM TEST").AsList();

        Assert.Equal(1.0 / 3, ratios[0], 10);
        Assert.Equal(2.0 / 3, ratios[1], 10);
        Assert.Equal(1.0, ratios[2], 10);
    }
}
