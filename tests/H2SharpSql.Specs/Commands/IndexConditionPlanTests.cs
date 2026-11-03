// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// What a WHERE clause becomes in the plan: the equality, the ranges, the IN list, the ANY
// over an array column, the IN over a query and the row-value IN list are printed inside the
// index annotation, and the always-false condition prints as such.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class IndexConditionPlanTests
{
    private static IDbConnection GivenIndexed()
    {
        IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE T(A INT PRIMARY KEY, B INT, C INT)");
        _ = connection.Execute("CREATE TABLE U(ID INT PRIMARY KEY, V INT)");
        _ = connection.Execute("CREATE TABLE U2(ID INT PRIMARY KEY, ARR INT ARRAY)");
        _ = connection.Execute("CREATE TABLE T3(A INT PRIMARY KEY, B INT, C INT)");
        _ = connection.Execute("CREATE INDEX IDX_AB ON T(B, A)");
        _ = connection.Execute("CREATE INDEX IDX_C ON T(C)");
        _ = connection.Execute("CREATE INDEX IDX_C_DESC ON T(C DESC)");
        _ = connection.Execute("CREATE INDEX IDX_AB3 ON T3(A, B)");
        _ = connection.Execute("INSERT INTO T VALUES (1, 10, 100), (2, 20, 200), (3, 30, 300)");
        _ = connection.Execute("INSERT INTO U VALUES (1, 10), (2, 20)");
        _ = connection.Execute("INSERT INTO U2 VALUES (1, ARRAY[10, 30])");
        _ = connection.Execute("INSERT INTO T3 VALUES (1, 10, 100), (2, 20, 200), (3, 30, 300)");
        return connection;
    }

    private static string Plan(IDbConnection connection, string query)
        => connection.QuerySingle<string>("EXPLAIN " + query);

    [Fact]
    public void ThePlanNamesTheIndexAndTheBounds()
    {
        using IDbConnection connection = GivenIndexed();

        string equality = Plan(connection, "SELECT A FROM T WHERE B = 10");
        Assert.Contains("IDX_AB: B = 10", equality, StringComparison.Ordinal);

        Assert.Contains("IDX_AB: B >= 10", Plan(connection, "SELECT A FROM T WHERE B >= 10"), StringComparison.Ordinal);
        Assert.Contains("IDX_AB: B > 10", Plan(connection, "SELECT A FROM T WHERE B > 10"), StringComparison.Ordinal);
        Assert.Contains("IDX_AB: B <= 10", Plan(connection, "SELECT A FROM T WHERE B <= 10"), StringComparison.Ordinal);
        Assert.Contains("IDX_AB: B < 10", Plan(connection, "SELECT A FROM T WHERE B < 10"), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlanPrintsTheNullSafeComparisons()
    {
        using IDbConnection connection = GivenIndexed();

        Assert.Contains(
            "IDX_AB: B IS NOT DISTINCT FROM 10",
            Plan(connection, "SELECT A FROM T WHERE B IS NOT DISTINCT FROM 10"),
            StringComparison.Ordinal);
        Assert.Contains(
            "IDX_AB: B IS NULL",
            Plan(connection, "SELECT A FROM T WHERE B IS NOT DISTINCT FROM NULL"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlanPrintsTheInListTheAnyAndTheQuery()
    {
        using IDbConnection connection = GivenIndexed();

        string list = Plan(connection, "SELECT A FROM T WHERE B IN (10, 30)");
        Assert.Contains("IDX_AB: B IN(10, 30)", list, StringComparison.Ordinal);

        // An IN list with a column in it is not evaluatable, so no index condition survives.
        string columnList = Plan(connection, "SELECT A FROM T WHERE B IN (A, 10)");
        Assert.Contains("IDX_AB */", columnList, StringComparison.Ordinal);
        Assert.DoesNotContain("IDX_AB:", columnList, StringComparison.Ordinal);

        // ANY over an array column keeps the expression in the condition.
        string any = Plan(connection, "SELECT A FROM T, U2 WHERE B = ANY(U2.ARR)");
        Assert.Contains("IDX_AB: B = ANY(", any, StringComparison.Ordinal);

        string query = Plan(connection, "SELECT A FROM T WHERE B IN (SELECT V FROM U)");
        Assert.Contains("IDX_AB: B IN(SELECT", query, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlanPrintsTheRowValueInListInTheIndexOrder()
    {
        using IDbConnection connection = GivenIndexed();

        string plan = Plan(connection, "SELECT A FROM T WHERE (B, A) IN ((10, 1), (30, 3))");
        Assert.Contains("IDX_AB:  IN(", plan, StringComparison.Ordinal);
        Assert.Contains("ROW (10, 1)", plan, StringComparison.Ordinal);

        // The index keeps (A, B); the condition is written (B, A) and is reordered to it.
        string reordered = Plan(connection, "SELECT A FROM T3 WHERE (B, A) IN ((10, 1), (30, 3))");
        Assert.Contains("IDX_AB3:  IN(", reordered, StringComparison.Ordinal);
        Assert.Contains("ROW (1, 10)", reordered, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlanPrintsAnAlwaysFalseCondition()
    {
        using IDbConnection connection = GivenIndexed();

        Assert.Contains("tableScan: FALSE */", Plan(connection, "SELECT A FROM T WHERE 1 = 0"), StringComparison.Ordinal);
        Assert.Contains("tableScan: FALSE */", Plan(connection, "SELECT A FROM T WHERE FALSE"), StringComparison.Ordinal);
    }

    [Fact]
    public void ADerivedTableKeepsTheInConditionOutOfTheIndex()
    {
        using IDbConnection connection = GivenIndexed();
        _ = connection.Execute("CREATE TABLE TEST2(A INT, B INT, ID INT, NAME INT, PRIMARY KEY(ID, NAME))");
        _ = connection.Execute("CREATE UNIQUE INDEX C2 ON TEST2(B, A)");
        _ = connection.Execute("INSERT INTO TEST2 VALUES (1, 10, 1, 1), (2, 20, 2, 2)");

        // An IN combined with another condition cannot use the index of a derived table, so
        // the index is named without a condition.
        string plan = Plan(connection, "SELECT * FROM (SELECT A, B FROM TEST2) WHERE A = 1 AND B IN (10, 20)");
        Assert.Contains("C2 */", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("C2: ", plan, StringComparison.Ordinal);

        string compound = Plan(connection, "SELECT * FROM (SELECT A, B FROM TEST2) WHERE A = 1 AND (B, A) IN ((10, 1))");
        Assert.Contains("C2 */", compound, StringComparison.Ordinal);
        Assert.DoesNotContain("C2: ", compound, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlanPrintsTheQuantifiedForms()
    {
        using IDbConnection connection = GivenIndexed();

        Assert.Contains(
            "WHERE \"B\" NOT IN(",
            Plan(connection, "SELECT A FROM T WHERE B NOT IN (SELECT V FROM U)"),
            StringComparison.Ordinal);
        // ANY over an equality is IN; with another operator it keeps the ANY form.
        Assert.Contains(
            "WHERE \"B\" IN(",
            Plan(connection, "SELECT A FROM T WHERE B = ANY(SELECT V FROM U)"),
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE \"B\" > ANY(",
            Plan(connection, "SELECT A FROM T WHERE B > ANY(SELECT V FROM U)"),
            StringComparison.Ordinal);
        // The reference writes the ALL form without a space before the operator.
        Assert.Contains(
            "WHERE \"B\"<> ALL(",
            Plan(connection, "SELECT A FROM T WHERE B <> ALL(SELECT V FROM U)"),
            StringComparison.Ordinal);
        Assert.Contains(
            "NOT (\"B\"<> ALL(",
            Plan(connection, "SELECT A FROM T WHERE NOT (B <> ALL(SELECT V FROM U))"),
            StringComparison.Ordinal);
        // A condition inside AND keeps its parentheses in the plan's WHERE line.
        Assert.Contains(
            "AND (\"B\" IN(",
            Plan(connection, "SELECT A FROM T WHERE B IN (SELECT V FROM U) AND A > 1"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheDescendingIndexServesTheInList()
    {
        using IDbConnection connection = GivenIndexed();

        // The descending index answers the same set, in its own order.
        Assert.Equal(
            [100, 300],
            connection.Query<int>("SELECT C FROM T WHERE C IN (100, 300) ORDER BY C").AsList());
    }
}
