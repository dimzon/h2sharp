/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The conversion pairs the value-for-type matrix did not reach: exotic sources into the
 * string, binary and temporal families, the padding and truncation roads of CHAR, the
 * decimal floating-point edges, and the assignment-mode length refusals.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ConversionEdgesOracleTests
{
    [JavaOracleFact]
    public void CastPairsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            // Sources with no straight text/binary form into the string families.
            "SELECT CAST(ARRAY[1] AS CHAR(5))",
            "SELECT CAST(ROW(1) AS CHAR(5))",
            "SELECT CAST(ARRAY[1] AS VARCHAR(5))",
            "SELECT CAST(ARRAY[1] AS VARCHAR_IGNORECASE(5))",
            "SELECT CAST(ARRAY[1] AS BINARY(2))",
            "SELECT CAST(ARRAY[1] AS CLOB)",
            // The padding and truncation roads of CHAR and VARCHAR.
            "SELECT CAST(CAST('ab' AS CHAR(5)) AS CHAR(5))",
            "SELECT CAST('abcdefgh' AS CHAR(3))",
            "SELECT CAST(CAST('ab   ' AS CHAR(5)) AS CHAR(2))",
            // The narrow-integer roads from DOUBLE, and a SMALLINT through its own type.
            "SELECT CAST(CAST(1.5 AS DOUBLE) AS TINYINT)",
            "SELECT CAST(CAST(300.5 AS DOUBLE) AS TINYINT)",
            "SELECT CAST(CAST(1.5 AS DOUBLE) AS SMALLINT)",
            "SELECT CAST(CAST(1 AS SMALLINT) AS SMALLINT)",
            // Binary sources into the integer families.
            "SELECT CAST(X'0000002A' AS INTEGER)",
            "SELECT CAST(X'000000000000002A' AS BIGINT)",
            "SELECT CAST(X'2A' AS SMALLINT)",
            "SELECT CAST(X'2A' AS TINYINT)",
            // Decimal floating point from REAL, DOUBLE and text.
            "SELECT CAST(CAST(1.5 AS REAL) AS DECFLOAT)",
            "SELECT CAST(CAST(1E300 AS DOUBLE) AS DECFLOAT)",
            "SELECT CAST('abc' AS DECFLOAT)",
            "SELECT CAST(CAST(1 AS REAL) AS REAL)",
            "SELECT CAST(CAST(1E300 AS DOUBLE) AS BIGINT)",
            "SELECT CAST(CAST(1E300 AS REAL) AS INTEGER)",
            // Temporal conversions across the date/time families.
            "SELECT CAST('2024-03-05 12:00:00' AS DATE)",
            "SELECT CAST(CAST('12:34:56' AS TIME(3)) AS TIME(3))",
            "SELECT CAST(TIMESTAMP '2024-03-05 12:34:56' AS TIME(3) WITH TIME ZONE)",
            "SELECT CAST('2024-03-05 24:00:00' AS TIMESTAMP(3))",
            "SELECT CAST(TIME(3) WITH TIME ZONE '12:34:56+02:00' AS TIMESTAMP(3) WITH TIME ZONE)",
            "SELECT CAST('2024-03-05 24:00:00+02:00' AS TIMESTAMP(3) WITH TIME ZONE)",
            // Rows and arrays between their shaped forms.
            "SELECT CAST(ROW(1, 2) AS ROW(A INT, B INT))",
            "SELECT CAST(ROW(1) AS ROW(A INT, B INT))",
            "SELECT CAST(X'414243' AS VARCHAR ARRAY)",
            "SELECT CAST('abc' AS CHAR ARRAY)",
            "SELECT CAST(ARRAY[1, 2] AS INTEGER ARRAY)",
        ]);
    }

    [JavaOracleFact]
    public void AssignmentLengthRefusalsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE CE(ID INT GENERATED ALWAYS AS IDENTITY, E ENUM('a', 'b'), "
                + "C5 CHAR(5), VCI VARCHAR_IGNORECASE(5), VB VARBINARY(2), N NUMERIC(5, 2), "
                + "BL BLOB, CL CLOB, C3 CHAR(3))",
            "INSERT INTO CE(E) VALUES ('a')",
            "INSERT INTO CE(E) VALUES (1)",
            "INSERT INTO CE(E) VALUES (2)",
            "INSERT INTO CE(E) VALUES ('c')",
            "INSERT INTO CE(E) VALUES (3)",
            "INSERT INTO CE(C5) VALUES ('abcdefgh')",
            "INSERT INTO CE(VCI) VALUES ('abcdefgh')",
            "INSERT INTO CE(VB) VALUES (X'01020304')",
            "INSERT INTO CE(N) VALUES (12345.6)",
            "INSERT INTO CE(BL) VALUES (X'0102')",
            "INSERT INTO CE(CL) VALUES (X'414243')",
            "INSERT INTO CE(C3) VALUES ('abcdefgh')",
            "UPDATE CE SET VB = X'01020304' WHERE ID = 1",
            "UPDATE CE SET C5 = 'abcdefgh' WHERE ID = 1",
            "UPDATE CE SET VCI = 'abcdefgh' WHERE ID = 1",
            "UPDATE CE SET N = 1234.5 WHERE ID = 1",
            "UPDATE CE SET BL = X'01020304' WHERE ID = 1",
            "UPDATE CE SET CL = X'414243' WHERE ID = 1",
            "UPDATE CE SET C3 = 'abcdefgh' WHERE ID = 1",
            "SELECT ID, E, C5, VCI, VB, N, BL, CL, C3 FROM CE ORDER BY ID",
        ]);
    }
}
