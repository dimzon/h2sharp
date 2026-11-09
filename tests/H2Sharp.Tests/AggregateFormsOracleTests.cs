/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 87: aggregate forms the other suites do not reach — MODE with and
 * without WITHIN GROUP, LISTAGG/ARRAY_AGG/JSON aggregates with ORDER BY and
 * FILTER, the aggregate names in plans, and aggregates inside grouping.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class AggregateFormsOracleTests
{
    [JavaOracleFact]
    public void ModeFormsMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE AM(V INT)",
            "SELECT MODE(V) FROM AM",
            "SELECT MODE(DISTINCT V) FROM AM",
            "INSERT INTO AM VALUES (NULL)",
            "SELECT MODE(V) FROM AM",
            "INSERT INTO AM VALUES (1), (2), (3), (1), (2), (1)",
            "SELECT MODE(V), MODE() WITHIN GROUP (ORDER BY V DESC) FROM AM",
            "SELECT MODE(V) FILTER (WHERE V > 1), MODE(V) FILTER (WHERE V < 0) FROM AM",
            "INSERT INTO AM VALUES (2), (3), (3)",
            "SELECT MODE(V ORDER BY V) FROM AM",
            "SELECT MODE(V ORDER BY V DESC) FROM AM",
            "SELECT MODE() WITHIN GROUP (ORDER BY V) FROM AM",
            "SELECT MODE() WITHIN GROUP (ORDER BY V DESC) FROM AM",
            // A NULL shared argument short-circuits the percentile without touching the rows.
            "SELECT PERCENTILE_CONT(NULL) WITHIN GROUP (ORDER BY V) FROM AM",
            "DROP TABLE AM",
        ]);
    }

    [JavaOracleFact]
    public void ListaggAndArrayAggMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE AL(ID INT, NAME VARCHAR)",
            "INSERT INTO AL VALUES (1, 'a'), (2, 'b'), (3, 'a')",
            "SELECT LISTAGG(ID) WITHIN GROUP (ORDER BY ID) FROM AL",
            "SELECT LISTAGG(ID, ';') WITHIN GROUP (ORDER BY NULL) FROM AL",
            "SELECT LISTAGG(DISTINCT NAME, ', ') WITHIN GROUP (ORDER BY NAME) FROM AL",
            "SELECT LISTAGG(NAME, ', ') WITHIN GROUP (ORDER BY NAME DESC) FROM AL",
            "SELECT LISTAGG(NAME, ', ') WITHIN GROUP (ORDER BY NAME) FILTER (WHERE ID > 1) FROM AL",
            "SELECT CARDINALITY(ARRAY_AGG(ID ORDER BY ID)) FROM AL",
            "SELECT ARRAY_GET(ARRAY_AGG(DISTINCT NAME ORDER BY NAME), 1) FROM AL",
            "SELECT ID % 2, LISTAGG(NAME, ',') WITHIN GROUP (ORDER BY NAME) FROM AL GROUP BY ID % 2 ORDER BY 1",
            // Window aggregates keep their arguments and ORDER BY expressions per row. The
            // OVER ORDER BY is what makes the aggregate ordered, so the remembering path runs.
            "SELECT ID, CARDINALITY(ARRAY_AGG(ID ORDER BY ID) OVER (PARTITION BY NAME ORDER BY ID)) FROM AL ORDER BY ID",
            "SELECT ID, CARDINALITY(ARRAY_AGG(ID ORDER BY ID) FILTER (WHERE ID > 1) OVER (PARTITION BY NAME ORDER BY ID)) FROM AL ORDER BY ID",
            "EXPLAIN SELECT LISTAGG(NAME, ', ') WITHIN GROUP (ORDER BY NAME) FROM AL",
            "DROP TABLE AL",
        ]);
    }

    [JavaOracleFact]
    public void JsonAggregatesMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE AJ(N VARCHAR, J VARCHAR)",
            "INSERT INTO AJ VALUES ('a', '1'), ('b', '2')",
            "SELECT CAST(JSON_OBJECTAGG(KEY N VALUE J) AS VARCHAR) FROM AJ",
            "SELECT CAST(JSON_OBJECTAGG(N VALUE J) AS VARCHAR) FROM AJ",
            "SELECT CAST(JSON_OBJECTAGG(N: J) AS VARCHAR) FROM AJ",
            "SELECT CAST(JSON_OBJECTAGG(N: J ABSENT ON NULL) AS VARCHAR) FROM AJ",
            "SELECT CAST(JSON_ARRAYAGG(J ORDER BY J) AS VARCHAR) FROM AJ",
            "SELECT CAST(JSON_ARRAYAGG(DISTINCT J ORDER BY J) AS VARCHAR) FROM AJ",
            "SELECT CAST(JSON_OBJECTAGG(NULL: J) AS VARCHAR) FROM AJ",
            "EXPLAIN SELECT JSON_ARRAYAGG(J ORDER BY J), JSON_OBJECTAGG(N VALUE J) FROM AJ",
            "DROP TABLE AJ",
        ]);
    }

    [JavaOracleFact]
    public void AggregateNamesInPlanMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE AG(X INT, Y INT)",
            "INSERT INTO AG VALUES (1, 10), (2, 20), (3, 30)",
            "EXPLAIN SELECT EVERY(X > 0), BIT_AND_AGG(X), BIT_OR_AGG(X), BIT_XOR_AGG(X), "
                + "REGR_COUNT(Y, X), REGR_AVGX(Y, X), REGR_AVGY(Y, X), "
                + "REGR_SXX(Y, X), REGR_SYY(Y, X), REGR_SXY(Y, X), "
                + "REGR_SLOPE(Y, X), REGR_INTERCEPT(Y, X), REGR_R2(Y, X), "
                + "COVAR_POP(Y, X), COVAR_SAMP(Y, X), CORR(Y, X) FROM AG",
            "SELECT CASE WHEN EVERY(X > 0) THEN 1 ELSE 0 END, BIT_AND_AGG(X), BIT_OR_AGG(X), BIT_XOR_AGG(X) FROM AG",
            // A view stores the SQL of its aggregate expressions, so creating one renders
            // them: the EVERY and REGR_COUNT names, the multi-column ORDER BY separator of
            // a hypothetical-set aggregate, and JSON_ARRAYAGG's DISTINCT.
            "CREATE VIEW AGG_V1 AS SELECT EVERY(X > 0), REGR_COUNT(Y, X) FROM AG",
            "CREATE VIEW AGG_V2 AS SELECT JSON_ARRAYAGG(DISTINCT CAST(X AS VARCHAR)) FROM AG",
            "CREATE VIEW AGG_V3 AS SELECT RANK(1, 2) WITHIN GROUP (ORDER BY X, Y) FROM AG",
            "DROP VIEW AGG_V1",
            "DROP VIEW AGG_V2",
            "DROP VIEW AGG_V3",
            "DROP TABLE AG",
        ]);
    }

    [JavaOracleFact]
    public void NonDeterministicAggregateArgumentsMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE AD(X INT)",
            "INSERT INTO AD VALUES (1), (2), (3)",
            // The draws differ (System.Random vs JDK by default), so only their presence
            // is compared; the aggregate still walks a non-deterministic argument and
            // ORDER BY expression during prepare. The ORDER BY one is a statement of its
            // own: the cache stops at the first non-deterministic expression it meets, so
            // a preceding SUM(RAND) would hide the ORDER BY walk behind it.
            "SELECT CASE WHEN ARRAY_AGG(X ORDER BY RAND(1)) IS NOT NULL THEN 1 ELSE 0 END FROM AD",
            "SELECT CASE WHEN SUM(RAND(1)) IS NOT NULL THEN 1 ELSE 0 END FROM AD",
            "DROP TABLE AD",
        ]);
    }
}