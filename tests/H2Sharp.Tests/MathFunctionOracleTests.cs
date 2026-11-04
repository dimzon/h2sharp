/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The rounding family as the reference numeric scripts exercise it: ROUND with and without a
 * scale on integers, NUMERIC, DECFLOAT, REAL and DOUBLE, the negative-scale integer and
 * NUMERIC paths, NULL scales, round-to-even at the double limits, ROUNDMAGIC's trimming,
 * TRUNC/FLOOR/CEIL, the date forms of TRUNCATE, MOD and ABS, the derived column types, and the
 * refusals.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class MathFunctionOracleTests
{
    [JavaOracleFact]
    public void TheRoundingFunctionsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            // The reference's functions/numeric/round.sql.
            "SELECT ROUND(-1.2), ROUND(-1.5), ROUND(-1.6), ROUND(2), ROUND(1.5), ROUND(1.8), ROUND(1.1)",
            "SELECT ROUND(NULL, NULL), ROUND(10.49, 0), ROUND(10.05, 1)",
            "SELECT ROUND(NULL), ROUND(0.6, NULL), ROUND(1.05), ROUND(-1.51)",
            "SELECT ROUND(998.5::DOUBLE), ROUND(998.5::REAL)",
            "SELECT ROUND(4503599627370495.0::DOUBLE), ROUND(4503599627370495.5::DOUBLE), "
                + "ROUND(4503599627370496.0::DOUBLE), ROUND(4503599627370497.0::DOUBLE)",
            "SELECT ROUND(450359962737049.50::DOUBLE, 1), ROUND(450359962737049.55::DOUBLE, 1), "
                + "ROUND(450359962737049.60::DOUBLE, 1), ROUND(450359962737049.70::DOUBLE, 1)",
            "SELECT ROUND(0.285, 2), ROUND(0.285::DOUBLE, 2), ROUND(0.285::REAL, 2)",
            "SELECT ROUND(1.285, 2), ROUND(1.285::DOUBLE, 2), ROUND(1.285::REAL, 2)",
            "SELECT ROUND(1, 1) IS OF (INTEGER), ROUND(1::DOUBLE, 1) IS OF (DOUBLE), "
                + "ROUND(1::REAL, 1) IS OF (REAL)",
            "SELECT ROUND(1.0::DECFLOAT, 1) IS OF (DECFLOAT), ROUND(1.25::DECFLOAT, 1)",
            // Integer arguments: a non-negative scale returns the argument, a negative one
            // shifts the value.
            "SELECT ROUND(12345, 0), ROUND(12345, 2), ROUND(12345, -1), ROUND(12345, -2), "
                + "ROUND(5, -1), ROUND(-5, -1), ROUND(149, -2), ROUND(-149, -2)",
            // NUMERIC: a scale at or above the declared one returns the argument; below it
            // the value is rounded, and a negative scale is clamped to zero.
            "SELECT ROUND(123.456, 3), ROUND(123.456, 4), ROUND(123.456, 1), ROUND(123.456, 0), "
                + "ROUND(123.456, -1), ROUND(123.456, -2), ROUND(-123.456, -1)",
            // REAL and DOUBLE: the zero-scale path rounds in double precision, any other
            // scale through a decimal.
            "SELECT ROUND(123.456::DOUBLE, 0), ROUND(123.456::REAL, 0), "
                + "ROUND(123.456::DOUBLE, 1), ROUND(123.456::REAL, 1), "
                + "ROUND(123.456::DOUBLE, -1), ROUND(-123.456::DOUBLE, -1)",
            // A scale that is neither constant nor known.
            "SELECT ROUND(V, S) FROM (VALUES (1.2345, 2), (1.2345, 0)) T(V, S)",
            "SELECT ROUND(CAST(V AS NUMERIC(10, 4)), S) FROM (VALUES (1.2345, 2)) T(V, S)",
            // The INTEGER / NUMERIC / DECFLOAT / floating-point bodies of FLOOR and CEIL, whose
            // only SQL shape is the one-argument form.
            "SELECT FLOOR(NULL), FLOOR(1), FLOOR(1.1), FLOOR(-1.1), FLOOR(1.9), FLOOR(-1.9)",
            "SELECT CEIL(NULL), CEIL(1), CEIL(1.1), CEIL(-1.1), CEIL(1.9), CEIL(-1.9)",
            "SELECT FLOOR(12345), CEIL(12345), FLOOR(CAST(12345 AS BIGINT)), CEIL(CAST(12345 AS BIGINT))",
            "SELECT FLOOR(1.5) IS OF (NUMERIC), FLOOR(-1.5), CEIL(1.5), CEIL(-1.5)",
            "SELECT FLOOR(CAST(123.456 AS NUMERIC(10, 4))), CEIL(CAST(123.456 AS NUMERIC(10, 4)))",
            "SELECT FLOOR(CAST(123.456 AS NUMERIC(10, 0))), CEIL(CAST(123.456 AS NUMERIC(10, 0)))",
            "SELECT FLOOR(1.9::DOUBLE), FLOOR(-1.9::DOUBLE), FLOOR(1.9::REAL), FLOOR(-1.9::REAL), "
                + "CEIL(1.1::DOUBLE), CEIL(-1.1::DOUBLE), CEIL(1.1::REAL), CEIL(-1.1::REAL)",
            "SELECT FLOOR(1.5::DOUBLE) IS OF (DOUBLE), FLOOR(1.5::REAL) IS OF (REAL), "
                + "CEIL(1.5::DOUBLE) IS OF (DOUBLE), CEIL(1.5::REAL) IS OF (REAL)",
            "SELECT FLOOR(1.25::DECFLOAT), CEIL(1.25::DECFLOAT), FLOOR(-1.25::DECFLOAT), "
                + "CEIL(-1.25::DECFLOAT)",
            "SELECT FLOOR(1.25::DECFLOAT) IS OF (DECFLOAT), CEIL(1.25::DECFLOAT) IS OF (DECFLOAT)",
            // TRUNC: the numeric forms and the two-argument shape.
            "SELECT TRUNCATE(1.234, 2), TRUNCATE(1.3), TRUNC(1.99, 0), TRUNCATE(1.99, 1), "
                + "TRUNCATE(-1.99, 0), TRUNCATE(-1.99, 1)",
            "SELECT TRUNCATE(1.99::DOUBLE, 0), TRUNCATE(1.99::DOUBLE, 1), "
                + "TRUNCATE(-1.99::DOUBLE, 0), TRUNCATE(-1.99::DOUBLE, 1)",
            "SELECT TRUNCATE(1.99::REAL, 0), TRUNCATE(1.99::REAL, 1), "
                + "TRUNCATE(-1.99::REAL, 0), TRUNCATE(-1.99::REAL, 1)",
            "SELECT TRUNCATE(12345, 0), TRUNCATE(12345, -1), TRUNCATE(123.456, -1)",
            "SELECT TRUNCATE(NULL, NULL), TRUNCATE(1.99, 0), TRUNCATE(-10.9, 0), TRUNC(NULL, NULL)",
            "SELECT TRUNCATE(1.3) IS OF (NUMERIC), TRUNCATE(CAST(1.3 AS DOUBLE)) IS OF (DOUBLE), "
                + "TRUNCATE(CAST(1.3 AS REAL)) IS OF (REAL)",
            "SELECT TRUNCATE(V, S) FROM (VALUES (1.111, 1)) T(V, S)",
            // The date forms.
            "SELECT TRUNCATE(DATE '2011-03-05')",
            "SELECT TRUNCATE(TIMESTAMP '2011-03-05 02:03:04')",
            "SELECT TRUNCATE(TIMESTAMP WITH TIME ZONE '2011-03-05 02:03:04+07')",
            "SELECT TRUNCATE('2011-03-05 02:03:04')",
            "SELECT TRUNCATE(CURRENT_DATE, 1)",
            "SELECT TRUNCATE(LOCALTIMESTAMP, 1)",
            "SELECT TRUNCATE(CURRENT_TIMESTAMP, 1)",
            "SELECT TRUNCATE('2011-03-05 02:03:04', 1)",
            "SELECT TRUNCATE('bad')",
            "SELECT TRUNCATE(1, 2, 3)",
        ]);
    }

    [JavaOracleFact]
    public void RoundMagicAndTheOtherScalarsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            // The reference's roundmagic.sql.
            "SELECT ROUNDMAGIC(NULL), ROUNDMAGIC(CAST(3.11 AS DOUBLE) - 3.1), "
                + "ROUNDMAGIC(3.11 - 3.1 - 0.01), ROUNDMAGIC(2000000000000)",
            "SELECT ROUNDMAGIC(0.0), ROUNDMAGIC(1.4), ROUNDMAGIC(-1.4), ROUNDMAGIC(1.234567890123456)",
            "SELECT ROUNDMAGIC(1.0000000000000002), ROUNDMAGIC(3.0999999999999996), "
                + "ROUNDMAGIC(123456789.12345678), ROUNDMAGIC(1E-20)",
            // ABS, SIGN, MOD and the NULL/type folding of each.
            "SELECT ABS(NULL), ABS(-1), ABS(1), ABS(0), ABS(-0.1), ABS(0.1)",
            "SELECT ABS(CAST(-1 AS BIGINT)), ABS(CAST(-1 AS SMALLINT)), ABS(CAST(-1 AS TINYINT)), "
                + "ABS(CAST(-1 AS REAL)), ABS(CAST(-1 AS DOUBLE)), ABS(CAST(-1 AS NUMERIC(3, 1)))",
            "SELECT SIGN(NULL), SIGN(-10), SIGN(-0.5), SIGN(0), SIGN(0.5), SIGN(10)",
            "SELECT SIGN(CAST(-5 AS BIGINT)), SIGN(CAST(-5 AS REAL)), SIGN(CAST(-5 AS NUMERIC(3, 1)))",
            "SELECT MOD(NULL, 1), MOD(1, NULL), MOD(NULL, NULL), MOD(10, 2)",
            "SELECT MOD(10, 3), MOD(-10, 3), MOD(10, -3), MOD(-10, -3), MOD(10.5, 3)",
            "SELECT MOD(10, 0)",
            "SELECT MOD('a', 1)",
            "SELECT MOD(1, 'a')",
            "SELECT MOD('a', 'b')",
            "SELECT MOD(DATE '2020-01-01', 1)",
            "SELECT MOD(1, DATE '2020-01-01')",
            // FLOOR / CEIL of a non-numeric scalar.
            "SELECT FLOOR('a')",
            "SELECT CEIL('a')",
            // The derived column types, from the reference's floor/ceil scripts.
            "CREATE TABLE S(N NUMERIC(5, 2))",
            "CREATE TABLE TF AS SELECT FLOOR(N) F FROM S",
            "CREATE TABLE TC AS SELECT CEIL(N) C FROM S",
            "SELECT DATA_TYPE, NUMERIC_PRECISION, NUMERIC_SCALE FROM INFORMATION_SCHEMA.COLUMNS "
                + "WHERE TABLE_NAME IN ('TF', 'TC') ORDER BY TABLE_NAME",
            "DROP TABLE S, TF, TC",
            // A scale outside the NUMERIC limits is refused.
            "SELECT ROUND(1.23, 1000001)",
            "SELECT TRUNC(1.23, -1000001)",
        ]);
    }

    [JavaOracleFact]
    public void TheMathPlansMatchJdbc()
    {
        ExplainOracleTests.Check([
            "EXPLAIN SELECT ROUND(1.25, 1)",
            "EXPLAIN SELECT ROUND(NULL, 1)",
            "EXPLAIN SELECT ROUND(1.25, NULL)",
            "EXPLAIN SELECT TRUNC('2020-01-02 03:04:05')",
            "EXPLAIN SELECT TRUNC(DATE '2020-01-02')",
            "EXPLAIN SELECT FLOOR(CAST(1.5 AS NUMERIC(5, 2)))",
            "EXPLAIN SELECT MOD(10, 3)",
            "EXPLAIN SELECT ABS(NULL)",
        ]);
    }
}
