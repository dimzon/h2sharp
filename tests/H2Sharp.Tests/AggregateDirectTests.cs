/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of Aggregate: aggregate function selection, distinct COUNT,
 * MODE, JSON_OBJECTAGG NULL key, LISTAGG overflow, MEDIAN, ANY_VALUE,
 * PERCENTILE, regressions, array aggregate.
 */
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using Xunit;

namespace H2Sharp.Tests;

public sealed class AggregateDirectTests
{
    private readonly SessionLocal session;
    private readonly Database database;

    public AggregateDirectTests()
    {
        database = new Database(
            ConnectionInfo.Memory("aggregate_" + Guid.NewGuid().ToString("N")));
        session = new SessionLocal(database, "sa");
    }

    private CommandContainer Prepare(string sql)
        => Assert.IsType<CommandContainer>(session.PrepareCommand(sql));

    private void Execute(string sql)
    {
        CommandContainer container = Prepare(sql);
        _ = container.ExecuteUpdate(false);
    }

    private string Scalar(string sql)
    {
        CommandContainer container = Prepare(sql);
        using ResultInterface result = container.ExecuteQuery(10, 0, false);
        Assert.True(result.Next());
        return result.CurrentRow()![0]!.GetString()!;
    }

    [Fact]
    public void CountDistinctStarThrows()
    {
        // COUNT(DISTINCT *) → COUNTALL with distinct → InternalError (строки 76–77)
        Execute("CREATE TABLE t(a INT)");
        Execute("INSERT INTO t VALUES (1), (2)");

        Assert.Throws<H2SharpException>(() => Scalar("SELECT COUNT(DISTINCT *) FROM t"));
    }

    [Fact]
    public void ModeAggregate()
    {
        // MODE(x) → GetExtraArguments (строка 233), distinct-values walk (строки 778–801)
        Execute("CREATE TABLE t(a INT)");
        Execute("INSERT INTO t VALUES (1), (2), (2), (3), (3), (3)");

        Assert.Equal("3", Scalar("SELECT MODE(a) FROM t"));
    }

    [Fact]
    public void AnyValueAggregate()
    {
        Execute("CREATE TABLE t(a INT)");
        Execute("INSERT INTO t VALUES (1), (2), (3)");

        string r = Scalar("SELECT ANY_VALUE(a) FROM t");
        Assert.True(r == "1" || r == "2" || r == "3");
    }

    [Fact]
    public void PercentileContAggregate()
    {
        Execute("CREATE TABLE t(a INT)");
        Execute("INSERT INTO t VALUES (1), (2), (3), (4), (5)");

        string r = Scalar("SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY a) FROM t");
        Assert.Equal("3", r);
    }

    [Fact]
    public void ListaggAggregate()
    {
        Execute("CREATE TABLE t(a INT)");
        Execute("INSERT INTO t VALUES (1), (2), (3)");

        string r = Scalar("SELECT LISTAGG(a, ',') WITHIN GROUP (ORDER BY a) FROM t");
        Assert.Equal("1,2,3", r);
    }

    [Fact]
    public void JsonObjectaggNullKeyThrows()
    {
        // JSON_OBJECTAGG with NULL key → InvalidValueException (строки 853–854)
        Execute("CREATE TABLE t(a INT)");
        Execute("INSERT INTO t VALUES (1)");

        Assert.Throws<H2SharpException>(() => Scalar("SELECT JSON_OBJECTAGG(NULL, a) FROM t"));
    }

    [Fact]
    public void StddevPopAggregate()
    {
        Execute("CREATE TABLE t(a INT)");
        Execute("INSERT INTO t VALUES (1), (2), (3), (4), (5)");

        string r = Scalar("SELECT STDDEV_POP(a) FROM t");
        Assert.Equal("1.4142135623730951", r);
    }

    [Fact]
    public void VarPopAggregate()
    {
        Execute("CREATE TABLE t(a INT)");
        Execute("INSERT INTO t VALUES (1), (2), (3), (4), (5)");

        string r = Scalar("SELECT VAR_POP(a) FROM t");
        Assert.Equal("2", r.TrimEnd('0').TrimEnd('.'));
    }

    [Fact]
    public void CorrelateAggregate()
    {
        Execute("CREATE TABLE t(a INT, b INT)");
        Execute("INSERT INTO t VALUES (1, 2), (2, 4), (3, 6)");

        string r = Scalar("SELECT CORR(a, b) FROM t");
        Assert.Equal("1", r.TrimEnd('0').TrimEnd('.'));
    }

    [Fact]
    public void ArrayAggAggregate()
    {
        Execute("CREATE TABLE t(a INT)");
        Execute("INSERT INTO t VALUES (1), (2), (3)");

        string r = Scalar("SELECT ARRAY_AGG(a ORDER BY a) FROM t");
        Assert.Equal("[1, 2, 3]", r);
    }

    [Fact]
    public void JsonArrayaggAggregate()
    {
        Execute("CREATE TABLE t(a INT)");
        Execute("INSERT INTO t VALUES (1), (2), (3)");

        string r = Scalar("SELECT JSON_ARRAYAGG(a) FROM t");
        Assert.Equal("[1,2,3]", r);
    }
}
