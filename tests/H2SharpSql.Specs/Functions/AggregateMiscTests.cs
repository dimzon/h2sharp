// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions-aggregate.html — ordered set and other aggregates, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class AggregateMiscTests
{
    private static void GivenPoints(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE GEO(ID INT, G GEOMETRY)");
        _ = connection.Execute(
            "INSERT INTO GEO(ID, G) VALUES (1, CAST('POINT(1 2)' AS GEOMETRY)), " +
            "(2, CAST('POINT(3 4)' AS GEOMETRY))");
    }

    private static void GivenCommonDivisors(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (12), (18)");
    }

    private static void GivenNames(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(NAME VARCHAR(10))");
        _ = connection.Execute("INSERT INTO TEST(NAME) VALUES ('a'), ('b'), ('c')");
    }

    private static void GivenNumbers(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (1), (2), (3)");
    }

    private static void GivenMode(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (1), (2), (2), (3)");
    }

    /// <summary>docs/functions-aggregate.html:767 — ENVELOPE( value )</summary>
    [Fact]
    public void Envelope_ReturnsTheBoundingBox() // AGG-0767
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPoints(connection);

        Assert.True(connection.QuerySingle<bool>("SELECT ENVELOPE(G) IS NOT NULL FROM GEO"));
    }

    /// <summary>docs/functions-aggregate.html:768 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void Envelope_WithFilterAndOver() // AGG-0768
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPoints(connection);

        Assert.True(connection.QuerySingle<bool>("SELECT ENVELOPE(G) FILTER (WHERE ID = 1) IS NOT NULL FROM GEO"));
        Assert.True(connection.QuerySingle<bool>("SELECT DISTINCT ENVELOPE(G) OVER () IS NOT NULL FROM GEO"));
    }

    /// <summary>docs/functions-aggregate.html:787 — GCD_AGG(numeric)</summary>
    [Fact]
    public void GcdAgg_ComputesTheGreatestCommonDivisor() // AGG-0787
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenCommonDivisors(connection);

        Assert.Equal(6, connection.QuerySingle<int>("SELECT GCD_AGG(ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:788 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void GcdAgg_WithFilterAndOver() // AGG-0788
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenCommonDivisors(connection);

        Assert.Equal(12, connection.QuerySingle<int>("SELECT GCD_AGG(ID) FILTER (WHERE ID = 12) FROM TEST"));
        Assert.Equal(6, connection.QuerySingle<int>("SELECT DISTINCT GCD_AGG(ID) OVER () FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:807 — LCM_AGG(numeric)</summary>
    [Fact]
    public void LcmAgg_ComputesTheLeastCommonMultiple() // AGG-0807
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenCommonDivisors(connection);

        Assert.Equal(36, connection.QuerySingle<int>("SELECT LCM_AGG(ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:808 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void LcmAgg_WithFilterAndOver() // AGG-0808
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenCommonDivisors(connection);

        Assert.Equal(12, connection.QuerySingle<int>("SELECT LCM_AGG(ID) FILTER (WHERE ID = 12) FROM TEST"));
        Assert.Equal(36, connection.QuerySingle<int>("SELECT DISTINCT LCM_AGG(ID) OVER () FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1071 — LISTAGG ( [ DISTINCT|ALL ] string [, separatorString]</summary>
    [Fact]
    public void ListAgg_ConcatenatesTheStrings() // AGG-1071
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNames(connection);

        Assert.Equal("a,b,c", connection.QuerySingle<string>(
            "SELECT LISTAGG(NAME, ',') WITHIN GROUP (ORDER BY NAME) FROM TEST"));
        Assert.Equal("a,b,c", connection.QuerySingle<string>(
            "SELECT LISTAGG(DISTINCT NAME, ',') WITHIN GROUP (ORDER BY NAME) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1072 — [ ON OVERFLOW { ERROR</summary>
    [Fact]
    public void ListAgg_OnOverflowError() // AGG-1072
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNames(connection);

        Assert.Equal("a,b,c", connection.QuerySingle<string>(
            "SELECT LISTAGG(NAME, ',' ON OVERFLOW ERROR) WITHIN GROUP (ORDER BY NAME) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1073 — | TRUNCATE [ filterString ] { WITH | WITHOUT } COUNT } ] )</summary>
    [Fact]
    public void ListAgg_OnOverflowTruncate() // AGG-1073
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNames(connection);

        Assert.Equal("a,b,c", connection.QuerySingle<string>(
            "SELECT LISTAGG(NAME, ',' ON OVERFLOW TRUNCATE '...' WITH COUNT) " +
            "WITHIN GROUP (ORDER BY NAME) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1074 — withinGroupSpecification</summary>
    [Fact]
    public void ListAgg_WithWithinGroupOrderBy() // AGG-1074
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNames(connection);

        Assert.Equal("c,b,a", connection.QuerySingle<string>(
            "SELECT LISTAGG(NAME) WITHIN GROUP (ORDER BY NAME DESC) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1075 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void ListAgg_WithFilter() // AGG-1075
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNames(connection);

        Assert.Equal("a,c", connection.QuerySingle<string>(
            "SELECT LISTAGG(NAME, ',') WITHIN GROUP (ORDER BY NAME) FILTER (WHERE NAME <> 'b') FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1097 — ARRAY_AGG ( [ DISTINCT|ALL ] value</summary>
    [Fact]
    public void ArrayAgg_CollectsTheValues() // AGG-1097
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(3, connection.QuerySingle<int>("SELECT CARDINALITY(ARRAY_AGG(ID)) FROM TEST"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT CARDINALITY(ARRAY_AGG(DISTINCT ID)) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1098 — [ ORDER BY sortSpecificationList ] )</summary>
    [Fact]
    public void ArrayAgg_WithOrderBy() // AGG-1098
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(3, connection.QuerySingle<int>("SELECT ARRAY_GET(ARRAY_AGG(ID ORDER BY ID DESC), 1) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1099 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void ArrayAgg_WithFilterAndOver() // AGG-1099
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT CARDINALITY(ARRAY_AGG(ID) FILTER (WHERE ID > 1)) FROM TEST"));
        Assert.Equal(3, connection.QuerySingle<int>(
            "SELECT DISTINCT CARDINALITY(ARRAY_AGG(ID) OVER ()) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1121 — RANK(value [,...])</summary>
    [Fact]
    public void RankAggregate_RanksTheValue() // AGG-1121
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(1, connection.QuerySingle<int>("SELECT RANK(1) WITHIN GROUP (ORDER BY ID) FROM TEST"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT RANK(2) WITHIN GROUP (ORDER BY ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1122 — withinGroupSpecification</summary>
    [Fact]
    public void RankAggregate_ForTheHighestValue() // AGG-1122
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(4, connection.QuerySingle<int>("SELECT RANK(5) WITHIN GROUP (ORDER BY ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1123 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void RankAggregate_WithFilter() // AGG-1123
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(3, connection.QuerySingle<int>(
            "SELECT RANK(5) WITHIN GROUP (ORDER BY ID) FILTER (WHERE ID > 1) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1143 — DENSE_RANK(value [,...])</summary>
    [Fact]
    public void DenseRankAggregate_RanksTheValue() // AGG-1143
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT DENSE_RANK(2) WITHIN GROUP (ORDER BY ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1144 — withinGroupSpecification</summary>
    [Fact]
    public void DenseRankAggregate_ForTheHighestValue() // AGG-1144
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(4, connection.QuerySingle<int>("SELECT DENSE_RANK(5) WITHIN GROUP (ORDER BY ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1145 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void DenseRankAggregate_WithFilter() // AGG-1145
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(3, connection.QuerySingle<int>(
            "SELECT DENSE_RANK(5) WITHIN GROUP (ORDER BY ID) FILTER (WHERE ID > 1) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1165 — PERCENT_RANK(value [,...])</summary>
    [Fact]
    public void PercentRankAggregate_RanksTheValue() // AGG-1165
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(1.0 / 3,
            connection.QuerySingle<double>("SELECT PERCENT_RANK(2) WITHIN GROUP (ORDER BY ID) FROM TEST"), 10);
        Assert.Equal(0.0,
            connection.QuerySingle<double>("SELECT PERCENT_RANK(1) WITHIN GROUP (ORDER BY ID) FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1166 — withinGroupSpecification</summary>
    [Fact]
    public void PercentRankAggregate_ForTheHighestValue() // AGG-1166
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(2.0 / 3,
            connection.QuerySingle<double>("SELECT PERCENT_RANK(3) WITHIN GROUP (ORDER BY ID) FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1167 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void PercentRankAggregate_WithFilter() // AGG-1167
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(0.5, connection.QuerySingle<double>(
            "SELECT PERCENT_RANK(3) WITHIN GROUP (ORDER BY ID) FILTER (WHERE ID > 1) FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1187 — CUME_DIST(value [,...])</summary>
    [Fact]
    public void CumeDistAggregate_DistributesTheValue() // AGG-1187
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        // H2 2.5 includes the hypothetical row in the population, so 1,2,3 plus 2
        // gives CUME_DIST(2) = 3/4. The pinned jar returns 0.75, not the SQL-standard 2/3.
        Assert.Equal(3.0 / 4,
            connection.QuerySingle<double>("SELECT CUME_DIST(2) WITHIN GROUP (ORDER BY ID) FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1188 — withinGroupSpecification</summary>
    [Fact]
    public void CumeDistAggregate_ForTheHighestValue() // AGG-1188
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(1.0, connection.QuerySingle<double>(
            "SELECT CUME_DIST(3) WITHIN GROUP (ORDER BY ID) FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1189 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void CumeDistAggregate_WithFilter() // AGG-1189
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(1.0, connection.QuerySingle<double>(
            "SELECT CUME_DIST(3) WITHIN GROUP (ORDER BY ID) FILTER (WHERE ID > 1) FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1211 — PERCENTILE_CONT(numeric) WITHIN GROUP (ORDER BY sortSpecification)</summary>
    [Fact]
    public void PercentileCont_InterpolatesTheValue() // AGG-1211
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(2.0, connection.QuerySingle<double>(
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY ID) FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1212 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void PercentileCont_WithFilter() // AGG-1212
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(2.5, connection.QuerySingle<double>(
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY ID) FILTER (WHERE ID > 1) FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1231 — PERCENTILE_DISC(numeric) WITHIN GROUP (ORDER BY sortSpecification)</summary>
    [Fact]
    public void PercentileDisc_PicksTheValue() // AGG-1231
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1232 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void PercentileDisc_WithFilter() // AGG-1232
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY ID) FILTER (WHERE ID > 1) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1251 — MEDIAN( [ DISTINCT|ALL ] value )</summary>
    [Fact]
    public void Median_ReturnsTheMiddleValue() // AGG-1251
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT MEDIAN(ID) FROM TEST"), 10);
        Assert.Equal(2.0, connection.QuerySingle<double>("SELECT MEDIAN(DISTINCT ID) FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1252 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void Median_WithFilter() // AGG-1252
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal(2.5, connection.QuerySingle<double>(
            "SELECT MEDIAN(ID) FILTER (WHERE ID > 1) FROM TEST"), 10);
    }

    /// <summary>docs/functions-aggregate.html:1271 — { MODE() WITHIN GROUP (ORDER BY sortSpecification) }</summary>
    [Fact]
    public void Mode_WithWithinGroup() // AGG-1271
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenMode(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT MODE() WITHIN GROUP (ORDER BY ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1272 — | { MODE( value [ ORDER BY sortSpecification ] ) }</summary>
    [Fact]
    public void Mode_WithValue() // AGG-1272
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenMode(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT MODE(ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1273 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void Mode_WithFilterAndOver() // AGG-1273
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenMode(connection);

        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT MODE() WITHIN GROUP (ORDER BY ID) FILTER (WHERE ID > 1) FROM TEST"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT DISTINCT MODE(ID) OVER () FROM TEST"));
    }
}
