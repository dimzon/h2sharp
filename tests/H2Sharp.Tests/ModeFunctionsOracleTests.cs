/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 231: the MSSQLServer compatibility functions of org.h2.mode.FunctionsMSSQLServer.
 * Values that are random or time-dependent are compared by shape, not by their exact value.
 */
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ModeFunctionsOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void MsSqlServerFunctionsMatchTheReference()
    {
        AssertSameAsReferenceInMode("mode_mssql_functions", "MSSQLServer",
            "SELECT ISNULL(NULL, 'x')",
            "SELECT ISNULL('a', 'b')",
            "SELECT LEN('abc')",
            "SELECT LEN(CAST('abc  ' AS CHAR(6)))",
            "SELECT CHARINDEX('b', 'abc')",
            "SELECT CHARINDEX('b', 'abc', 2)",
            "SELECT CHARINDEX('z', 'abc')",
            "SELECT CHARINDEX('b', 'abc', 1, 2)",
            "SELECT YEAR(GETDATE()) > 2000",
            "SELECT CAST(NEWID() AS VARCHAR) LIKE '____-____-____-____-____'",
            "SELECT CAST(NEWSEQUENTIALID() AS VARCHAR) LIKE '____-____-____-____-____'",
            "CREATE TABLE T(ID INT IDENTITY(1,1))",
            "INSERT INTO T DEFAULT VALUES",
            "SELECT SCOPE_IDENTITY()");
    }

    [JavaOracleFact]
    public void LegacyIdentityFunctions()
    {
        AssertSameAsReferenceInMode("mode_legacy_functions", "Legacy",
            "CREATE TABLE T(ID INT IDENTITY)",
            "INSERT INTO T DEFAULT VALUES",
            "SELECT IDENTITY()",
            "SELECT SCOPE_IDENTITY()");
    }

    [JavaOracleFact]
    public void Db2IdentityValLocal()
    {
        AssertSameAsReferenceInMode("mode_db2_functions", "DB2",
            "CREATE TABLE T(ID INT IDENTITY)",
            "INSERT INTO T DEFAULT VALUES",
            "SELECT IDENTITY_VAL_LOCAL()");
    }

    [JavaOracleFact]
    public void OracleAddMonthsAndSysGuid()
    {
        AssertSameAsReferenceInMode("mode_oracle_functions", "Oracle",
            "SELECT EXTRACT(YEAR FROM ADD_MONTHS(DATE '2020-01-31', 1)), "
                + "EXTRACT(MONTH FROM ADD_MONTHS(DATE '2020-01-31', 1)), "
                + "EXTRACT(DAY FROM ADD_MONTHS(DATE '2020-01-31', 1))",
            "SELECT EXTRACT(YEAR FROM ADD_MONTHS(DATE '2020-01-31', -1)), "
                + "EXTRACT(MONTH FROM ADD_MONTHS(DATE '2020-01-31', -1)), "
                + "EXTRACT(DAY FROM ADD_MONTHS(DATE '2020-01-31', -1))",
            "SELECT CAST(SYS_GUID() AS VARCHAR) LIKE '________-____-____-____-____________'");
    }

    [JavaOracleFact]
    public void MySqlFunctions()
    {
        AssertSameAsReferenceInMode("mode_mysql_functions", "MySQL",
            "SELECT UNIX_TIMESTAMP(TIMESTAMP '1970-01-02 00:00:00')",
            // The JVM and CLR defaults differ in time zone and date rendering, so compare
            // parts of the answer rather than the literal string.
            "SELECT SUBSTRING(FROM_UNIXTIME(0), 1, 10)",
            "SELECT SUBSTRING(FROM_UNIXTIME(0, '%Y-%m-%d'), 1, 10)",
            "SELECT YEAR(DATE('2020-01-02 03:04:05')), "
                + "MONTH(DATE('2020-01-02 03:04:05')), DAY(DATE('2020-01-02 03:04:05'))",
            "CREATE TABLE T(ID INT AUTO_INCREMENT PRIMARY KEY)",
            "INSERT INTO T DEFAULT VALUES",
            "SELECT LAST_INSERT_ID()",
            "SELECT LAST_INSERT_ID(5)",
            "SELECT LAST_INSERT_ID()");
    }

    [JavaOracleFact]
    public void PostgreSqlFunctions()
    {
        AssertSameAsReferenceInMode("mode_pg_functions", "PostgreSQL",
            "SELECT CURRENT_DATABASE()",
            "SELECT SUBSTRING(VERSION(), 1, 10)",
            "CREATE TABLE T(ID INT IDENTITY)",
            "CREATE SEQUENCE S",
            "SELECT NEXT VALUE FOR S",
            "SELECT LASTVAL()",
            "SELECT FORMAT_TYPE(1043)",
            "SELECT FORMAT_TYPE(0)",
            "SELECT FORMAT_TYPE(16)",
            "SELECT PG_ENCODING_TO_CHAR(6)",
            "SELECT PG_ENCODING_TO_CHAR(99)",
            "SELECT ARRAY_TO_STRING(ARRAY[1, NULL, 3], ',')",
            "SELECT ARRAY_TO_STRING(ARRAY[1, NULL, 3], ',', 'N')",
            "SELECT HAS_TABLE_PRIVILEGE('T', 'SELECT')",
            "SELECT PG_STAT_GET_NUMSCANS(1)",
            "SELECT CURRTID2(1, 2)",
            "SELECT CAST(GEN_RANDOM_UUID() AS VARCHAR) LIKE '____-____-____-____-____'");
    }

    [JavaOracleFact]
    public void ToDateParsing()
    {
        AssertSameAsReferenceInMode("mode_todate_oracle", "Oracle",
            "SELECT EXTRACT(YEAR FROM TO_DATE('2017-04-21', 'YYYY-MM-DD')), "
                + "EXTRACT(MONTH FROM TO_DATE('2017-04-21', 'YYYY-MM-DD')), "
                + "EXTRACT(DAY FROM TO_DATE('2017-04-21', 'YYYY-MM-DD'))",
            "SELECT EXTRACT(MONTH FROM TO_DATE('2020-01-31 12:30:45', 'YYYY-MM-DD HH24:MI:SS')), "
                + "EXTRACT(DAY FROM TO_DATE('2020-01-31 12:30:45', 'YYYY-MM-DD HH24:MI:SS')), "
                + "EXTRACT(HOUR FROM TO_DATE('2020-01-31 12:30:45', 'YYYY-MM-DD HH24:MI:SS')), "
                + "EXTRACT(MINUTE FROM TO_DATE('2020-01-31 12:30:45', 'YYYY-MM-DD HH24:MI:SS')), "
                + "EXTRACT(SECOND FROM TO_DATE('2020-01-31 12:30:45', 'YYYY-MM-DD HH24:MI:SS'))",
            "SELECT EXTRACT(MONTH FROM TO_DATE('21-APR-2017')), "
                + "EXTRACT(DAY FROM TO_DATE('21-APR-2017'))",
            "SELECT EXTRACT(MONTH FROM TO_TIMESTAMP('2017-04-21 05:06:07')), "
                + "EXTRACT(HOUR FROM TO_TIMESTAMP('2017-04-21 05:06:07'))",
            "SELECT EXTRACT(MONTH FROM TO_DATE('2020 091', 'YYYY DDD')), "
                + "EXTRACT(DAY FROM TO_DATE('2020 091', 'YYYY DDD'))",
            "SELECT EXTRACT(DAY FROM TO_DATE('2459304', 'J'))",
            "SELECT EXTRACT(MONTH FROM TO_DATE('15', 'RM'))",
            "SELECT EXTRACT(MONTH FROM TO_DATE('2020-02-29', 'SYYYY-MM-DD'))",
            "SELECT EXTRACT(YEAR FROM TO_DATE('BC 2020-02-29', 'BC SYYYY-MM-DD'))",
            "SELECT EXTRACT(HOUR FROM TO_TIMESTAMP('2017-04-21 11:06:07 PM', "
                + "'YYYY-MM-DD HH12:MI:SS AM'))",
            "SELECT EXTRACT(SECOND FROM TO_TIMESTAMP('2017-04-21 05:06:07.123', "
                + "'YYYY-MM-DD HH24:MI:SS.FF'))",
            "SELECT EXTRACT(DAY FROM TO_DATE('2020\"T\"04\"T\"21', 'YYYY\"T\"MM\"T\"DD'))",
            "SELECT EXTRACT(YEAR FROM TO_DATE('20', 'RR'))",
            "SELECT EXTRACT(YEAR FROM TO_DATE('49', 'RRRR'))",
            "SELECT EXTRACT(YEAR FROM TO_DATE('CC2020-01-01', 'SCC-YYYY-MM-DD'))",
            "SELECT EXTRACT(YEAR FROM TO_DATE('2020-01-01', 'CC-YYYY-MM-DD'))",
            "SELECT EXTRACT(YEAR FROM TO_DATE('62020-01-01', 'Y,YYYY-MM-DD'))",
            "SELECT EXTRACT(YEAR FROM TO_DATE('20', 'YY'))",
            "SELECT EXTRACT(YEAR FROM TO_DATE('2', 'Y'))",
            "SELECT EXTRACT(YEAR FROM TO_DATE('2020-01-01', 'YYYY-MM-DD'))",
            "SELECT EXTRACT(HOUR FROM TO_TIMESTAMP_TZ('2017-04-21 05:06:07', "
                + "'YYYY-MM-DD HH24:MI:SS'))",
            "TO_DATE('2017-04-21', 'YYYY-Q-MM')",
            "TO_DATE('2017-04-21', 'YYYY-EE-MM')",
            "TO_DATE('2017-04-21', 'YYYY-MM-DD', 'x')");

        AssertSameAsReferenceInMode("mode_todate_pg", "PostgreSQL",
            "SELECT EXTRACT(YEAR FROM TO_DATE('2017-04-21', 'YYYY-MM-DD')), "
                + "EXTRACT(DAY FROM TO_DATE('2017-04-21', 'YYYY-MM-DD'))",
            "SELECT EXTRACT(MONTH FROM TO_TIMESTAMP('2017-04-21 05:06:07')), "
                + "EXTRACT(HOUR FROM TO_TIMESTAMP('2017-04-21 05:06:07'))");
    }

    [JavaOracleFact]
    public void WrongArgumentCountsAreRefused()
    {
        AssertSameAsReferenceInMode("mode_mssql_args", "MSSQLServer",
            "SELECT ISNULL(1)",
            "SELECT LEN()",
            "SELECT LEN(1, 2)");
    }
}
