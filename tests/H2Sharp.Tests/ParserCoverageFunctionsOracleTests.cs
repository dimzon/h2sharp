/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Parser coverage: built-in functions and error codes — run through the ADO.NET layer
 * and compared against the reference jar.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserCoverageFunctionsOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void Aggregates()
    {
        AssertSameAsReference("parser_fn1",
            "CREATE TABLE P(ID INT, GRP VARCHAR(10), VAL INT)",
            "INSERT INTO P VALUES(1,'a',10),(2,'a',NULL),(3,'b',30),(4,'b',40)",
            "SELECT COUNT(*), COUNT(VAL), SUM(VAL), AVG(VAL), MIN(VAL), MAX(VAL) FROM P",
            "SELECT GRP, CAST(STDDEV_POP(VAL) AS NUMERIC(30,10)),"
            + " CAST(VAR_POP(VAL) AS NUMERIC(30,10)) FROM P GROUP BY GRP ORDER BY GRP",
            "SELECT GRP, GROUP_CONCAT(VAL ORDER BY VAL) FROM P GROUP BY GRP ORDER BY GRP",
            "SELECT COUNT(DISTINCT GRP) FROM P");
    }

    [JavaOracleFact]
    public void StringAndNumeric()
    {
        AssertSameAsReference("parser_fn2",
            "SELECT UPPER('abc'), LOWER('ABC'), LENGTH('abc'), LENGTH(NULL)",
            "SELECT TRIM('  x  '), LTRIM('  x'), RTRIM('x  ')",
            "SELECT SUBSTRING('abcdef', 2, 3), SUBSTR('abcdef', 2, 3)",
            "SELECT REPLACE('aaa', 'a', 'b'), CONCAT('a', 'b')",
            "SELECT LEFT('abcdef', 2), RIGHT('abcdef', 2)",
            "SELECT LPAD('7', 3, '0'), RPAD('7', 3, '0')",
            "SELECT POSITION('bc' IN 'abcdef'), ASCII('A'), CHAR(65)",
            "SELECT ABS(-5), CEILING(1.2), FLOOR(1.8), ROUND(1.234, 2), MOD(10, 3)",
            "SELECT CAST(POWER(2, 10) AS BIGINT), CAST(SQRT(16) AS BIGINT), EXP(1),"
            + " CAST(LOG10(1000) AS BIGINT), SIGN(-3), SIGN(0)",
            "SELECT LEAST(3, 1, 2), GREATEST(3, 1, 2), NULLIF(1, 1), COALESCE(NULL, 5)");
    }

    [JavaOracleFact]
    public void DateTimeFunctions()
    {
        AssertSameAsReference("parser_fn3",
            "SELECT EXTRACT(YEAR FROM DATE '2020-05-06'), EXTRACT(MONTH FROM DATE '2020-05-06'),"
            + " EXTRACT(DAY FROM DATE '2020-05-06')",
            "SELECT EXTRACT(HOUR FROM TIME '12:34:56'), EXTRACT(MINUTE FROM TIME '12:34:56'),"
            + " EXTRACT(SECOND FROM TIME '12:34:56')",
            "SELECT DATEDIFF(DAY, DATE '2020-01-01', DATE '2020-01-11')",
            "SELECT CAST(DATEADD(MONTH, 2, DATE '2020-01-01') AS VARCHAR)",
            "SELECT DAY_OF_WEEK(DATE '2020-05-06'), DAY_OF_YEAR(DATE '2020-05-06')",
            "SELECT EXTRACT(YEAR FROM TIMESTAMP '2020-05-06 12:34:56')");
    }

    [JavaOracleFact]
    public void Errors()
    {
        AssertSameAsReference("parser_err",
            "SELECT * FROM MISSING_TABLE",
            "CREATE TABLE T(ID INT PRIMARY KEY)",
            "INSERT INTO T VALUES(1)",
            "INSERT INTO T VALUES(1)",
            "SELECT 1 / 0",
            "SELECT FROM",
            "SELECT NO_SUCH_FUNCTION(1)",
            "SELECT 'abc' + 1",
            "CREATE TABLE T(X INT)",
            "DROP TABLE MISSING_TABLE");
    }
}
