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
    public void WrongArgumentCountsAreRefused()
    {
        AssertSameAsReferenceInMode("mode_mssql_args", "MSSQLServer",
            "SELECT ISNULL(1)",
            "SELECT LEN()",
            "SELECT LEN(1, 2)");
    }
}
