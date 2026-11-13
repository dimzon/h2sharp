/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of Table/TableTypes: generated column assignment,
 * row-value IN subquery, compound IN conditions.
 */
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TableTypesDirectTests
{
    private readonly SessionLocal session;
    private readonly Database database;

    public TableTypesDirectTests()
    {
        database = new Database(
            ConnectionInfo.Memory("tabletypes_" + Guid.NewGuid().ToString("N")));
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
    public void GeneratedColumnWithNonNullValueThrows()
    {
        // GENERATED column с явным не-null значением → GeneratedColumnCannotBeAssigned1 (строки 1983–1986)
        Execute("CREATE TABLE t(b INT, a INT GENERATED ALWAYS AS (b + 1))");

        Assert.Throws<H2SharpException>(() => Execute("INSERT INTO t(b, a) VALUES (1, 5)"));
    }

    [Fact]
    public void RowValueInSubqueryConvertsValueRow()
    {
        // (a,b) IN (SELECT x,y FROM t2) → Column.Convert(session?.CastDataProvider, columns, ValueRow) (строки 818–820)
        Execute("CREATE TABLE t1(a INT, b INT)");
        Execute("CREATE TABLE t2(x INT, y INT)");
        Execute("INSERT INTO t1 VALUES (1, 2)");
        Execute("INSERT INTO t2 VALUES (1, 2)");

        Assert.Equal("1", Scalar("SELECT a FROM t1 WHERE (a,b) IN (SELECT x,y FROM t2)"));
    }

    [Fact]
    public void CompoundInConditionsSecondIsSkipped()
    {
        // Два IN(...) условия: второе видит inColumn уже установленным → CanUseIndexForIn возвращает false (строки 628–630)
        Execute("CREATE TABLE t(a INT, b INT)");
        Execute("CREATE INDEX ia ON t(a)");
        Execute("CREATE INDEX ib ON t(b)");
        Execute("INSERT INTO t VALUES (1, 3), (2, 4)");

        CommandContainer container = Prepare("SELECT * FROM t WHERE a IN (1, 2) AND b IN (3, 4)");
        using ResultInterface result = container.ExecuteQuery(10, 0, false);
        Assert.True(result.Next());
    }
}
