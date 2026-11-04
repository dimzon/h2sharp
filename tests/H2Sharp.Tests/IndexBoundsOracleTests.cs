/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The bounds an index cursor is given: the tighter of two ranges, the NULL bound that is not a
 * bound, a second IN that has no list of its own, compound IN lists and IN subqueries, a
 * descending index column, and two intersects conditions over one spatial index.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class IndexBoundsOracleTests
{
    [JavaOracleFact]
    public void AscendingIndexBoundsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE T(ID INT, V INT)",
            "INSERT INTO T VALUES (1, 10), (2, 20), (3, 30), (4, 40), (5, 50), (6, 60), (NULL, 70)",
            "CREATE INDEX I ON T(ID)",
            "SELECT ID FROM T WHERE ID > 2 AND ID > 4 ORDER BY ID",
            "SELECT ID FROM T WHERE ID < 5 AND ID < 3 ORDER BY ID",
            "SELECT ID FROM T WHERE ID >= 1 AND ID <= 2 ORDER BY ID",
            "SELECT ID FROM T WHERE ID >= 1 AND ID > 1 AND ID < 5 AND ID <= 4 ORDER BY ID",
            "SELECT ID FROM T WHERE ID IS NULL",
            "SELECT ID FROM T WHERE ID = 3 AND ID = 4",
            "SELECT ID FROM T WHERE ID = 3 AND ID = 3",
            "SELECT ID FROM T WHERE ID IN (1, 2) AND ID IN (3, 4) ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (2, 4, 9) ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (2, 4, 9) AND ID > 3 ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (2, 9) AND ID > 1 AND ID < 5 ORDER BY ID",
            "SELECT ID FROM T WHERE ID NOT IN (2, NULL)",
            "SELECT ID FROM T WHERE ID IN (SELECT ID FROM T WHERE ID > 3) ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (SELECT V FROM T) ORDER BY ID",
            "SELECT ID FROM T WHERE ID IN (SELECT ID FROM T WHERE ID > 9) ORDER BY ID",
            "SELECT ID FROM T WHERE _ROWID_ > 0 ORDER BY ID",
            "SELECT ID FROM T WHERE _ROWID_ >= 2 AND _ROWID_ < 4 ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void DescendingIndexBoundsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE TD(ID INT)",
            "INSERT INTO TD VALUES (1), (2), (3), (4), (5), (6), (NULL)",
            "CREATE INDEX IDESC ON TD(ID DESC)",
            "SELECT ID FROM TD WHERE ID > 2 ORDER BY ID",
            "SELECT ID FROM TD WHERE ID < 5 ORDER BY ID",
            "SELECT ID FROM TD WHERE ID >= 2 AND ID <= 4 ORDER BY ID",
            "SELECT ID FROM TD WHERE ID > 1 AND ID > 4 ORDER BY ID",
            "SELECT ID FROM TD WHERE ID IS NULL",
            "SELECT ID FROM TD ORDER BY ID",
            "SELECT ID FROM TD WHERE ID IN (2, 5) ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void CompoundIndexBoundsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE C(A INT, B INT)",
            "INSERT INTO C VALUES (1, 2), (3, 4), (5, 6), (NULL, 2), (2, NULL)",
            "CREATE INDEX IC ON C(A, B)",
            "SELECT A, B FROM C WHERE (A, B) IN ((1, 2), (3, 4)) ORDER BY A, B",
            "SELECT A, B FROM C WHERE (A, B) IN ((1, 2), (9, 9)) ORDER BY A, B",
            "CREATE TABLE C2(A INT, B INT)",
            "INSERT INTO C2 VALUES (3, 4), (5, 6)",
            "SELECT A, B FROM C WHERE (A, B) IN (SELECT A, B FROM C2) ORDER BY A, B",
            "SELECT A, B FROM C WHERE (A, B) IN (SELECT A, B FROM C2 WHERE A > 4) ORDER BY A, B",
            "SELECT A, B FROM C WHERE (A, B) IN (SELECT A, B FROM C2 WHERE A > 9) ORDER BY A, B",
            "SELECT A, B FROM C WHERE (A, B) IN ((1, 2)) AND (A, B) IN ((3, 4)) ORDER BY A, B",
            "SELECT A, B FROM C WHERE B = 2 ORDER BY A NULLS LAST",
            "SELECT A, B FROM C WHERE A = 3 AND B = 4",
        ]);
    }

    [JavaOracleFact]
    public void TwoIntersectsConditionsUseTheirUnionMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE G(ID INT, GEOM GEOMETRY)",
            "INSERT INTO G VALUES (1, 'POINT (1 1)'), (2, 'POINT (9 9)'), (3, 'POINT (15 15)')",
            "CREATE SPATIAL INDEX SI ON G(GEOM)",
            "SELECT ID FROM G WHERE GEOM && CAST('POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))' AS GEOMETRY) "
                + "AND GEOM && CAST('POLYGON((5 5, 20 5, 20 20, 5 20, 5 5))' AS GEOMETRY) ORDER BY ID",
            "SELECT ID FROM G WHERE GEOM && CAST('POLYGON((0 0, 2 0, 2 2, 0 2, 0 0))' AS GEOMETRY) "
                + "AND GEOM && CAST('POLYGON((5 5, 20 5, 20 20, 5 20, 5 5))' AS GEOMETRY) ORDER BY ID",
            "SELECT ID FROM G WHERE GEOM && CAST('POLYGON((0 0, 30 0, 30 30, 0 30, 0 0))' AS GEOMETRY) "
                + "AND GEOM && CAST('POLYGON((0 0, 30 0, 30 30, 0 30, 0 0))' AS GEOMETRY) AND ID > 1 ORDER BY ID",
        ]);
    }

    [JavaOracleFact]
    public void ThePlansChooseTheSameIndexes()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE C(A INT, B INT)",
            "CREATE INDEX IC ON C(A, B)",
            "CREATE TABLE C2(A INT, B INT)",
            "EXPLAIN SELECT A, B FROM C WHERE (A, B) IN (SELECT A, B FROM C2)",
            "EXPLAIN SELECT A, B FROM C WHERE (A, B) IN ((1, 2), (3, 4))",
            "CREATE TABLE TD(ID INT)",
            "CREATE INDEX IDESC ON TD(ID DESC)",
            "EXPLAIN SELECT ID FROM TD WHERE ID > 2",
            "EXPLAIN SELECT ID FROM TD WHERE ID < 5",
            "CREATE TABLE G(ID INT, GEOM GEOMETRY)",
            "CREATE SPATIAL INDEX SI ON G(GEOM)",
            "EXPLAIN SELECT ID FROM G WHERE GEOM && CAST('POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))' AS GEOMETRY) "
                + "AND GEOM && CAST('POLYGON((5 5, 20 5, 20 20, 5 20, 5 5))' AS GEOMETRY)",
            "EXPLAIN SELECT ID FROM G WHERE GEOM && CAST('POLYGON((0 0, 2 0, 2 2, 0 2, 0 0))' AS GEOMETRY) "
                + "AND GEOM && CAST('POLYGON((5 5, 20 5, 20 20, 5 20, 5 5))' AS GEOMETRY)",
            "EXPLAIN SELECT ID FROM G WHERE GEOM && CAST('POLYGON((0 0, 30 0, 30 30, 0 30, 0 0))' AS GEOMETRY) "
                + "AND GEOM && CAST('POLYGON((0 0, 30 0, 30 30, 0 30, 0 0))' AS GEOMETRY)",
        ]);
    }

    [JavaOracleFact]
    public void DroppingColumnsWithIndexesMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE D1(A INT, B INT)",
            "CREATE INDEX IX1 ON D1(A, B)",
            "ALTER TABLE D1 DROP COLUMN A",
            "ALTER TABLE D1 DROP COLUMN B",
            "SELECT * FROM D1",
            "CREATE TABLE D2(A INT, B INT)",
            "CREATE INDEX IX2 ON D2(A, B)",
            "ALTER TABLE D2 DROP COLUMN (A, B)",
            "SELECT * FROM D2",
            "CREATE TABLE D3(A INT, B INT, UNIQUE(A, B))",
            "ALTER TABLE D3 DROP COLUMN A",
            "ALTER TABLE D3 DROP COLUMN (A, B)",
            "CREATE TABLE D4(A INT, B INT, CONSTRAINT U4 UNIQUE(A))",
            "ALTER TABLE D4 DROP COLUMN A",
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'D4'",
        ]);
    }
}
