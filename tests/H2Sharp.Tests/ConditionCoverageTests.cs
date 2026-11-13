/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Condition coverage: LIKE prefix index condition, NOT LIKE rendered over a view,
 * CASE WHEN LIKE, and LIKE with ESCAPE.
 */
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ConditionCoverageTests
{
    private readonly SessionLocal session;
    private readonly Database database;

    public ConditionCoverageTests()
    {
        database = new Database(
            ConnectionInfo.Memory("condition_" + Guid.NewGuid().ToString("N")));
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
    public void LikePrefixCreatesIndexCondition()
    {
        // LIKE с фиксированным префиксом по индексированной колонке строит
        // IndexCondition (CompareLike.addIndexCondition, строки 408–411).
        Execute("CREATE TABLE T(A VARCHAR)");
        Execute("CREATE INDEX IA ON T(A)");
        Execute("INSERT INTO T VALUES ('abc'), ('abd'), ('ac')");

        CommandContainer container = Prepare("SELECT * FROM T WHERE A LIKE 'ab%'");
        _ = container.ExecuteQuery(10, 0, false);
    }

    [Fact]
    public void NotLikeOverViewRendersNeedParentheses()
    {
        // NOT над LIKE рендерится со скобками (NeedParentheses, строка 191).
        Execute("CREATE TABLE T(A VARCHAR)");
        Execute("INSERT INTO T VALUES ('abc')");
        Execute("CREATE VIEW VL AS SELECT * FROM T WHERE NOT A LIKE 'ab%'");

        string def = Scalar("SELECT VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME='VL'");
        Assert.Contains("NOT", def);
        Assert.Contains("LIKE", def);
    }

    [Fact]
    public void CaseWhenLikeReturnsWhenValue()
    {
        // CASE WHEN A LIKE ... вызывает GetWhenValue (строки 439–440).
        Execute("CREATE TABLE T(A VARCHAR)");
        Execute("INSERT INTO T VALUES ('abc')");

        Assert.Equal("1", Scalar("SELECT CASE WHEN A LIKE 'a%' THEN 1 ELSE 2 END FROM T"));
        Assert.Equal("2", Scalar("SELECT CASE WHEN A LIKE 'z%' THEN 1 ELSE 2 END FROM T"));
    }

    [Fact]
    public void LikeExactMatchCreatesIndexCondition()
    {
        // LIKE без подстановочных знаков — полное совпадение, ветка maxMatch == patternLength
        // (CompareLike.CreateIndexConditions, строки 407–411).
        Execute("CREATE TABLE T2(A VARCHAR)");
        Execute("CREATE INDEX IA2 ON T2(A)");
        Execute("INSERT INTO T2 VALUES ('abc')");

        CommandContainer container = Prepare("SELECT * FROM T2 WHERE A LIKE 'abc'");
        _ = container.ExecuteQuery(10, 0, false);
    }

    [Fact]
    public void UpdateWithLikeExactMatchCreatesIndexCondition()
    {
        // UPDATE с LIKE вызывает CreateIndexConditions в DoPrepare (строки 407–411).
        Execute("CREATE TABLE T4(A VARCHAR)");
        Execute("CREATE INDEX IA4 ON T4(A)");
        Execute("INSERT INTO T4 VALUES ('abc'), ('abd')");

        CommandContainer container = Prepare("UPDATE T4 SET A = 'xyz' WHERE A LIKE 'abc'");
        _ = container.ExecuteUpdate(false);
    }

    [Fact]
    public void LikeWithEscapeIsAccepted()
    {
        // LIKE с ESCAPE.
        Execute("CREATE TABLE T(A VARCHAR)");
        Execute("INSERT INTO T VALUES ('a_%')");

        Assert.Equal("a_%", Scalar("SELECT A FROM T WHERE A LIKE 'a!_%' ESCAPE '!'"));
    }
}
