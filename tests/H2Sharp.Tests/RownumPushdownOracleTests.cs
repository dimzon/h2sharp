/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * ROWNUM() and the derived-table condition pushdown: a condition on a derived table whose
 * query uses ROWNUM() must not reach the inner query, or the row numbers change. The
 * reference refuses index conditions on a non-query-comparable derived table; this locks
 * the rows and the plans that show the condition staying outside.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class RownumPushdownOracleTests
{
    [JavaOracleFact]
    public void RownumOverAnOrderedSubqueryMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(1))",
            "INSERT INTO TEST(ID, NAME) VALUES (1, 'b'), (3, 'a')",
            "SELECT ID FROM TEST ORDER BY NAME",
            "SELECT ROWNUM() AS NR, A.ID AS ID FROM (SELECT ID FROM TEST ORDER BY NAME) A",
            // The outer condition must stay outside the derived table: id = 1 is the second
            // row in name order, so its ROWNUM is 2, not 1.
            "SELECT NR FROM (SELECT ROWNUM() AS NR, A.ID AS ID "
                + "FROM (SELECT ID FROM TEST ORDER BY NAME) A) B WHERE B.ID = 1",
            "SELECT NR FROM (SELECT ROWNUM() AS NR, A.ID AS ID "
                + "FROM (SELECT ID FROM TEST ORDER BY NAME) A) B WHERE B.NR = 2",
            "SELECT NR FROM (SELECT ROWNUM() AS NR, A.ID AS ID "
                + "FROM (SELECT ID FROM TEST ORDER BY NAME) A) B WHERE B.ID > 0 ORDER BY B.ID",
            // The window function computes after the filter, so its numbering is unaffected.
            "SELECT NR FROM (SELECT ROW_NUMBER() OVER () AS NR, A.ID AS ID "
                + "FROM (SELECT ID FROM TEST ORDER BY NAME) A) B WHERE B.ID = 1",
            "SELECT NR FROM (SELECT ROW_NUMBER() OVER () AS NR, A.ID AS ID "
                + "FROM (SELECT ID FROM TEST ORDER BY NAME) A) B WHERE B.ID > 0 ORDER BY B.ID",
            // A plain derived table without ROWNUM still pushes its equality condition.
            "SELECT ID FROM (SELECT ID FROM TEST) B WHERE B.ID = 1",
            "SELECT ID FROM (SELECT ID FROM TEST ORDER BY NAME) B WHERE B.ID = 1",
            "DROP TABLE TEST",
        ]);
    }

    [JavaOracleFact]
    public void JoiningAWindowSubqueryMatchesJdbc()
    {
        // Issue #71: the join condition must not be pushed into a derived table that
        // computes a window function. With the reference's QUERY_COMPARABLE rule the
        // derived table refuses index conditions and the join stays outside.
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE Posts(Id INT PRIMARY KEY, TopicId INT, CreatedAt TIMESTAMP)",
            "INSERT INTO Posts VALUES (1, 1, TIMESTAMP '2024-01-01 10:00:00')",
            "INSERT INTO Posts VALUES (2, 1, TIMESTAMP '2024-01-02 10:00:00')",
            "INSERT INTO Posts VALUES (3, 2, TIMESTAMP '2024-01-03 10:00:00')",
            "SELECT p1.Id, p1.Rank FROM Posts p "
                + "INNER JOIN (SELECT p0.Id, ROW_NUMBER() OVER(ORDER BY p0.Id) AS Rank "
                + "FROM Posts p0) p1 ON p.Id = p1.Id",
            "SELECT p.Id, p1.Rank FROM Posts p "
                + "INNER JOIN (SELECT p0.Id, ROW_NUMBER() OVER(ORDER BY p0.Id) AS Rank "
                + "FROM Posts p0) p1 ON p.Id = p1.Id WHERE p1.Rank = 2",
            // A plain derived table has no window function and still takes the condition.
            "SELECT p.Id, q.Id FROM Posts p "
                + "INNER JOIN (SELECT Id FROM Posts) q ON p.Id = q.Id ORDER BY p.Id",
            "DROP TABLE Posts",
        ]);
    }

    [JavaOracleFact]
    public void TheWindowJoinPlanMatchesJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE Posts(Id INT PRIMARY KEY, TopicId INT, CreatedAt TIMESTAMP)",
            "INSERT INTO Posts VALUES (1, 1, TIMESTAMP '2024-01-01 10:00:00')",
            "INSERT INTO Posts VALUES (2, 1, TIMESTAMP '2024-01-02 10:00:00')",
            "INSERT INTO Posts VALUES (3, 2, TIMESTAMP '2024-01-03 10:00:00')",
            "EXPLAIN SELECT p1.Id, p1.Rank FROM Posts p "
                + "INNER JOIN (SELECT p0.Id, ROW_NUMBER() OVER(ORDER BY p0.Id) AS Rank "
                + "FROM Posts p0) p1 ON p.Id = p1.Id",
        ]);
    }

    [JavaOracleFact]
    public void TheRownumPlansKeepTheConditionOutside()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(1))",
            "EXPLAIN SELECT NR FROM (SELECT ROWNUM() AS NR, A.ID AS ID "
                + "FROM (SELECT ID FROM TEST ORDER BY NAME) A) B WHERE B.ID = 1",
            "EXPLAIN SELECT NR FROM (SELECT ROW_NUMBER() OVER () AS NR, A.ID AS ID "
                + "FROM (SELECT ID FROM TEST ORDER BY NAME) A) B WHERE B.ID = 1",
            "EXPLAIN SELECT ID FROM (SELECT ID FROM TEST) B WHERE B.ID = 1",
        ]);
    }
}
