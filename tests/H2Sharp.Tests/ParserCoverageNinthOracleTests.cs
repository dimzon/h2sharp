/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Parser coverage (ninth wave): JSON predicate forms, functional indexes, percentile
 * aggregates, deep operator trees — run through the ADO.NET layer and compared with the
 * reference jar.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserCoverageNinthOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void JsonPredicateForms()
    {
        AssertSameAsReference("parser_ninth1",
            "SELECT CASE WHEN '{}' IS JSON OBJECT THEN 1 ELSE 0 END",
            "SELECT CASE WHEN '[]' IS JSON ARRAY THEN 1 ELSE 0 END",
            "SELECT CASE WHEN '5' IS JSON SCALAR THEN 1 ELSE 0 END",
            "SELECT CASE WHEN '{}' IS JSON WITH UNIQUE KEYS THEN 1 ELSE 0 END",
            "SELECT CASE WHEN '{\"a\":1,\"a\":2}' IS JSON WITH UNIQUE KEYS THEN 1 ELSE 0 END",
            "SELECT CASE WHEN '{\"a\":1}' IS JSON WITHOUT UNIQUE KEYS THEN 1 ELSE 0 END",
            "SELECT CASE WHEN NULL IS JSON THEN 1 ELSE 0 END");
    }

    [JavaOracleFact]
    public void FunctionalIndexes()
    {
        AssertSameAsReference("parser_ninth2",
            "CREATE TABLE T(ID INT, NAME VARCHAR(20))",
            "CREATE INDEX IX ON T(LOWER(NAME))",
            "INSERT INTO T VALUES(1, 'Alice')",
            "SELECT ID FROM T WHERE LOWER(NAME) = 'alice'",
            "SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME='T'",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void PercentileAggregates()
    {
        AssertSameAsReference("parser_ninth3",
            "CREATE TABLE T(VAL INT)",
            "INSERT INTO T VALUES(1),(2),(3),(4),(5)",
            "SELECT CAST(PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY VAL) AS NUMERIC(10,4)) FROM T",
            "SELECT PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY VAL) FROM T",
            "SELECT CAST(PERCENTILE_CONT(0.9) WITHIN GROUP (ORDER BY VAL) AS NUMERIC(10,4)) FROM T",
            "DROP TABLE T");
    }

    [JavaOracleFact]
    public void DeepOperatorTrees()
    {
        AssertSameAsReference("parser_ninth4",
            "SELECT 1 + 2 + 3 + 4 + 5",
            "SELECT 1 - 2 - 3 - 4 - 5",
            "SELECT 1 * 2 * 3 * 4 * 5",
            "SELECT 1 + 2 * 3 + 4 * 5 + 6");
    }
}
