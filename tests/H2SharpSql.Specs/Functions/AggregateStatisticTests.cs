// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions-aggregate.html — statistical aggregates, one test per checklist row.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class AggregateStatisticTests
{
    private static void GivenNumbers(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (1), (2), (3)");
    }

    private static void GivenPairs(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE PAIRS(X INT, Y INT)");
        _ = connection.Execute("INSERT INTO PAIRS(X, Y) VALUES (1, 2), (2, 4), (3, 6)");
    }

    /// <summary>docs/functions-aggregate.html:547 — STDDEV_POP( [ DISTINCT|ALL ] numeric )</summary>
    [Fact]
    public void StddevPop_ComputesTheDeviation() // AGG-0547
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(Math.Sqrt(2.0 / 3), connection.QuerySingle<double>("SELECT STDDEV_POP(ID) FROM TEST"), 10);
        Assert.Equal(Math.Sqrt(2.0 / 3),
            connection.QuerySingle<double>("SELECT STDDEV_POP(DISTINCT ID) FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:548 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void StddevPop_WithFilterAndOver() // AGG-0548
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(0.5,
            connection.QuerySingle<double>("SELECT STDDEV_POP(ID) FILTER (WHERE ID > 1) FROM TEST"), 10);
        Assert.Equal(Math.Sqrt(2.0 / 3),
            connection.QuerySingle<double>("SELECT DISTINCT STDDEV_POP(ID) OVER () FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:567 — STDDEV_SAMP( [ DISTINCT|ALL ] numeric )</summary>
    [Fact]
    public void StddevSamp_ComputesTheSampleDeviation() // AGG-0567
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT STDDEV_SAMP(ID) FROM TEST"), 10);
        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT STDDEV_SAMP(ALL ID) FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:568 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void StddevSamp_WithFilterAndOver() // AGG-0568
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(Math.Sqrt(0.5),
            connection.QuerySingle<double>("SELECT STDDEV_SAMP(ID) FILTER (WHERE ID > 1) FROM TEST"), 10);
        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT DISTINCT STDDEV_SAMP(ID) OVER () FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:587 — VAR_POP( [ DISTINCT|ALL ] numeric )</summary>
    [Fact]
    public void VarPop_ComputesTheVariance() // AGG-0587
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(2.0 / 3, connection.QuerySingle<double>("SELECT VAR_POP(ID) FROM TEST"), 10);
        Assert.Equal(2.0 / 3, connection.QuerySingle<double>("SELECT VAR_POP(DISTINCT ID) FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:588 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void VarPop_WithFilterAndOver() // AGG-0588
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(0.25, connection.QuerySingle<double>("SELECT VAR_POP(ID) FILTER (WHERE ID > 1) FROM TEST"), 10);
        Assert.Equal(2.0 / 3, connection.QuerySingle<double>("SELECT DISTINCT VAR_POP(ID) OVER () FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:607 — VAR_SAMP( [ DISTINCT|ALL ] numeric )</summary>
    [Fact]
    public void VarSamp_ComputesTheSampleVariance() // AGG-0607
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT VAR_SAMP(ID) FROM TEST"), 10);
        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT VAR_SAMP(ALL ID) FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:608 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void VarSamp_WithFilterAndOver() // AGG-0608
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(0.5, connection.QuerySingle<double>("SELECT VAR_SAMP(ID) FILTER (WHERE ID > 1) FROM TEST"), 10);
        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT DISTINCT VAR_SAMP(ID) OVER () FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:829 — COVAR_POP(dependentExpression, independentExpression)</summary>
    [Fact]
    public void CovarPop_ComputesTheCovariance() // AGG-0829
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(4.0 / 3, connection.QuerySingle<double>("SELECT COVAR_POP(Y, X) FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:830 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void CovarPop_WithFilterAndOver() // AGG-0830
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(0.5, connection.QuerySingle<double>("SELECT COVAR_POP(Y, X) FILTER (WHERE X > 1) FROM PAIRS"), 10);
        Assert.Equal(4.0 / 3, connection.QuerySingle<double>("SELECT DISTINCT COVAR_POP(Y, X) OVER () FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:849 — COVAR_SAMP(dependentExpression, independentExpression)</summary>
    [Fact]
    public void CovarSamp_ComputesTheSampleCovariance() // AGG-0849
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT COVAR_SAMP(Y, X) FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:850 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void CovarSamp_WithFilterAndOver() // AGG-0850
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT COVAR_SAMP(Y, X) FILTER (WHERE X > 1) FROM PAIRS"), 10);
        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT DISTINCT COVAR_SAMP(Y, X) OVER () FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:869 — CORR(dependentExpression, independentExpression)</summary>
    [Fact]
    public void Corr_ComputesTheCoefficient() // AGG-0869
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT CORR(Y, X) FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:870 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void Corr_WithFilterAndOver() // AGG-0870
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT CORR(Y, X) FILTER (WHERE X > 1) FROM PAIRS"), 10);
        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT DISTINCT CORR(Y, X) OVER () FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:889 — REGR_SLOPE(dependentExpression, independentExpression)</summary>
    [Fact]
    public void RegrSlope_ReturnsTheSlope() // AGG-0889
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT REGR_SLOPE(Y, X) FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:890 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void RegrSlope_WithFilterAndOver() // AGG-0890
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT REGR_SLOPE(Y, X) FILTER (WHERE X > 1) FROM PAIRS"), 10);
        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT DISTINCT REGR_SLOPE(Y, X) OVER () FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:909 — REGR_INTERCEPT(dependentExpression, independentExpression)</summary>
    [Fact]
    public void RegrIntercept_ReturnsTheIntercept() // AGG-0909
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(0.0, connection.QuerySingle<double>("SELECT REGR_INTERCEPT(Y, X) FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:910 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void RegrIntercept_WithFilterAndOver() // AGG-0910
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(0.0, connection.QuerySingle<double>("SELECT REGR_INTERCEPT(Y, X) FILTER (WHERE X > 1) FROM PAIRS"), 10);
        Assert.Equal(0.0, connection.QuerySingle<double>("SELECT DISTINCT REGR_INTERCEPT(Y, X) OVER () FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:929 — REGR_COUNT(dependentExpression, independentExpression)</summary>
    [Fact]
    public void RegrCount_CountsThePairs() // AGG-0929
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(3L, connection.QuerySingle<long>("SELECT REGR_COUNT(Y, X) FROM PAIRS"));
    }

    /// <summary>docs/functions-aggregate.html:930 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void RegrCount_WithFilterAndOver() // AGG-0930
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(2L, connection.QuerySingle<long>("SELECT REGR_COUNT(Y, X) FILTER (WHERE X > 1) FROM PAIRS"));
        Assert.Equal(3L, connection.QuerySingle<long>("SELECT DISTINCT REGR_COUNT(Y, X) OVER () FROM PAIRS"));
    }

    /// <summary>docs/functions-aggregate.html:949 — REGR_R2(dependentExpression, independentExpression)</summary>
    [Fact]
    public void RegrR2_ReturnsTheDetermination() // AGG-0949
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT REGR_R2(Y, X) FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:950 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void RegrR2_WithFilterAndOver() // AGG-0950
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT REGR_R2(Y, X) FILTER (WHERE X > 1) FROM PAIRS"), 10);
        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT DISTINCT REGR_R2(Y, X) OVER () FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:969 — REGR_AVGX(dependentExpression, independentExpression)</summary>
    [Fact]
    public void RegrAvgX_AveragesTheIndependentValues() // AGG-0969
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT REGR_AVGX(Y, X) FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:970 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void RegrAvgX_WithFilterAndOver() // AGG-0970
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(2.5, connection.QuerySingle<double>("SELECT REGR_AVGX(Y, X) FILTER (WHERE X > 1) FROM PAIRS"), 10);
        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT DISTINCT REGR_AVGX(Y, X) OVER () FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:989 — REGR_AVGY(dependentExpression, independentExpression)</summary>
    [Fact]
    public void RegrAvgY_AveragesTheDependentValues() // AGG-0989
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(4.0, connection.QuerySingle<double>("SELECT REGR_AVGY(Y, X) FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:990 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void RegrAvgY_WithFilterAndOver() // AGG-0990
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(5.0, connection.QuerySingle<double>("SELECT REGR_AVGY(Y, X) FILTER (WHERE X > 1) FROM PAIRS"), 10);
        Assert.Equal(4.0, connection.QuerySingle<double>("SELECT DISTINCT REGR_AVGY(Y, X) OVER () FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1009 — REGR_SXX(dependentExpression, independentExpression)</summary>
    [Fact]
    public void RegrSxx_SumsTheIndependentSquares() // AGG-1009
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT REGR_SXX(Y, X) FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1010 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void RegrSxx_WithFilterAndOver() // AGG-1010
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(0.5, connection.QuerySingle<double>("SELECT REGR_SXX(Y, X) FILTER (WHERE X > 1) FROM PAIRS"), 10);
        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT DISTINCT REGR_SXX(Y, X) OVER () FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1029 — REGR_SYY(dependentExpression, independentExpression)</summary>
    [Fact]
    public void RegrSyy_SumsTheDependentSquares() // AGG-1029
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(8.0, connection.QuerySingle<double>("SELECT REGR_SYY(Y, X) FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1030 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void RegrSyy_WithFilterAndOver() // AGG-1030
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT REGR_SYY(Y, X) FILTER (WHERE X > 1) FROM PAIRS"), 10);
        Assert.Equal(8.0, connection.QuerySingle<double>("SELECT DISTINCT REGR_SYY(Y, X) OVER () FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1049 — REGR_SXY(dependentExpression, independentExpression)</summary>
    [Fact]
    public void RegrSxy_SumsTheCrossProducts() // AGG-1049
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(4.0, connection.QuerySingle<double>("SELECT REGR_SXY(Y, X) FROM PAIRS"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1050 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void RegrSxy_WithFilterAndOver() // AGG-1050
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal(1.0, connection.QuerySingle<double>("SELECT REGR_SXY(Y, X) FILTER (WHERE X > 1) FROM PAIRS"), 10);
        Assert.Equal(4.0, connection.QuerySingle<double>("SELECT DISTINCT REGR_SXY(Y, X) OVER () FROM PAIRS"), 10);
    }
}
