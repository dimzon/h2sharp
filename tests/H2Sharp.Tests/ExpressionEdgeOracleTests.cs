/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 217: the expression edges — the index conditions a LIKE and a comparison build
 * on the UPDATE and DELETE paths (exact and prefix LIKE, the smaller-equal rewrite), the
 * percentile aggregates over every numeric kind through an index (empty, null, single),
 * the ordered-set MODE, the JSON_OBJECTAGG null key, and the promotion corners of unknown
 * and null types.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ExpressionEdgeOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void LikeBuildsIndexConditionsOnDml()
    {
        AssertSameAsReference("expr_like_index",
            "CREATE TABLE T(NAME VARCHAR(20), A INT)",
            "CREATE INDEX IN ON T(NAME)",
            "INSERT INTO T VALUES ('abc', 1), ('abd', 2), ('abcd', 3), ('x%y', 4), ('a!b', 5), (NULL, 6)",
            "UPDATE T SET A = 10 WHERE NAME LIKE 'abc'",
            "SELECT COUNT(*) FROM T WHERE A = 10",
            "UPDATE T SET A = 11 WHERE NAME LIKE 'abc%'",
            "SELECT COUNT(*) FROM T WHERE A = 11",
            "DELETE FROM T WHERE NAME LIKE 'ab_'",
            "SELECT COUNT(*) FROM T",
            "DELETE FROM T WHERE NAME LIKE '%bc'",
            "DELETE FROM T WHERE NAME LIKE 'a!%%' ESCAPE '!'",
            "DELETE FROM T WHERE NAME LIKE 'a!_%' ESCAPE '!'",
            "DELETE FROM T WHERE NAME LIKE 'x' ESCAPE NULL",
            "DELETE FROM T WHERE NAME LIKE NULL",
            "DELETE FROM T WHERE NOT NAME LIKE 'abc%'",
            "DELETE FROM T WHERE A LIKE '1%'",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void ComparisonBuildsIndexConditionsOnDml()
    {
        AssertSameAsReference("expr_cmp_index",
            "CREATE TABLE T(A INT, B INT)",
            "CREATE INDEX IA ON T(A)",
            "INSERT INTO T SELECT X, 100 - X FROM SYSTEM_RANGE(1, 100)",
            "DELETE FROM T WHERE A < 50",
            "DELETE FROM T WHERE A > 50",
            "UPDATE T SET B = B + 1 WHERE A <= 40",
            "UPDATE T SET B = B + 1 WHERE A >= 40",
            "DELETE FROM T WHERE A <> 40",
            "DELETE FROM T WHERE A = 40",
            "DELETE FROM T WHERE (A, B) < (1, 1)",
            "CREATE INDEX IAB ON T(A, B)",
            "DELETE FROM T WHERE (A, B) < (B, A)",
            "DELETE FROM T WHERE (A, B) = (B, A)",
            "DELETE FROM T WHERE (A, B) < (SELECT A, B FROM T WHERE A = 1)",
            "UPDATE T SET B = B WHERE 100 > A",
            "UPDATE T SET B = B WHERE 0 < A",
            "DELETE FROM T WHERE 100 >= A",
            "DELETE FROM T WHERE 0 <= A",
            "DELETE FROM T WHERE 100 = A",
            "DELETE FROM T WHERE 100 <> A",
            "SELECT COUNT(*) FROM T",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void PercentilesThroughAnIndex()
    {
        AssertSameAsReference("expr_percentile",
            "CREATE TABLE T(G INT, X INT, Y BIGINT, Z DOUBLE)",
            "CREATE INDEX IX ON T(X)",
            "CREATE INDEX IY ON T(Y DESC)",
            "CREATE INDEX IZ ON T(Z)",
            "INSERT INTO T VALUES (1, 1, 10, 1.5), (1, 2, 20, 2.5), (1, 3, 30, 3.5), (1, 4, 40, 4.5)",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY X) FROM T",
            "SELECT PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY X) FROM T",
            "SELECT PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY X) FROM T",
            "SELECT PERCENTILE_CONT(0.75) WITHIN GROUP (ORDER BY X) FROM T",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY Y) FROM T",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY X DESC) FROM T",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY Z) FROM T",
            "SELECT PERCENTILE_DISC(0.25) WITHIN GROUP (ORDER BY X) FROM T",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY X) FROM T WHERE 1 = 0",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY NULL) FROM T",
            "CREATE TABLE S(X INT)",
            "CREATE INDEX ISX ON S(X)",
            "INSERT INTO S VALUES (7)",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY X) FROM S",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY X) OVER () FROM S",
            "DROP TABLE S",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void OrderedSetModeAndJsonKeyEdges()
    {
        AssertSameAsReference("expr_agg_edges",
            "CREATE TABLE T(V INT)",
            "INSERT INTO T VALUES (1), (1), (2), (NULL)",
            "SELECT MODE(V) FROM T",
            "SELECT MODE(DISTINCT V) FROM T",
            "SELECT MODE() WITHIN GROUP (ORDER BY V) FROM T",
            "SELECT MODE() WITHIN GROUP (ORDER BY V DESC) FROM T",
            "CREATE TABLE J(K VARCHAR(10), V INT)",
            "INSERT INTO J VALUES ('a', 1), ('b', 2)",
            "SELECT CAST(JSON_OBJECTAGG(K: V) AS VARCHAR) FROM J",
            "SELECT CAST(JSON_OBJECTAGG(NULLIF(K, 'a'): V) AS VARCHAR) FROM J",
            "CREATE TABLE SP(G GEOMETRY)",
            "CREATE SPATIAL INDEX ISP ON SP(G)",
            "INSERT INTO SP VALUES (GEOMETRY 'POINT (1 2)')",
            "SELECT COUNT(*) FROM SP WHERE G && GEOMETRY 'POINT (1 2)'",
            "SELECT COUNT(*) FROM SP WHERE GEOMETRY 'POINT (1 2)' && G",
            "DROP TABLE SP",
            "DROP TABLE J",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void PromotionOfUnknownAndNullTypes()
    {
        AssertSameAsReference("expr_promotion",
            "SELECT ? + NULL",
            "SELECT ? + ?",
            "SELECT CAST(NULL AS INT) + ?",
            "SELECT ? = ?",
            "SELECT CAST(1 AS NUMERIC(10, 5)) + ?",
            "SELECT CAST(1 AS NUMERIC(100, 2)) * ?");
    }
}
