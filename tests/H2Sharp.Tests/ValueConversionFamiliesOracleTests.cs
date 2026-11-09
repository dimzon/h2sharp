/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 89: the conversion families of Value.Conversion.Families — the assignment
 * differences of CHAR/VARCHAR (padding, trimming, too-long), the BINARY padding and
 * truncation, BLOB/CLOB and JSON precision checks, ENUM labels, JAVA_OBJECT from
 * binary and the DECFLOAT infinities.
 */
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ValueConversionFamiliesOracleTests
{
    /// <summary>
    /// CHAR pads and VARCHAR refuses: an assignment may lose padding but nothing else.
    /// The mode decides what padding means, so the never-padding branch lives in a
    /// database opened with MODE=MySQL (the port fixes the mode when it opens, D-029).
    /// </summary>
    [JavaOracleFact]
    public void CharAssignmentAndPaddingModesMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE CH(C CHAR(2), V VARCHAR(2), I VARCHAR_IGNORECASE(2))",
            "INSERT INTO CH(C) VALUES ('ab  ')",
            "INSERT INTO CH(C) VALUES ('abc')",
            "INSERT INTO CH(V) VALUES ('abc')",
            "INSERT INTO CH(I) VALUES ('abc')",
            "SELECT C FROM CH ORDER BY C",
            "DROP TABLE CH",
        ]);
    }

    /// <summary>
    /// With CharPadding.NEVER a cast truncates and an assignment trims the trailing
    /// spaces before it compares the length, so 'ab  ' fits CHAR(2) but 'abc' does not.
    /// </summary>
    [JavaOracleFact]
    public void CharAssignmentWithoutPaddingMatchesJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE CH(C CHAR(2))",
            "SELECT CAST('abc' AS CHAR(2))",
            "SELECT CAST('a   ' AS CHAR(2))",
            "INSERT INTO CH(C) VALUES ('ab  ')",
            "INSERT INTO CH(C) VALUES ('abc')",
            "SELECT C FROM CH ORDER BY C",
            "DROP TABLE CH",
        ], "MODE=MySQL");
    }

    /// <summary>
    /// BLOB and Java objects cannot be read as character data; BINARY pads with zeroes and
    /// truncates on a cast; BLOB/VARBINARY/CLOB/JSON assignments refuse to lose anything.
    /// </summary>
    [JavaOracleFact]
    public void BinaryBlobClobAndJsonConversionsMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TABLE LB(B BLOB(2), C CLOB(2), VB VARBINARY(2), BB BINARY(2))",
            "INSERT INTO LB(B) VALUES (X'010203')",
            "INSERT INTO LB(C) VALUES ('abc')",
            "INSERT INTO LB(VB) VALUES (X'010203')",
            "INSERT INTO LB(BB) VALUES (X'010203')",
            "SELECT CAST(CAST(X'00' AS BLOB) AS CHAR)",
            "SELECT CAST(CAST(X'00' AS BLOB) AS VARCHAR)",
            "SELECT CAST(CAST(X'00' AS BLOB) AS VARCHAR_IGNORECASE)",
            "SELECT CAST(TRUE AS BINARY)",
            "SELECT CAST(DATE '2020-01-01' AS BINARY)",
            "SELECT CASE WHEN CAST(X'01' AS BINARY(2)) = X'0100' THEN 1 ELSE 0 END",
            "SELECT CASE WHEN CAST(X'010203' AS BINARY(2)) = X'0102' THEN 1 ELSE 0 END",
            // A BINARY source keeps its value object: the second cast pads what the first
            // one already produced instead of rebuilding it from bytes.
            "SELECT CASE WHEN CAST(CAST(X'01' AS BINARY(2)) AS BINARY(2)) = X'0100' THEN 1 ELSE 0 END",
            "CREATE TABLE JS(X JSON(3))",
            "INSERT INTO JS VALUES ('{\"a\":1}')",
            "DROP TABLE JS",
            "DROP TABLE LB",
        ]);
    }

    /// <summary>
    /// ENUM labels map to values through the type's list; JAVA_OBJECT accepts binary
    /// payloads (and nothing else) and keeps the precision check; DECFLOAT takes the
    /// infinities and NaN a REAL can hold.
    /// </summary>
    [JavaOracleFact]
    public void EnumJavaObjectAndDecfloatConversionsMatchJdbc()
    {
        ExplainOracleTests.Check([
            "CREATE TYPE E AS ENUM('a', 'b')",
            "SELECT CAST('a' AS E)",
            "SELECT CAST('c' AS E)",
            "CREATE TABLE J(X JAVA_OBJECT(64))",
            "INSERT INTO J VALUES (CAST(X'0102' AS JAVA_OBJECT))",
            "SELECT CASE WHEN CAST(CAST(X'0102' AS JAVA_OBJECT) AS VARBINARY(64)) = X'0102' THEN 1 ELSE 0 END",
            "SELECT CAST(1 AS JAVA_OBJECT)",
            "CREATE TABLE J2(X JAVA_OBJECT(1))",
            "INSERT INTO J2 VALUES (CAST(X'0102' AS JAVA_OBJECT))",
            "DROP TABLE J2",
            "DROP TABLE J",
            "DROP TYPE E",
            // The values are read as text: the ADO projection of a DECFLOAT goes through
            // decimal, which cannot hold infinity or NaN, while the literal can.
            "SELECT CAST(CAST(CAST('Infinity' AS REAL) AS DECFLOAT) AS VARCHAR)",
            "SELECT CAST(CAST(CAST('-Infinity' AS REAL) AS DECFLOAT) AS VARCHAR)",
            "SELECT CAST(CAST(CAST('NaN' AS REAL) AS DECFLOAT) AS VARCHAR)",
            // A string that is not a number is a conversion error, not a syntax error.
            "SELECT CAST('abc' AS DECFLOAT)",
        ]);
    }

    /// <summary>
    /// Oracle accepts a timestamp string where a DATE is asked for, but the reference never
    /// reaches that conversion in Oracle mode: it parses DATE as TIMESTAMP(0) there
    /// (<c>Mode.dateIsTimestamp0</c>, Parser.java:5820). The port's data type reader does not
    /// port the mode flags (see the header of Parser.DataTypes.cs), so it reads a real DATE,
    /// takes the Oracle compatibility conversion below and reports a DATE with no time.
    /// Blocked by that unported flag; the test stays as the oracle-checked defect.
    /// </summary>
    [Trait("Category", "BlockedByPort")]
    [JavaOracleFact]
    public void OracleDateFromTimestampStringMatchesJdbc()
    {
        ExplainOracleTests.Check([
            "SELECT CAST('2020-01-02 10:00:00' AS DATE)",
        ], "MODE=Oracle");
    }

    /// <summary>
    /// The parameterless ConvertToChar is the conversion of a value to CHAR without a
    /// declared length: the padding a CHAR carries is dropped.
    /// </summary>
    [Fact]
    public void ConvertToCharDropsThePadding()
    {
        Assert.Equal("x", ValueVarchar.Get("x  ").ConvertToChar().GetString());
        Assert.Equal("x", ValueChar.Get("x  ").ConvertToChar().GetString());
    }
}
