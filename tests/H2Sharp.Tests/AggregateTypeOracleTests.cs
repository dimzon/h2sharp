/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The aggregate result types and the aggregate SQL surface: AVG and SUM over every numeric,
 * DECFLOAT and INTERVAL type as the reference avg.sql and sum.sql derive them, the COUNT
 * rewrites, aggregates over an empty table, DISTINCT and FILTER in groups and in windows, the
 * window frames, and the SQL an EXPLAIN prints for the ordered, distinct, JSON and
 * truncating aggregates.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class AggregateTypeOracleTests
{
    private const string AllTypes =
        "CREATE TABLE S(B BOOLEAN, N1 TINYINT, N2 SMALLINT, N4 INTEGER, N8 BIGINT, "
        + "N NUMERIC(10, 2), F4 REAL, F8 DOUBLE PRECISION, D DECFLOAT(10), "
        + "I1 INTERVAL YEAR(3), I2 INTERVAL MONTH(3), I3 INTERVAL DAY(3), I4 INTERVAL HOUR(3), "
        + "I5 INTERVAL MINUTE(3), I6 INTERVAL SECOND(2), I7 INTERVAL YEAR(3) TO MONTH, "
        + "I8 INTERVAL DAY(3) TO HOUR, I9 INTERVAL DAY(3) TO MINUTE, "
        + "I10 INTERVAL DAY(3) TO SECOND(2), I11 INTERVAL HOUR(3) TO MINUTE, "
        + "I12 INTERVAL HOUR(3) TO SECOND(2), I13 INTERVAL MINUTE(3) TO SECOND(2))";

    private const string TypeQuery =
        "SELECT COLUMN_NAME, DATA_TYPE_SQL('PUBLIC', 'A', 'TABLE', DTD_IDENTIFIER) TYPE "
        + "FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'A' ORDER BY ORDINAL_POSITION";

    [JavaOracleFact]
    public void TheAvgResultTypesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            // The reference's functions/aggregate/avg.sql ranges.
            "SELECT AVG(CAST(X AS INT)) FROM SYSTEM_RANGE(2147483547, 2147483637)",
            "SELECT AVG(X) FROM SYSTEM_RANGE(9223372036854775707, 9223372036854775797)",
            "SELECT AVG(CAST(100 AS TINYINT)) FROM SYSTEM_RANGE(1, 1000)",
            "SELECT AVG(CAST(100 AS SMALLINT)) FROM SYSTEM_RANGE(1, 1000)",
            "CREATE TABLE TEST(V INT)",
            "INSERT INTO TEST VALUES (10), (20), (30), (40), (50), (60), (70), (80), (90), (100), (110), (120)",
            "SELECT AVG(V), AVG(V) FILTER (WHERE V >= 40) FROM TEST WHERE V <= 100",
            "CREATE INDEX TEST_IDX ON TEST(V)",
            "SELECT AVG(V), AVG(V) FILTER (WHERE V >= 40) FROM TEST WHERE V <= 100",
            "DROP TABLE TEST",
            // The derived column types, one per input type.
            AllTypes,
            "CREATE TABLE A AS SELECT AVG(N1) N1, AVG(N2) N2, AVG(N4) N4, AVG(N8) N8, AVG(N) N, "
                + "AVG(F4) F4, AVG(F8) F8, AVG(D) D, AVG(I1) I1, AVG(I2) I2, AVG(I3) I3, "
                + "AVG(I4) I4, AVG(I5) I5, AVG(I6) I6, AVG(I7) I7, AVG(I8) I8, AVG(I9) I9, "
                + "AVG(I10) I10, AVG(I11) I11, AVG(I12) I12, AVG(I13) I13 FROM S",
            TypeQuery,
            "DROP TABLE S, A",
            // AVG over INTERVAL, NUMERIC, and the wide DECFLOAT values.
            "SELECT AVG(X) FROM (VALUES INTERVAL '1' DAY, INTERVAL '2' DAY) T(X)",
            "SELECT AVG(X) FROM (VALUES CAST(1 AS NUMERIC(1)), CAST(2 AS NUMERIC(1))) T(X)",
            "SELECT AVG(I) FROM (VALUES 9e99999 - 1, 1e99999 + 1) T(I)",
            "SELECT AVG(I) = 5E99999 FROM (VALUES CAST(9e99999 - 1 AS NUMERIC(100000)), "
                + "CAST(1e99999 + 1 AS NUMERIC(100000))) T(I)",
            "SELECT AVG(I) FROM (VALUES INTERVAL '999999999999999999' SECOND, INTERVAL '1' SECOND) T(I)",
        ]);
    }

    [JavaOracleFact]
    public void TheSumResultTypesAndWindowsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            // The reference's functions/aggregate/sum.sql ranges and the interval sum.
            "SELECT SUM(CAST(X AS INT)) FROM SYSTEM_RANGE(2147483547, 2147483637)",
            "SELECT SUM(X) FROM SYSTEM_RANGE(9223372036854775707, 9223372036854775797)",
            "SELECT SUM(CAST(100 AS TINYINT)) FROM SYSTEM_RANGE(1, 1000)",
            "SELECT SUM(CAST(100 AS SMALLINT)) FROM SYSTEM_RANGE(1, 1000)",
            "CREATE TABLE TEST(V INT)",
            "INSERT INTO TEST VALUES (1), (2), (3), (4), (5), (6), (7), (8), (9), (10), (11), (12)",
            "SELECT SUM(V), SUM(ALL V), SUM(DISTINCT V) FROM TEST",
            "DROP TABLE TEST",
            "CREATE TABLE TEST(V INTERVAL DAY TO SECOND)",
            "INSERT INTO TEST VALUES ('0 1'), ('0 2'), ('0 2'), ('0 2'), ('-0 1'), ('-0 1')",
            "SELECT SUM(V) FROM TEST",
            "DROP TABLE TEST",
            // The SUM result types over every input type.
            AllTypes,
            "CREATE TABLE A AS SELECT SUM(N1) N1, SUM(N2) N2, SUM(N4) N4, SUM(N8) N8, SUM(N) N, "
                + "SUM(F4) F4, SUM(F8) F8, SUM(D) D, SUM(I1) I1, SUM(I2) I2, SUM(I3) I3, "
                + "SUM(I4) I4, SUM(I5) I5, SUM(I6) I6, SUM(I7) I7, SUM(I8) I8, SUM(I9) I9, "
                + "SUM(I10) I10, SUM(I11) I11, SUM(I12) I12, SUM(I13) I13 FROM S",
            TypeQuery,
            "DROP TABLE S, A",
            // Aggregates over an empty table, grouped and windowed.
            "CREATE TABLE TEST(ID INT)",
            "SELECT SUM(ID) FROM TEST",
            "SELECT SUM(ID) OVER () FROM TEST",
            "DROP TABLE TEST",
            // Window frames.
            "SELECT ID, SUM(ID) OVER (ORDER BY ID) S, "
                + "SUM(ID) OVER (ORDER BY ID RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) S_U_C, "
                + "SUM(ID) OVER (ORDER BY ID RANGE BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING) S_C_U, "
                + "SUM(ID) OVER (ORDER BY ID RANGE BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) S_U_U "
                + "FROM (SELECT X ID FROM SYSTEM_RANGE(1, 8))",
            "SELECT I, V, SUM(V) OVER W S, SUM(DISTINCT V) OVER W D FROM "
                + "VALUES (1, 1), (2, 1), (3, 1), (4, 1), (5, 2), (6, 2), (7, 3) T(I, V) "
                + "WINDOW W AS (ORDER BY I)",
            "SELECT * FROM (SELECT SUM(V) OVER (ORDER BY V ROWS BETWEEN CURRENT ROW AND CURRENT ROW) S "
                + "FROM (VALUES 1, 2, 2) T(V))",
            "SELECT V, SUM(V) FILTER (WHERE V <> 1) OVER (ROWS CURRENT ROW) S "
                + "FROM (VALUES 1, 2, 2) T(V)",
            "SELECT V, "
                + "SUM(V) FILTER (WHERE V <> 1) OVER (ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) S, "
                + "SUM(V) FILTER (WHERE V <> 1) OVER (ROWS BETWEEN UNBOUNDED PRECEDING AND 1 FOLLOWING) T "
                + "FROM (VALUES 1, 2, 2) T(V)",
            // The nested aggregate through the group-sorted and window paths.
            "SELECT X, COUNT(*), SUM(COUNT(*)) OVER() FROM VALUES (1), (1), (1), (1), (2), (2), (3) T(X) "
                + "GROUP BY X ORDER BY X",
            // LISTAGG as a window function: with and without WITHIN GROUP and with a filter.
            "CREATE TABLE G(G INT, V INT)",
            "INSERT INTO G VALUES (1, 1), (1, 2), (1, 3), (2, 4), (2, 5), (2, 6), (3, NULL)",
            "SELECT G, LISTAGG(V, '-') WITHIN GROUP (ORDER BY NULL) FROM G GROUP BY G ORDER BY G",
            "SELECT G, LISTAGG(V, '-') OVER (PARTITION BY G) FROM G ORDER BY V",
            "SELECT G, LISTAGG(V, '-') WITHIN GROUP (ORDER BY V) "
                + "FILTER (WHERE V <> 2) OVER (PARTITION BY G) FROM G ORDER BY V",
            "SELECT G, ARRAY_AGG(V ORDER BY V) OVER (PARTITION BY G) FROM G ORDER BY V",
            "DROP TABLE G",
        ]);
    }

    [JavaOracleFact]
    public void TheCountRewritesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE TEST(V INT)",
            "INSERT INTO TEST VALUES (1), (2), (3), (NULL)",
            "SELECT COUNT(*), COUNT(1), COUNT(V), COUNT(NULL), COUNT(CAST(NULL AS INT)), "
                + "COUNT(DISTINCT 1), COUNT(DISTINCT V) FROM TEST",
            "SELECT COUNT(1) FROM TEST WHERE FALSE",
            "SELECT COUNT(*) FROM TEST WHERE FALSE",
            "SELECT COUNT(NULL) FROM TEST WHERE FALSE",
            "SELECT COUNT(DISTINCT 1) FROM TEST WHERE FALSE",
            "SELECT COUNT(1) FILTER (WHERE V > 1), COUNT(*) FILTER (WHERE FALSE) FROM TEST",
            "DROP TABLE TEST",
            "SELECT COUNT(*) FROM (VALUES 1, 2, 3) T(X)",
            "SELECT COUNT(1) FROM (VALUES 1, 2, 3) T(X)",
        ]);
    }

    [JavaOracleFact]
    public void TheAggregatePlansMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE T(ID INT, V INT, S VARCHAR, J JSON)",
            "INSERT INTO T VALUES (1, 10, 'a', JSON '{\"a\": 1}')",
            "EXPLAIN SELECT COUNT(*), SUM(V), MIN(V), MAX(V), AVG(V) FROM T",
            "EXPLAIN SELECT COUNT(DISTINCT V), SUM(DISTINCT V), AVG(DISTINCT V) FROM T",
            "EXPLAIN SELECT STDDEV_POP(V), STDDEV_SAMP(V), VAR_POP(V), VAR_SAMP(V) FROM T",
            "EXPLAIN SELECT MEDIAN(V), PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY V), "
                + "PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY V) FROM T",
            "EXPLAIN SELECT LISTAGG(S, ',') WITHIN GROUP (ORDER BY ID) FROM T",
            "EXPLAIN SELECT LISTAGG(DISTINCT S, ',') WITHIN GROUP (ORDER BY S) FROM T",
            "EXPLAIN SELECT LISTAGG(S, ',' ON OVERFLOW TRUNCATE) WITHIN GROUP (ORDER BY ID) FROM T",
            "EXPLAIN SELECT LISTAGG(S, ',' ON OVERFLOW TRUNCATE '...' WITH COUNT) "
                + "WITHIN GROUP (ORDER BY ID) FROM T",
            "EXPLAIN SELECT ARRAY_AGG(S ORDER BY ID) FROM T",
            "EXPLAIN SELECT ARRAY_AGG(DISTINCT S ORDER BY S) FROM T",
            "EXPLAIN SELECT JSON_ARRAYAGG(S) FROM T",
            "EXPLAIN SELECT JSON_ARRAYAGG(S FORMAT JSON ABSENT ON NULL) FROM T",
            "EXPLAIN SELECT JSON_OBJECTAGG(ID: S) FROM T",
            "EXPLAIN SELECT SUM(V) FILTER (WHERE V > 1) FROM T",
            "EXPLAIN SELECT SUM(V) OVER (PARTITION BY ID ORDER BY V ROWS UNBOUNDED PRECEDING) FROM T",
            "EXPLAIN SELECT SUM(V) OVER (PARTITION BY ID ORDER BY V ROWS UNBOUNDED PRECEDING) FILTER (WHERE V > 1) FROM T",
            // The rest of the aggregate names, so every SQL rendering is asked for.
            "EXPLAIN SELECT ANY_VALUE(V), ANY(V), EVERY(V) FROM T",
            "EXPLAIN SELECT REGR_COUNT(V, ID), REGR_AVGX(V, ID), REGR_AVGY(V, ID), "
                + "REGR_SXX(V, ID), REGR_SYY(V, ID), REGR_SXY(V, ID), COVAR_POP(V, ID), "
                + "COVAR_SAMP(V, ID), CORR(V, ID), REGR_SLOPE(V, ID), REGR_INTERCEPT(V, ID), "
                + "REGR_R2(V, ID) FROM T",
            "EXPLAIN SELECT BIT_AND_AGG(V), BIT_OR_AGG(V), BIT_XOR_AGG(V), "
                + "BIT_NAND_AGG(V), BIT_NOR_AGG(V), BIT_XNOR_AGG(V) FROM T",
            "EXPLAIN SELECT GCD_AGG(V), LCM_AGG(V), HISTOGRAM(V), MODE(V) FROM T",
            // A non-deterministic argument, which no cache may reuse.
            "EXPLAIN SELECT SUM(RAND()) FROM T",
        ]);
    }
}
