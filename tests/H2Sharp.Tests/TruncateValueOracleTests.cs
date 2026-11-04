/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * TRUNCATE_VALUE: the precision path for types that carry one (strings, numeric, decfloat,
 * binary, arrays, intervals), the forced path for integers and floating point, NULL, a
 * column argument, the refusals, and the plans.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class TruncateValueOracleTests
{
    [JavaOracleFact]
    public void TruncateValueMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            // The reference's functions/system/truncate-value.sql cases, through SELECT so the
            // shared oracle compares the rows instead of the CALL update count.
            "SELECT TRUNCATE_VALUE('Test 123', 4, FALSE)",
            "SELECT TRUNCATE_VALUE(1234567890.123456789, 4, FALSE)",
            "SELECT TRUNCATE_VALUE(1234567890.123456789, 4, TRUE)",
            "SELECT TRUNCATE_VALUE(CAST(1234567890.123456789 AS DOUBLE PRECISION), 4, FALSE)",
            "SELECT TRUNCATE_VALUE(CAST(1234567890.123456789 AS DOUBLE PRECISION), 4, TRUE)",
            // Numeric keeps its type; a precision wider than the value changes nothing.
            "SELECT TRUNCATE_VALUE(CAST(12345.6789 AS NUMERIC(10, 4)), 3, FALSE)",
            "SELECT TRUNCATE_VALUE(CAST(12345.6789 AS NUMERIC(10, 4)), 10, FALSE)",
            "SELECT TRUNCATE_VALUE(CAST(0.00012345 AS NUMERIC(10, 8)), 2, FALSE)",
            "SELECT TRUNCATE_VALUE(CAST(-12345.6789 AS NUMERIC(10, 4)), 3, FALSE)",
            "SELECT TRUNCATE_VALUE(CAST(1234567890.123456789 AS DECFLOAT), 4, FALSE)",
            "SELECT TRUNCATE_VALUE(CAST(1234567890.123456789 AS DECFLOAT), 12, FALSE)",
            // Strings, binary and arrays read the precision off the data type.
            "SELECT TRUNCATE_VALUE(CAST('abcdef' AS VARCHAR(10)), 3, FALSE)",
            "SELECT TRUNCATE_VALUE(CAST('abcdef' AS CHAR(10)), 3, FALSE)",
            "SELECT TRUNCATE_VALUE(CAST('abcdef' AS VARCHAR(10)), 20, FALSE)",
            "SELECT TRUNCATE_VALUE(X'01020304', 2, FALSE)",
            "SELECT TRUNCATE_VALUE(ARRAY[1, 2, 3], 2, FALSE)",
            // The forced path converts integers and floating point to the precision.
            "SELECT TRUNCATE_VALUE(CAST(12345 AS INT), 2, FALSE)",
            "SELECT TRUNCATE_VALUE(CAST(12345 AS INT), 2, TRUE)",
            "SELECT TRUNCATE_VALUE(CAST(12345 AS BIGINT), 2, TRUE)",
            "SELECT TRUNCATE_VALUE(CAST(12345 AS SMALLINT), 2, TRUE)",
            "SELECT TRUNCATE_VALUE(CAST(12345 AS TINYINT), 2, TRUE)",
            "SELECT TRUNCATE_VALUE(CAST(1234.5678 AS REAL), 2, TRUE)",
            "SELECT TRUNCATE_VALUE(CAST(-12345 AS INT), 2, TRUE)",
            // NULL and a column argument, so the runtime path and the typed NULL run too.
            "SELECT TRUNCATE_VALUE(NULL, 3, TRUE)",
            "SELECT TRUNCATE_VALUE(NULL, 3, FALSE)",
            // The forced path returns types it cannot express unchanged; zero has no digits to
            // round off.
            "SELECT TRUNCATE_VALUE(TRUE, 3, TRUE)",
            "SELECT TRUNCATE_VALUE(DATE '2020-01-01', 3, TRUE)",
            "SELECT TRUNCATE_VALUE(CAST(0 AS NUMERIC(10, 4)), 1, FALSE)",
            "SELECT TRUNCATE_VALUE(CAST(0 AS DECFLOAT), 1, FALSE)",
            "SELECT TRUNCATE_VALUE(CAST(0.0000 AS DOUBLE), 1, TRUE)",
            "CREATE TABLE TT(ID INT, V VARCHAR(10), N NUMERIC(10, 4))",
            "INSERT INTO TT VALUES (1, 'abcdef', 12345.6789), (2, NULL, NULL)",
            "SELECT ID, TRUNCATE_VALUE(V, 3, FALSE) FROM TT ORDER BY ID",
            "SELECT ID, TRUNCATE_VALUE(N, 3, FALSE) FROM TT ORDER BY ID",
            "SELECT ID, TRUNCATE_VALUE(ID, 2, TRUE) FROM TT ORDER BY ID",
            "SELECT TRUNCATE_VALUE(V, 3, FALSE) FROM TT WHERE ID = 2",
            // Refusals.
            "SELECT TRUNCATE_VALUE(1, 0, TRUE)",
            "SELECT TRUNCATE_VALUE('x', -1, FALSE)",
            "SELECT TRUNCATE_VALUE('abcdef', 3, FALSE)",
            "SELECT TRUNCATE_VALUE(X'01020304', 1, FALSE)",
        ]);
    }

    [JavaOracleFact]
    public void TruncateValuePlansMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE TT(ID INT, V VARCHAR(10), N NUMERIC(10, 4))",
            "EXPLAIN SELECT TRUNCATE_VALUE('Test 123', 4, FALSE)",
            "EXPLAIN SELECT TRUNCATE_VALUE(NULL, 3, TRUE)",
            "EXPLAIN SELECT TRUNCATE_VALUE(V, 3, FALSE) FROM TT",
            "EXPLAIN SELECT TRUNCATE_VALUE(N, 3, FALSE) FROM TT",
        ]);
    }
}
