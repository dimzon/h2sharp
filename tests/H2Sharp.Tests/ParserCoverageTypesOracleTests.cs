/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Parser coverage: data types, literals, casts — run through the ADO.NET layer and
 * compared against the reference jar. Rendered answers are normalized (CASE, CAST to
 * VARCHAR, EXTRACT) so the two sides print the same text.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ParserCoverageTypesOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void DataTypesRoundTrip()
    {
        AssertSameAsReference("parser_types1",
            "CREATE TABLE TYPES(ID INT, C CHAR(5), V VARCHAR(10),"
            + " VC VARCHAR_IGNORECASE(10), B BOOLEAN, T TINYINT, S SMALLINT, I INT,"
            + " BI BIGINT, N NUMERIC(10,2), DC DECFLOAT, R REAL, D DOUBLE,"
            + " DT DATE, TM TIME, TS TIMESTAMP, BIN VARBINARY(16), U UUID, J JSON)",
            "INSERT INTO TYPES VALUES(1, 'a', 'x', 'y', TRUE, 1, 2, 3, 9000000000,"
            + " 1.50, 1.25, 1.5, 2.5, DATE '2020-01-02', TIME '12:34:56',"
            + " TIMESTAMP '2020-01-02 12:34:56', X'00FF',"
            + " '123e4567-e89b-12d3-a456-426614174000', '{\"a\":1}')",
            "SELECT ID, C, V, VC, T, S, I, BI, N, DC, R, D FROM TYPES",
            "SELECT ID, CASE WHEN B THEN 1 ELSE 0 END FROM TYPES",
            "SELECT ID, CAST(DT AS VARCHAR), CAST(TM AS VARCHAR), CAST(TS AS VARCHAR) FROM TYPES",
            "SELECT ID, LENGTH(BIN), CAST(U AS VARCHAR), CAST(J AS VARCHAR) FROM TYPES");
    }

    [JavaOracleFact]
    public void Literals()
    {
        AssertSameAsReference("parser_literals",
            "SELECT 1 + 2",
            "SELECT 1.5 * 2",
            "SELECT -7",
            "SELECT CASE WHEN 1e3 = 1000 THEN 1 ELSE 0 END",
            "SELECT 0x1F",
            "SELECT B'101'",
            "SELECT 'hello'",
            "SELECT 'It''s'",
            "SELECT N'unicode'",
            "SELECT CASE WHEN TRUE AND FALSE THEN 1 ELSE 0 END",
            "SELECT CASE WHEN NOT TRUE THEN 1 ELSE 0 END",
            "SELECT CASE WHEN NULL IS NULL THEN 1 ELSE 0 END",
            "SELECT CASE WHEN X'00FF' = X'00FF' THEN 1 ELSE 0 END",
            "SELECT CAST(DATE '2020-01-02' AS VARCHAR)",
            "SELECT CAST(TIME '01:02:03' AS VARCHAR)",
            "SELECT CAST(TIMESTAMP '2020-01-02 01:02:03.123' AS VARCHAR)",
            "SELECT EXTRACT(DAY FROM INTERVAL '1' DAY)",
            "SELECT EXTRACT(YEAR FROM INTERVAL '1-2' YEAR TO MONTH)",
            "SELECT CASE WHEN CURRENT_DATE = CURRENT_DATE THEN 1 ELSE 0 END",
            "SELECT CASE WHEN CURRENT_TIME = CURRENT_TIME THEN 1 ELSE 0 END",
            "SELECT CASE WHEN TIMESTAMP '2020-01-02 01:02:03' = TIMESTAMP '2020-01-02 01:02:03' THEN 1 ELSE 0 END");
    }

    [JavaOracleFact]
    public void Casts()
    {
        AssertSameAsReference("parser_casts",
            "SELECT CAST('12' AS INT)",
            "SELECT CAST(1.9 AS INT)",
            "SELECT CAST(TRUE AS VARCHAR)",
            "SELECT CAST(123 AS VARCHAR)",
            "SELECT CAST(1.5 AS NUMERIC(4,1))",
            "SELECT CAST('a' AS CHAR(3))",
            "SELECT CAST(1 AS BIGINT)",
            "SELECT CAST(CAST('2020-01-02' AS DATE) AS VARCHAR)",
            "SELECT CAST(CAST('2020-01-02 01:02:03' AS TIMESTAMP) AS VARCHAR)",
            "SELECT CAST(INT '5' AS BIGINT)",
            "SELECT EXTRACT(HOUR FROM INTERVAL '2' HOUR)",
            "SELECT CONVERT(VARCHAR, 42)",
            "SELECT CASE WHEN TRY_CAST('abc' AS INT) IS NULL THEN 1 ELSE 0 END",
            "SELECT CASE WHEN CAST(NULL AS INT) IS NULL THEN 1 ELSE 0 END");
    }
}
