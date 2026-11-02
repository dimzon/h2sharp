// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// The primary index answers MIN/MAX straight from its ends and walks only the
// key range a bound names; non-integer bounds fold the way the index folds them.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class PrimaryIndexBoundsTests
{
    private static IDbConnection GivenTable()
    {
        IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID BIGINT PRIMARY KEY, NAME VARCHAR(50))");
        _ = connection.Execute(
            "INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada'), (2, 'Alan'), (3, 'Bob'), (10, 'Grace')");
        return connection;
    }

    [Fact]
    public void MinAndMaxComeFromTheEndsOfTheIndex()
    {
        using IDbConnection connection = GivenTable();

        Assert.Equal((1L, 10L), connection.QuerySingle<(long, long)>("SELECT MIN(ID), MAX(ID) FROM TEST"));
    }

    [Fact]
    public void MinAndMaxOverNoRowsAreNull()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID BIGINT PRIMARY KEY)");

        Assert.Equal((null, null), connection.QuerySingle<(long?, long?)>("SELECT MIN(ID), MAX(ID) FROM TEST"));
    }

    [Fact]
    public void RealBoundStartsAtItsTruncation()
    {
        using IDbConnection connection = GivenTable();

        Assert.Equal([2L, 3L, 10L], connection.Query<long>("SELECT ID FROM TEST WHERE ID > 1.5 ORDER BY ID").AsList());
        Assert.Equal([1L, 2L, 3L], connection.Query<long>("SELECT ID FROM TEST WHERE ID < 3.5 ORDER BY ID").AsList());
    }

    [Fact]
    public void NanBoundMatchesNothing()
    {
        using IDbConnection connection = GivenTable();

        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE ID > CAST('NaN' AS DOUBLE)"));
    }

    /// <summary>An explicit _ROWID_ past the counter survives truncate-and-reuse</summary>
    [Fact]
    public void ExplicitRowIdInsert_SurvivesTruncateAndUpdate()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE R(V VARCHAR(50))");
        _ = connection.Execute("INSERT INTO R(_ROWID_, V) VALUES (100, 'a')");
        _ = connection.Execute("TRUNCATE TABLE R");
        _ = connection.Execute("INSERT INTO R(V) VALUES ('b')");
        _ = connection.Execute("UPDATE R SET V = 'c'");

        Assert.Equal("c", connection.QuerySingle<string>("SELECT V FROM R"));
        Assert.Equal(1L, connection.QuerySingle<long>("SELECT _ROWID_ FROM R"));
    }

    /// <summary>_ROWID_ modifications are not allowed, as in the reference script test</summary>
    [Fact]
    public void RowIdAssignment_IsRejectedAsSyntaxError()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE R(ID INT)");
        _ = connection.Execute("INSERT INTO R(ID) VALUES (100)");

        H2Sharp.H2SharpException e = Assert.Throws<H2Sharp.H2SharpException>(
            () => connection.Execute("UPDATE R SET _ROWID_ = 2 WHERE ID = 100"));
        Assert.Equal(42001, e.H2ErrorCode);
        Assert.Equal(100, connection.QuerySingle<int>("SELECT ID FROM R"));
    }

    [Fact]
    public void InfiniteDecfloatBoundsWalkFromTheOpenEnd()
    {
        using IDbConnection connection = GivenTable();

        Assert.Equal(
            0, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE ID > CAST('Infinity' AS DECFLOAT)"));
        Assert.Equal(
            4, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE ID > CAST('-Infinity' AS DECFLOAT)"));
    }

    /// <summary>
    /// A parameterized bound survives optimization and reaches the walk, so every value kind
    /// the bounds converter knows is exercised: a NaN lower bound is empty while a NaN upper
    /// bound is open — the reference's own asymmetry — the double infinities saturate to the
    /// long ends the way the Java cast does, DECFLOAT infinities walk from the open end or
    /// hold nothing, and NUMERIC bounds past the long range end or empty the walk.
    /// </summary>
    [Fact]
    public void ParameterBoundsWalkTheReferenceConversions()
    {
        using IDbConnection connection = GivenTable();

        Assert.Equal([1L, 2L, 3L, 10L],
            connection.Query<long>("SELECT ID FROM TEST WHERE ID < $d", new { d = double.NaN }).AsList());
        Assert.Empty(connection.Query<long>("SELECT ID FROM TEST WHERE ID > $d", new { d = double.NaN }).AsList());

        Assert.Empty(
            connection.Query<long>("SELECT ID FROM TEST WHERE ID > $d", new { d = double.PositiveInfinity }).AsList());
        Assert.Equal([1L, 2L, 3L, 10L],
            connection.Query<long>("SELECT ID FROM TEST WHERE ID < $d", new { d = double.PositiveInfinity }).AsList());

        Assert.Equal([1L, 2L, 3L, 10L], connection.Query<long>(
            "SELECT ID FROM TEST WHERE ID > CAST($d AS DECFLOAT)", new { d = double.NegativeInfinity }).AsList());
        Assert.Empty(connection.Query<long>(
            "SELECT ID FROM TEST WHERE ID > CAST($d AS DECFLOAT)", new { d = double.PositiveInfinity }).AsList());
        Assert.Equal([1L, 2L, 3L, 10L], connection.Query<long>(
            "SELECT ID FROM TEST WHERE ID < CAST($d AS DECFLOAT)", new { d = double.PositiveInfinity }).AsList());
        Assert.Empty(connection.Query<long>(
            "SELECT ID FROM TEST WHERE ID < CAST($d AS DECFLOAT)", new { d = double.NegativeInfinity }).AsList());

        Assert.Equal([1L, 2L, 3L, 10L],
            connection.Query<long>("SELECT ID FROM TEST WHERE ID > $d", new { d = -2e19m }).AsList());
        Assert.Empty(connection.Query<long>("SELECT ID FROM TEST WHERE ID > $d", new { d = 2e19m }).AsList());
        Assert.Equal([1L, 2L, 3L, 10L],
            connection.Query<long>("SELECT ID FROM TEST WHERE ID < $d", new { d = 2e19m }).AsList());
        Assert.Empty(connection.Query<long>("SELECT ID FROM TEST WHERE ID < $d", new { d = -2e19m }).AsList());

        Assert.Equal([2L, 3L, 10L],
            connection.Query<long>("SELECT ID FROM TEST WHERE ID > $d", new { d = 1.5m }).AsList());
        Assert.Equal([1L, 2L, 3L],
            connection.Query<long>("SELECT ID FROM TEST WHERE ID < $d", new { d = 3.5m }).AsList());
    }
}
