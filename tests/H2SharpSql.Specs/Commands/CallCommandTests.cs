// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — CALL command, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class CallCommandTests
{
    /// <summary>docs/commands.html:834 — CALL expression</summary>
    [Fact]
    public void Call_Expression_ReturnsItsValue() // CMD-0834
    {
        using IDbConnection connection = TestDatabase.Open();

        int result = connection.QuerySingle<int>("CALL 15 * 25");

        Assert.Equal(375, result);
    }
}
