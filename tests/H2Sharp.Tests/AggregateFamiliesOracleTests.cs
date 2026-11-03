/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The aggregate families the older suites did not drive: hypothetical-set ranking, MODE,
 * the JSON aggregates, the SUM/AVG type derivation, the index quick path of MIN/MAX, and
 * the SQL printing EXPLAIN reads back.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Aggregate behavior against the reference over SQL, through the same JVM connection
/// comparison the provider-issue suites use.
/// </summary>
/// <remarks>
/// The hypothetical-set functions take their arguments from WITHIN GROUP, so a parser or a
/// result-type mistake shows as a different row or a different error code, not as a compile
/// error. The EXPLAIN group prints each aggregate's own SQL through its unenclosed form,
/// which is the only observable road to the printing code.
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class AggregateFamiliesOracleTests
{
    [JavaOracleFact]
    public void HypotheticalSetRankingMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE H(ID INT, N INT)",
            "INSERT INTO H VALUES (1, 10), (2, 20), (2, 20), (3, 30), (4, 40), (5, NULL)",
            "SELECT RANK(20) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT RANK(25) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT RANK(0) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT RANK(100) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT DENSE_RANK(20) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT DENSE_RANK(25) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT DENSE_RANK(0) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT DENSE_RANK(100) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT PERCENT_RANK(20) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT PERCENT_RANK(25) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT PERCENT_RANK(0) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT PERCENT_RANK(100) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT CUME_DIST(20) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT CUME_DIST(25) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT CUME_DIST(0) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT CUME_DIST(100) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT RANK(CAST(NULL AS INT)) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT CUME_DIST(CAST(NULL AS INT)) WITHIN GROUP (ORDER BY N) FROM H",
            "SELECT RANK(20, 2) WITHIN GROUP (ORDER BY N, ID) FROM H",
            "SELECT DENSE_RANK(20, 2) WITHIN GROUP (ORDER BY N, ID) FROM H",
            "SELECT PERCENT_RANK(20, 2) WITHIN GROUP (ORDER BY N, ID) FROM H",
            "SELECT CUME_DIST(20, 2) WITHIN GROUP (ORDER BY N, ID) FROM H",
            "SELECT RANK(5) WITHIN GROUP (ORDER BY N) FROM H WHERE FALSE",
            "SELECT DENSE_RANK(5) WITHIN GROUP (ORDER BY N) FROM H WHERE FALSE",
            "SELECT PERCENT_RANK(5) WITHIN GROUP (ORDER BY N) FROM H WHERE FALSE",
            "SELECT CUME_DIST(5) WITHIN GROUP (ORDER BY N) FROM H WHERE FALSE",
        ]);
    }

    [JavaOracleFact]
    public void ModeAndJsonAggregatesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE M(V VARCHAR, K INT)",
            "INSERT INTO M VALUES ('a', 1), ('b', 2), ('b', 3), ('c', 4), ('c', 5), (NULL, 6)",
            "SELECT MODE() WITHIN GROUP (ORDER BY V) FROM M",
            "SELECT MODE() WITHIN GROUP (ORDER BY V DESC) FROM M",
            "SELECT MODE() WITHIN GROUP (ORDER BY K) FROM M",
            "SELECT MODE() WITHIN GROUP (ORDER BY V) FILTER (WHERE K < 5) FROM M",
            "SELECT MODE() WITHIN GROUP (ORDER BY V) FROM M WHERE FALSE",
            "SELECT MODE(V) FROM M",
            "SELECT MODE() OVER () FROM M",
            "SELECT MODE() WITHIN GROUP (ORDER BY V) OVER () FROM M",
            "SELECT JSON_ARRAYAGG(K) FROM M",
            "SELECT JSON_ARRAYAGG(K ORDER BY K DESC) FROM M",
            "SELECT JSON_ARRAYAGG(V) FROM M",
            "SELECT JSON_ARRAYAGG(V ABSENT ON NULL) FROM M",
            "SELECT JSON_ARRAYAGG(V NULL ON NULL) FROM M",
            "SELECT JSON_ARRAYAGG(K) FROM M WHERE FALSE",
            "SELECT JSON_OBJECTAGG(K, V) FROM M",
            "SELECT JSON_OBJECTAGG(KEY V VALUE K) FROM M",
            "SELECT JSON_OBJECTAGG(K, V ABSENT ON NULL) FROM M",
            "SELECT JSON_OBJECTAGG(K, V NULL ON NULL) FROM M",
            "SELECT JSON_OBJECTAGG(K, V) FROM M WHERE FALSE",
            "SELECT JSON_ARRAYAGG(FORMATDATETIME(DATE '2020-01-01', 'yyyy')) FROM M",
            "SELECT STDDEV_POP(DISTINCT K), STDDEV_SAMP(DISTINCT K), VAR_POP(DISTINCT K), VAR_SAMP(DISTINCT K) FROM M WHERE FALSE",
            // ANY_VALUE(DISTINCT x) picks an entry with Random, so its answer is not
            // comparable; the empty-group road below is deterministic.
            "SELECT ANY_VALUE(DISTINCT V) FROM M WHERE FALSE",
        ]);
    }

    [JavaOracleFact]
    public void SumAndAvgTypesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE TY(A INT, B BIGINT, C NUMERIC(10, 2), D DOUBLE, E REAL, F SMALLINT, G TINYINT)",
            "INSERT INTO TY VALUES (1, 2, 3.25, 4.5, 5.5, 6, 7), (8, 9, 10.75, 11.5, 12.5, 13, 14)",
            "SELECT SUM(A), SUM(B), SUM(C), SUM(D), SUM(E), SUM(F), SUM(G) FROM TY",
            "SELECT AVG(A), AVG(B), AVG(C), AVG(D), AVG(E), AVG(F), AVG(G) FROM TY",
            "SELECT SUM(DISTINCT A), AVG(DISTINCT A) FROM TY",
            "SELECT SUM(DISTINCT B) FROM TY WHERE FALSE",
            "SELECT AVG(DISTINCT C) FROM TY WHERE FALSE",
            "SELECT SUM(CAST(A AS INTERVAL DAY)), AVG(CAST(B AS INTERVAL HOUR)) FROM TY",
            "SELECT SUM(CAST(A AS INTERVAL YEAR TO MONTH)), AVG(CAST(B AS INTERVAL YEAR TO MONTH)) FROM TY",
            "SELECT SUM(CAST(C AS NUMERIC(100, 2))), AVG(CAST(C AS NUMERIC(100, 2))) FROM TY",
            "SELECT SUM(A), AVG(B) FROM TY WHERE FALSE",
        ]);
    }

    [JavaOracleFact]
    public void MinMaxQuickPathMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE MX(ID INT PRIMARY KEY, N INT)",
            "INSERT INTO MX VALUES (1, 10), (2, 20), (3, 30)",
            "SELECT MIN(ID), MAX(ID), MIN(N), MAX(N) FROM MX",
            "SELECT MIN(ID), MAX(ID) FROM MX WHERE FALSE",
            "SELECT MIN(ID), MAX(ID) FROM MX WHERE ID > 1",
            "CREATE TABLE MD(ID INT, N INT)",
            "CREATE INDEX MD_ID_DESC ON MD(ID DESC)",
            "INSERT INTO MD VALUES (1, 10), (2, 20), (3, 30)",
            "SELECT MIN(ID), MAX(ID) FROM MD",
            "SELECT MIN(ID + 0), MAX(ID + 0) FROM MD",
            "SELECT MIN(N), MAX(N) FROM MD",
            "CREATE TABLE MI(ID INT)",
            "CREATE INDEX MI_ID ON MI(ID)",
            "INSERT INTO MI VALUES (5), (1), (3)",
            "SELECT MIN(ID), MAX(ID) FROM MI",
            "SELECT MIN(ID) FROM MI WHERE ID > 2",
            "SELECT MAX(ID) FROM MI WHERE ID < 2",
        ]);
    }

    [JavaOracleFact]
    public void ExplainPrintsAggregateSqlLikeJdbc()
    {
        // EXPLAIN answers one cell holding the whole multi-line plan, so the comparison
        // has to escape the newlines — the ExplainOracleTests harness does exactly that.
        ExplainOracleTests.Check([
            "CREATE TABLE H(ID INT, N INT, V VARCHAR)",
            "INSERT INTO H VALUES (1, 10, 'a'), (2, 20, 'b'), (3, 20, 'c'), (4, 30, NULL)",
            "EXPLAIN SELECT COUNT(*), COUNT(N), SUM(N), AVG(N), MIN(N), MAX(N) FROM H",
            "EXPLAIN SELECT COUNT(DISTINCT N), SUM(DISTINCT N), AVG(DISTINCT N), STDDEV_POP(DISTINCT N), VAR_SAMP(DISTINCT N) FROM H",
            "EXPLAIN SELECT LISTAGG(V, ',') WITHIN GROUP (ORDER BY V) FROM H",
            "EXPLAIN SELECT LISTAGG(DISTINCT V, ':') WITHIN GROUP (ORDER BY V DESC) FILTER (WHERE N > 10) FROM H",
            "EXPLAIN SELECT JSON_ARRAYAGG(N ORDER BY N) FROM H",
            "EXPLAIN SELECT JSON_OBJECTAGG(ID, N) FROM H",
            "EXPLAIN SELECT ARRAY_AGG(N ORDER BY N) FROM H",
            "EXPLAIN SELECT MEDIAN(N), PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY N), PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY N) FROM H",
            "EXPLAIN SELECT MODE() WITHIN GROUP (ORDER BY V) FROM H",
            "EXPLAIN SELECT RANK(20) WITHIN GROUP (ORDER BY N), DENSE_RANK(20) WITHIN GROUP (ORDER BY N) FROM H",
            "EXPLAIN SELECT RANK() OVER (ORDER BY N), DENSE_RANK() OVER (ORDER BY N), CUME_DIST() OVER (ORDER BY N), PERCENT_RANK() OVER (ORDER BY N) FROM H",
            "EXPLAIN SELECT BIT_AND_AGG(N), BIT_OR_AGG(N), BIT_XOR_AGG(N), BIT_NAND_AGG(N), BIT_NOR_AGG(N), BIT_XNOR_AGG(N) FROM H",
            "EXPLAIN SELECT GCD_AGG(N), LCM_AGG(N) FROM H",
            "EXPLAIN SELECT ANY(N), EVERY(N > 5), ANY_VALUE(N) FROM H",
            "EXPLAIN SELECT COUNT(N) FILTER (WHERE N > 10), MAX(N) FILTER (WHERE N < 30) FROM H",
        ]);
    }
}
