// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — EXECUTE IMMEDIATE command, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class ExecuteImmediateCommandTests
{
    /// <summary>docs/commands.html:853 — EXECUTE IMMEDIATE sqlString</summary>
    [Fact]
    public void ExecuteImmediate_String_IsPreparedAndRun() // CMD-0853
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("EXECUTE IMMEDIATE 'CREATE TABLE TEST(ID INT)'");

        int tables = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'PUBLIC' AND TABLE_NAME = 'TEST'");
        Assert.Equal(1, tables);
    }
}
