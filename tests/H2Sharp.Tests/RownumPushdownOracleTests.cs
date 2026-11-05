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
