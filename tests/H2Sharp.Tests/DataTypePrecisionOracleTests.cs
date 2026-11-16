/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 228: the reference's datatypes/numeric.sql uses TRUNCATE_LARGE_LENGTH to clamp an
 * over-wide NUMERIC precision instead of refusing it.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DataTypePrecisionOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void TruncateLargeLengthClampsNumericPrecision()
    {
        AssertSameAsReference("numeric_truncate",
            "CREATE TABLE T(A NUMERIC(100001))",
            "SET TRUNCATE_LARGE_LENGTH TRUE",
            "CREATE TABLE T(A NUMERIC(100001))",
            "SELECT DATA_TYPE, NUMERIC_PRECISION, DECLARED_NUMERIC_PRECISION "
                + "FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'T'",
            "INSERT INTO T VALUES (1)",
            "SELECT * FROM T",
            "SET TRUNCATE_LARGE_LENGTH FALSE",
            "CREATE TABLE T2(A NUMERIC(100001))");
    }
}
