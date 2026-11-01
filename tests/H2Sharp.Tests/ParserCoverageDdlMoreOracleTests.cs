/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Parser coverage (more): table variants, limit/offset forms, interval qualifiers,
 * nested expressions, enum/array types — run through the ADO.NET layer and compared with
 * the reference jar.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserCoverageDdlMoreOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void TableVariants()
    {
        AssertSameAsReference("parser_more1",
            "CREATE LOCAL TEMPORARY TABLE LT1(ID INT)",
            "CREATE GLOBAL TEMPORARY TABLE GT1(ID INT)",
            "CREATE TEMP TABLE TP1(ID INT)",
            "CREATE TEMPORARY TABLE TC1(ID INT) ON COMMIT DROP",
            "CREATE TABLE IF NOT EXISTS LT1(ID INT)",
            "CREATE TABLE AS1(X INT NOT NULL, Y VARCHAR(5) DEFAULT 'd')",
            "INSERT INTO AS1(X) VALUES(1)",
            "SELECT X, Y FROM AS1",
            "CREATE TABLE AS2(X INT) AS SELECT 1 WITH NO DATA",
            "CREATE TABLE AS3 AS SELECT 1 AS X, 2 AS Y WITH DATA",
            "SELECT X, Y FROM AS3",
            "CREATE TABLE CM1(ID INT CONSTRAINT PK_CM1 PRIMARY KEY)",
            "CREATE TABLE CM2(ID INT, CONSTRAINT UQ_CM2 UNIQUE(ID))",
            "CREATE TABLE CM3(ID INT, CONSTRAINT CK_CM3 CHECK (ID > 0))",
            "INSERT INTO CM3 VALUES(1)",
            "INSERT INTO CM3 VALUES(-1)",
            "DROP TABLE LT1, GT1, TP1, TC1, AS1, AS2, AS3, CM1, CM2, CM3");
    }

    [JavaOracleFact]
    public void LimitOffsetForms()
    {
        AssertSameAsReference("parser_more2",
            "CREATE TABLE P(ID INT)",
            "INSERT INTO P VALUES(1),(2),(3),(4),(5),(6)",
            "SELECT ID FROM P ORDER BY ID LIMIT 2",
            "SELECT ID FROM P ORDER BY ID LIMIT 2 OFFSET 1",
            "SELECT ID FROM P ORDER BY ID OFFSET 2 ROWS",
            "SELECT ID FROM P ORDER BY ID FETCH FIRST ROW ONLY",
            "SELECT ID FROM P ORDER BY ID FETCH FIRST 3 ROWS WITH TIES");
    }

    [JavaOracleFact]
    public void IntervalQualifiers()
    {
        AssertSameAsReference("parser_more3",
            "SELECT EXTRACT(YEAR FROM INTERVAL '2' YEAR)",
            "SELECT EXTRACT(MONTH FROM INTERVAL '3' MONTH)",
            "SELECT EXTRACT(DAY FROM INTERVAL '4' DAY)",
            "SELECT EXTRACT(HOUR FROM INTERVAL '5' HOUR)",
            "SELECT EXTRACT(MINUTE FROM INTERVAL '6' MINUTE)",
            "SELECT EXTRACT(SECOND FROM INTERVAL '7' SECOND)",
            "SELECT EXTRACT(YEAR FROM INTERVAL '1-2' YEAR TO MONTH)",
            "SELECT EXTRACT(DAY FROM INTERVAL '1 2' DAY TO HOUR)",
            "SELECT EXTRACT(DAY FROM INTERVAL '1 2:30' DAY TO MINUTE)",
            "SELECT EXTRACT(DAY FROM INTERVAL '1 2:30:40' DAY TO SECOND)",
            "SELECT EXTRACT(HOUR FROM INTERVAL '1:20' HOUR TO MINUTE)",
            "SELECT EXTRACT(HOUR FROM INTERVAL '1:20:30' HOUR TO SECOND)",
            "SELECT EXTRACT(MINUTE FROM INTERVAL '1:20' MINUTE TO SECOND)");
    }

    [JavaOracleFact]
    public void NestedExpressions()
    {
        AssertSameAsReference("parser_more4",
            "SELECT ((1 + 2) * 3) - (4 / 2)",
            "SELECT (1 + (2 * (3 - (4 / 2))))",
            "SELECT SUM(DISTINCT (VAL)) FROM (SELECT 1 AS VAL UNION ALL SELECT 1 UNION ALL SELECT 2)",
            "SELECT COUNT(DISTINCT (GRP)) FROM (SELECT 'a' AS GRP UNION ALL SELECT 'a' UNION ALL SELECT 'b')",
            "SELECT CASE WHEN ((1 < 2) AND (3 > 2)) THEN 1 ELSE 0 END",
            "SELECT (SELECT (SELECT 42))");
    }

    [JavaOracleFact]
    public void EnumAndArray()
    {
        AssertSameAsReference("parser_more5",
            "CREATE TABLE E(C ENUM('a','b'))",
            "INSERT INTO E VALUES('a')",
            "SELECT CAST(C AS VARCHAR) FROM E",
            "INSERT INTO E VALUES('c')",
            "SELECT CARDINALITY(ARRAY[1,2,3])",
            "SELECT CARDINALITY(ARRAY[1,2] || ARRAY[3])",
            "SELECT ARRAY[1,2][1]",
            "DROP TABLE E");
    }
}
