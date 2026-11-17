/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 239: percentile interpolation and numeric precision edges of binary operations.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class NumericEdgeOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void PercentileInterpolatesEveryNumericFamily()
    {
        AssertSameAsReference("numeric_percentile",
            "CREATE TABLE T(I INT, B BIGINT, D DOUBLE, N NUMERIC(20,4))",
            "INSERT INTO T VALUES (1, 1, 1.0, 1.0), (2, 2, 2.0, 2.0)",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY I) FROM T",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY B) FROM T",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY D) FROM T",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY N) FROM T",
            "SELECT PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY I) FROM T",
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY I) FILTER (WHERE I > 2) FROM T");
    }

    [JavaOracleFact]
    public void TwoArgumentMathFunctionsMatchTheReference()
    {
        AssertSameAsReference("numeric_math2",
            "SELECT LOG(2, 8) = 3",
            "SELECT LOG(10, 100) = 2",
            "SELECT ROUND(LOG(3, 2), 14)",
            "SELECT LOG(1, 10)",
            "SELECT LOG(0, 10)",
            "SELECT POWER(2, 3) = 8",
            "SELECT ROUND(ATAN2(1, 1), 14)");
    }

    [JavaOracleFact]
    public void NumericArithmeticAdjustsPrecisionAndScale()
    {
        AssertSameAsReference("numeric_arithmetic",
            "SELECT CAST(1 AS NUMERIC(38,10)) + CAST(2 AS NUMERIC(38,10))",
            "SELECT CAST(1 AS NUMERIC(38,0)) * CAST(2 AS NUMERIC(38,0))",
            "SELECT CAST(1.5 AS DECIMAL(10,5)) / CAST(2 AS DECIMAL(10,5))",
            "SELECT CAST(1 AS NUMERIC(38,20)) + CAST(2 AS NUMERIC(38,20))",
            "SELECT CAST(1 AS NUMERIC(38,10)) + NULL");
    }
}
