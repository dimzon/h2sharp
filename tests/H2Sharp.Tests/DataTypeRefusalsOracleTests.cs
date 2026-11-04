/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The data type spellings and refusals: precision and scale limits, the K/M/G/T/P size
 * suffixes of the large objects, the unit words after a length, the compatibility aliases,
 * the domain lookups, and the geometry type mistakes.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DataTypeRefusalsOracleTests
{
    [JavaOracleFact]
    public void PrecisionAndScaleRefusalsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "SELECT CAST(1 AS NUMERIC(0))",
            "SELECT CAST(1 AS NUMERIC(1, 2))",
            "SELECT CAST(1 AS NUMERIC(100001))",
            "SELECT CAST(1 AS NUMERIC(1, 100000))",
            "SELECT CAST(1 AS NUMERIC(100000))",
            "SELECT CAST(1 AS DECFLOAT(0))",
            "SELECT CAST(1 AS DECFLOAT(100001))",
            "SELECT CAST(1 AS DECFLOAT)",
            "SELECT CAST(1 AS FLOAT(0))",
            "SELECT CAST(1 AS FLOAT(54))",
            "SELECT CAST(1 AS FLOAT(53))",
            "SELECT CAST('x' AS VARCHAR(0))",
            "SELECT CAST('x' AS CHAR(0))",
            "SELECT CAST('x' AS BINARY(0))",
            "SELECT CAST('x' AS VARBINARY(0))",
            "SELECT CAST('2020-01-01' AS TIME(10))",
            "SELECT CAST('2020-01-01' AS TIMESTAMP(10))",
            "SELECT CAST('1' AS INTERVAL DAY(0))",
            "SELECT CAST('1' AS INTERVAL YEAR(0))",
            "SELECT CAST('1' AS INTERVAL SECOND(0))",
            "SELECT CAST('1' AS INTERVAL SECOND(9, 10))",
            "SELECT CAST('1' AS INTERVAL SECOND(2))",
        ]);
    }

    [JavaOracleFact]
    public void SizeSuffixesAndUnitWordsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "SELECT CAST('x' AS CLOB(1K))",
            "SELECT CAST('x' AS CLOB(1M))",
            "SELECT CAST('x' AS CLOB(1G))",
            "SELECT CAST('x' AS CLOB(1T))",
            "SELECT CAST('x' AS CLOB(1P))",
            "SELECT CAST('x' AS BLOB(2K))",
            "SELECT CAST('x' AS CLOB(1Q))",
            "SELECT CAST('x' AS CLOB(1k))",
            "SELECT CAST('x' AS VARCHAR(10 CHARACTERS))",
            "SELECT CAST('x' AS VARCHAR(10 OCTETS))",
            "SELECT CAST('x' AS VARCHAR(10 CHAR))",
            "SELECT CAST('x' AS VARCHAR(10 BYTE))",
            "SELECT CAST('x' AS VARCHAR(10 NOSUCH))",
            "SELECT CAST('x' AS CHAR(10 CHARACTERS))",
            "SELECT CAST('x' AS CLOB(10 CHARACTERS))",
        ]);
    }

    [JavaOracleFact]
    public void AliasesAndMisspelledTypesMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE AL1(A LONG RAW)",
            "CREATE TABLE AL2(A NATIONAL CHAR(5))",
            "CREATE TABLE AL3(A NATIONAL CHARACTER VARYING(5))",
            "CREATE TABLE AL4(A VARCHAR_IGNORECASE(10))",
            "CREATE TABLE AL5(A VARCHAR_IGNORECASE)",
            "CREATE TABLE AL6(A LONG VARCHAR)",
            "CREATE TABLE AL7(A VARCHAR(10 CHARACTER VARYING))",
            "SELECT CAST(1 AS NOSUCHTYPE)",
            "CREATE TABLE BT(A NOSUCHTYPE)",
            "CREATE TABLE BT2(A INT, A INT)",
            "SELECT CAST(1 AS INTERVAL)",
            "SELECT CAST(1 AS INTERVAL NOSUCH)",
            "CREATE TABLE BT3(A INTERVAL NOSUCH)",
            "CREATE TABLE GT(A GEOMETRY(NOSUCH))",
            "CREATE TABLE GT2(A GEOMETRY(POINT, -1))",
            "CREATE TABLE GT3(A GEOMETRY(POINT, 100000000))",
            "CREATE TABLE GT4(A GEOMETRY)",
            "CREATE TABLE GT5(A GEOMETRY(POINT))",
        ]);
    }

    [JavaOracleFact]
    public void OracleAcceptsCharAndByteLengthUnits()
    {
        // CHAR and BYTE are units Oracle's mode allows and the regular mode does not; the
        // refusals of the tests above are the regular mode's, so the acceptance is checked
        // where the reference accepts them.
        DatabaseSettingsOracleTests.Compare("MODE=Oracle", [
            "SELECT CAST('x' AS VARCHAR(10 CHAR))",
            "SELECT CAST('x' AS VARCHAR(10 BYTE))",
            "SELECT CAST('x' AS CHAR(10 CHAR))",
            "SELECT CAST('x' AS VARCHAR(10 NOSUCH))",
            "SELECT CAST('x' AS CLOB(10 OCTETS))",
            "SELECT CAST('x' AS CLOB(1K))",
        ]);
    }

    [JavaOracleFact]
    public void DomainLookupsMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE DOMAIN D AS INT",
            "CREATE DOMAIN SD AS D",
            "CREATE TABLE DM(A D, B SD)",
            "SELECT * FROM INFORMATION_SCHEMA.DOMAINS WHERE DOMAIN_NAME IN ('D', 'SD') ORDER BY DOMAIN_NAME",
            "SELECT CAST(1 AS D)",
            "SELECT CAST(1 AS SD)",
            "SELECT CAST(1 AS PUBLIC.D)",
            "SELECT CAST(1 AS NOSUCH.D)",
            "CREATE DOMAIN BAD AS NOSUCH",
        ]);
    }
}
