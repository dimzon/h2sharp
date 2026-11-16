/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 224: the query-plan edges — window and grouped lazy results, DISTINCT with ORDER BY,
 * LIMIT/OFFSET, ORDER BY on an index, EXPLAIN of a grouped query, joins with and without an
 * index, and the ordered-set aggregate ties.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class QueryPlanEdgeOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void WindowAndGroupedLazyResults()
    {
        AssertSameLinesAsReference("qpe_window",
            "CREATE TABLE T(ID INT, G INT, V INT)",
            "INSERT INTO T VALUES (1, 1, 10), (2, 1, 20), (3, 2, 20), (4, 2, 5)",
            "SELECT ID, ROW_NUMBER() OVER (ORDER BY V, ID), RANK() OVER (PARTITION BY G ORDER BY V) "
            + "FROM T ORDER BY ID",
            "SELECT ID, SUM(V) OVER (PARTITION BY G ORDER BY ID ROWS UNBOUNDED PRECEDING), "
            + "LAG(V) OVER (PARTITION BY G ORDER BY ID) FROM T ORDER BY ID",
            "SELECT G, COUNT(*), SUM(V) FROM T GROUP BY G HAVING SUM(V) > 15 ORDER BY G",
            "SELECT G, COUNT(*) FROM T GROUP BY G ORDER BY COUNT(*) DESC LIMIT 1",
            "SELECT DISTINCT G, V FROM T ORDER BY G, V",
            "SELECT DISTINCT G FROM T ORDER BY G DESC LIMIT 1 OFFSET 1",
            "EXPLAIN SELECT G, COUNT(*), SUM(V) FROM T GROUP BY G HAVING SUM(V) > 15 ORDER BY G LIMIT 1",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void IndexOrderAndSubqueryLimits()
    {
        AssertSameLinesAsReference("qpe_index",
            "CREATE TABLE T(ID INT, V INT)",
            "CREATE INDEX IV ON T(V)",
            "INSERT INTO T SELECT X, 100 - X FROM SYSTEM_RANGE(1, 10)",
            "SELECT V FROM T ORDER BY V LIMIT 3",
            "SELECT V FROM T ORDER BY V DESC LIMIT 3 OFFSET 2",
            "SELECT * FROM T WHERE ID IN (SELECT ID FROM T ORDER BY V LIMIT 2) ORDER BY ID",
            "SELECT * FROM T ORDER BY V NULLS LAST LIMIT 2",
            "EXPLAIN SELECT V FROM T ORDER BY V LIMIT 3",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void JoinsWithAndWithoutAnIndex()
    {
        AssertSameLinesAsReference("qpe_join",
            "CREATE TABLE A(ID INT PRIMARY KEY, V INT)",
            "CREATE TABLE B(ID INT, AID INT, W INT)",
            "CREATE INDEX IB ON B(AID)",
            "INSERT INTO A VALUES (1, 10), (2, 20), (3, 30)",
            "INSERT INTO B VALUES (1, 1, 100), (2, 1, 200), (3, 2, 300)",
            "SELECT A.ID, B.W FROM A JOIN B ON A.ID = B.AID ORDER BY A.ID, B.W",
            "SELECT A.ID, B.W FROM A LEFT JOIN B ON A.ID = B.AID AND B.W > 100 ORDER BY A.ID, B.W",
            "SELECT A.ID FROM A WHERE EXISTS (SELECT 1 FROM B WHERE B.AID = A.ID)",
            "SELECT A.ID, B.W FROM A, B WHERE A.ID = B.AID ORDER BY A.ID, B.W",
            "EXPLAIN SELECT A.ID, B.W FROM A JOIN B ON A.ID = B.AID",
            "DROP TABLE B",
            "DROP TABLE A");
    }

    [JavaOracleFact]
    public void OrderedSetAggregateTies()
    {
        AssertSameLinesAsReference("qpe_mode",
            "CREATE TABLE T(V INT)",
            "INSERT INTO T VALUES (1), (1), (2), (2), (2), (3)",
            "SELECT MODE(V) FROM T",
            "SELECT MODE(DISTINCT V) FROM T",
            "SELECT MODE() WITHIN GROUP (ORDER BY V) FROM T",
            "SELECT MODE() WITHIN GROUP (ORDER BY V DESC) FROM T",
            "DROP TABLE T");
    }
}
