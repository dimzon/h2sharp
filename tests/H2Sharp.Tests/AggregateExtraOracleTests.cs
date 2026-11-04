/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The aggregate families the first suites did not reach: the hypothetical-set ranks,
 * MODE with its tie-break order, HISTOGRAM, the ordered and DISTINCT forms of ARRAY_AGG
 * and LISTAGG, JSON_ARRAYAGG and JSON_OBJECTAGG with their NULL and FORMAT clauses, the
 * ENVELOPE quick path over a spatial index, GCD/LCM, the bit reductions, EVERY/ANY, and the
 * quick MIN/MAX reads.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class AggregateExtraOracleTests
{
    [JavaOracleFact]
    public void HypotheticalSetAndModeMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE T(ID INT, V INT)",
            "INSERT INTO T VALUES (1, 1), (2, 2), (3, 3), (4, 3), (5, 4), (6, 5), (7, 6)",
            "SELECT RANK(1) WITHIN GROUP (ORDER BY V), RANK(3) WITHIN GROUP (ORDER BY V), "
                + "RANK(7) WITHIN GROUP (ORDER BY V) FROM T",
            "SELECT DENSE_RANK(1) WITHIN GROUP (ORDER BY V), DENSE_RANK(3) WITHIN GROUP (ORDER BY V), "
                + "DENSE_RANK(7) WITHIN GROUP (ORDER BY V) FROM T",
            "SELECT ROUND(PERCENT_RANK(1) WITHIN GROUP (ORDER BY V), 2), "
                + "ROUND(PERCENT_RANK(3) WITHIN GROUP (ORDER BY V), 2), "
                + "ROUND(PERCENT_RANK(7) WITHIN GROUP (ORDER BY V), 2) FROM T",
            "SELECT ROUND(CUME_DIST(1) WITHIN GROUP (ORDER BY V), 2), "
                + "ROUND(CUME_DIST(3) WITHIN GROUP (ORDER BY V), 2), "
                + "ROUND(CUME_DIST(7) WITHIN GROUP (ORDER BY V), 2) FROM T",
            "SELECT RANK(3) WITHIN GROUP (ORDER BY V DESC), DENSE_RANK(3) WITHIN GROUP (ORDER BY V DESC) FROM T",
            "SELECT PERCENT_RANK(NULL) WITHIN GROUP (ORDER BY V), CUME_DIST(NULL) WITHIN GROUP (ORDER BY V), "
                + "RANK(NULL) WITHIN GROUP (ORDER BY V) FROM T",
            "SELECT RANK(3) WITHIN GROUP (ORDER BY V) FROM T WHERE FALSE",
            // MODE: the plain form, the order tie-break, a filter, and the Oracle spelling.
            "SELECT MODE(V) FROM T",
            "SELECT MODE(DISTINCT V) FROM T",
            "SELECT MODE(V), MODE() WITHIN GROUP (ORDER BY V DESC) FROM T",
            "SELECT MODE(V) FILTER (WHERE V > 1), MODE(V) FILTER (WHERE V < 0) FROM T",
            "SELECT STATS_MODE(V) FROM T",
            "CREATE TABLE TM(V INT)",
            "INSERT INTO TM VALUES (1), (2), (3), (3), (2), (1)",
            "SELECT MODE(V), MODE() WITHIN GROUP (ORDER BY V), MODE() WITHIN GROUP (ORDER BY V DESC) FROM TM",
            "CREATE TABLE TE(V INT)",
            "SELECT MODE(V) FROM TE",
            "SELECT MODE(V) FROM TE WHERE FALSE",
        ]);
    }

    [JavaOracleFact]
    public void HistogramAndCollectedAggregatesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "SELECT HISTOGRAM(X) FROM (VALUES (1), (2), (3), (1), (2), (NULL), (5)) T(X)",
            "SELECT HISTOGRAM(X) FILTER (WHERE X > 1) FROM (VALUES (1), (2), (3), (1), (2), (NULL), (5)) T(X)",
            "SELECT HISTOGRAM(X) FILTER (WHERE X > 0) FROM (VALUES (0)) T(X)",
            "SELECT HISTOGRAM(DISTINCT X) FROM (VALUES (0)) T(X)",
            "SELECT HISTOGRAM(ALL X) FROM (VALUES (0)) T(X)",
            "CREATE TABLE TS(V VARCHAR)",
            "INSERT INTO TS VALUES ('1'), ('2'), ('3'), ('4'), ('5'), ('6'), ('7'), ('8'), ('9')",
            "SELECT ARRAY_AGG(V ORDER BY V), ARRAY_AGG(V ORDER BY V DESC) FILTER (WHERE V >= '4') "
                + "FROM TS WHERE V >= '2'",
            "CREATE INDEX TS_IDX ON TS(V)",
            "SELECT ARRAY_AGG(V ORDER BY V), ARRAY_AGG(V ORDER BY V DESC) FILTER (WHERE V >= '4') FROM TS",
            "CREATE TABLE TI(ID INT, V INT)",
            "INSERT INTO TI VALUES (1, 7), (2, 2), (3, 8), (4, 3), (5, 7), (6, 3), (7, 9), (8, -1)",
            "SELECT ARRAY_AGG(V) FROM TI",
            "SELECT ARRAY_AGG(DISTINCT V) FROM TI",
            "SELECT ARRAY_AGG(DISTINCT V ORDER BY V DESC) FROM TI",
            "SELECT ARRAY_AGG(V) FILTER (WHERE V > 7) FROM TI",
            "SELECT LISTAGG(V, '-') WITHIN GROUP (ORDER BY V), "
                + "LISTAGG(V, '-') WITHIN GROUP (ORDER BY V DESC) FILTER (WHERE V >= '4') "
                + "FROM TS WHERE V >= '2'",
            "SELECT GROUP_CONCAT(V ORDER BY V ASC SEPARATOR '-'), "
                + "GROUP_CONCAT(V ORDER BY V DESC SEPARATOR '-') FILTER (WHERE V >= '4') FROM TS",
            "SELECT GROUP_CONCAT(V) FROM TI",
            "SELECT GROUP_CONCAT(DISTINCT V) FROM TI",
            "SELECT GROUP_CONCAT(DISTINCT V ORDER BY V DESC) FROM TI",
            "INSERT INTO TI VALUES (9, NULL)",
            "SELECT LISTAGG(V, ',') WITHIN GROUP (ORDER BY ID) FROM TI",
            "SELECT LISTAGG(V, ',') WITHIN GROUP (ORDER BY ID) FILTER (WHERE V IS NOT NULL) FROM TI",
            "SELECT LISTAGG(V, ',') WITHIN GROUP (ORDER BY ID) FROM TI WHERE FALSE",
        ]);
    }

    [JavaOracleFact]
    public void JsonAggregatesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE TJ(ID INT, N VARCHAR, J JSON)",
            "INSERT INTO TJ VALUES (1, '10', JSON '10'), (2, NULL, NULL), (3, 'null', JSON 'null'), "
                + "(4, 'false', JSON 'false'), (5, 'false', JSON 'false')",
            "SELECT JSON_ARRAYAGG(J NULL ON NULL) FROM TJ",
            "SELECT JSON_ARRAYAGG(N NULL ON NULL) FROM TJ",
            "SELECT JSON_ARRAYAGG(N FORMAT JSON NULL ON NULL) FROM TJ",
            "SELECT JSON_ARRAYAGG(J) FROM TJ",
            "SELECT JSON_ARRAYAGG(N) FROM TJ",
            "SELECT JSON_ARRAYAGG(N FORMAT JSON) FROM TJ",
            "SELECT JSON_ARRAYAGG(ALL J) FROM TJ",
            "SELECT JSON_ARRAYAGG(DISTINCT J) FROM TJ",
            "SELECT JSON_ARRAYAGG(J ABSENT ON NULL) FROM TJ",
            "SELECT JSON_ARRAYAGG(J ORDER BY ID DESC NULL ON NULL) FROM TJ",
            "INSERT INTO TJ VALUES (6, 'Ten', JSON '-10')",
            "SELECT JSON_OBJECTAGG(N: J) FROM TJ",
            "SELECT JSON_OBJECTAGG(N: J ABSENT ON NULL) FROM TJ",
            "SELECT JSON_OBJECTAGG(N: J ABSENT ON NULL) FILTER (WHERE J IS NULL) FROM TJ",
            "SELECT JSON_OBJECTAGG(N: J) FILTER (WHERE FALSE) FROM TJ",
            "SELECT JSON_OBJECTAGG(NULL: J) FROM TJ",
            "SELECT JSON_OBJECTAGG(N: J) FROM TJ WHERE FALSE",
        ]);
    }

    [JavaOracleFact]
    public void EnvelopeAndQuickMinMaxMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE TG(ID INT, V GEOMETRY)",
            "SELECT ENVELOPE(V) FROM TG",
            "INSERT INTO TG VALUES (1, 'POINT(1 1)')",
            "SELECT ENVELOPE(V) FROM TG",
            "INSERT INTO TG VALUES (2, 'POINT(1 2)'), (3, NULL), (4, 'POINT(3 1)')",
            "SELECT ENVELOPE(V), ENVELOPE(V) FILTER (WHERE V <> 'POINT(3 1)'), "
                + "ENVELOPE(V) FILTER (WHERE V <> 'POINT(1 2)') FROM TG",
            // Without an index the column expression cannot take the quick path.
            "SELECT ENVELOPE(N) FROM (SELECT V AS N FROM TG)",
            "CREATE SPATIAL INDEX TG_IDX ON TG(V)",
            "SELECT ENVELOPE(V) FROM TG",
            "SELECT ENVELOPE(V) FROM TG WHERE V <> 'POINT(3 1)'",
            "SELECT ENVELOPE(V) FROM TG WHERE FALSE",
            // The quick MIN/MAX reads: an indexed column, a plain column and an empty table.
            "CREATE TABLE TI(ID INT, V INT, K VARCHAR)",
            "INSERT INTO TI VALUES (1, 5, 'e'), (2, 3, 'c'), (3, 9, 'i'), (4, NULL, NULL)",
            "CREATE INDEX TI_V ON TI(V)",
            "SELECT MIN(V), MAX(V) FROM TI",
            "SELECT MIN(V), MAX(V) FROM TI WHERE ID > 1",
            "SELECT MIN(K), MAX(K) FROM TI",
            "SELECT MIN(V), MAX(V) FROM TI WHERE FALSE",
            "SELECT MIN(ID), MAX(ID) FROM TI",
        ]);
    }

    [JavaOracleFact]
    public void ReductionsAndBooleanAggregatesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE TR(ID INT, V INT, B BOOLEAN)",
            "INSERT INTO TR VALUES (1, 12, TRUE), (2, 18, FALSE), (3, 24, NULL), (4, -6, TRUE)",
            "SELECT GCD_AGG(V), LCM_AGG(V) FROM TR",
            "SELECT GCD_AGG(DISTINCT V), LCM_AGG(DISTINCT V) FROM TR",
            "SELECT GCD_AGG(V), LCM_AGG(V) FROM TR WHERE FALSE",
            "SELECT BIT_AND_AGG(V), BIT_OR_AGG(V), BIT_XOR_AGG(V) FROM TR",
            "SELECT BIT_AND_AGG(DISTINCT V), BIT_OR_AGG(DISTINCT V), BIT_XOR_AGG(DISTINCT V) FROM TR",
            "SELECT BIT_AND_AGG(V), BIT_OR_AGG(V), BIT_XOR_AGG(V) FROM TR WHERE FALSE",
            "SELECT EVERY(B), ANY(B) FROM TR",
            "SELECT EVERY(B) FILTER (WHERE ID > 1), ANY(B) FILTER (WHERE ID > 1) FROM TR",
            "SELECT EVERY(B), ANY(B) FROM TR WHERE FALSE",
            "SELECT ANY(V), EVERY(V) FROM TR",
        ]);
    }
}
