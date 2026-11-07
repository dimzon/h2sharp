/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * PREPARE / EXECUTE / DEALLOCATE, the session's stored statements: the parameter list of
 * PREPARE is purely syntactic, the values of EXECUTE bind to the statement's parameters
 * by position, and DEALLOCATE makes the name unknown. Compared with H2 2.5 on the JVM.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class SessionProcedureOracleTests
{
    [JavaOracleFact]
    public void PrepareExecuteAndDeallocateMatchJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "PREPARE test(int, int) AS SELECT ?1 * ?2",
            "EXECUTE test(3, 2)",
            "EXECUTE test(4, 5)",
            // A second PREPARE of the same name replaces the stored statement.
            "PREPARE test AS SELECT 40 + ?",
            "EXECUTE test(2)",
            // DEALLOCATE removes the name; the next EXECUTE is an unknown function.
            "DEALLOCATE test",
            "EXECUTE test(2)",
            // The type list is dropped: a statement with no parameters still runs, and a
            // mismatched list changes nothing.
            "PREPARE p2(nonsense type) AS SELECT 1",
            "EXECUTE p2",
            // DEALLOCATE of an unknown name is fine.
            "DEALLOCATE p2",
            "DEALLOCATE p2",
        ]);
    }

    [Fact]
    public void PrepareIsRejectedInSqlServerMode()
    {
        // PostgreSQL-style PREPARE is disabled in MSSQLServer mode, as the reference's
        // parsePrepareProcedure refuses before reading anything: the two-pass parser names
        // what the position expected (SYNTAX_ERROR_2).
        using H2Sharp.Data.H2Connection connection = new(
            "Data Source=prepare_mssql_" + Guid.NewGuid().ToString("N") + ";Storage=Memory;MODE=MSSQLServer");
        connection.Open();
        using H2Sharp.Data.H2Command command = (H2Sharp.Data.H2Command)connection.CreateCommand();
        command.CommandText = "PREPARE test AS SELECT 1";
        H2Sharp.H2SharpException error = Assert.Throws<H2Sharp.H2SharpException>(
            () => _ = command.ExecuteNonQuery());
        Assert.Equal(H2Sharp.Api.ErrorCode.SyntaxError2, error.H2ErrorCode);
    }
}
