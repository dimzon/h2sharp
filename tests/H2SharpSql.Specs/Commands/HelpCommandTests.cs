// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — HELP command, one test per checklist row.
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class HelpCommandTests
{
    /// <summary>docs/commands.html:2410 — HELP [ anything [...] ]</summary>
    [Fact]
    public void Help_WithTopic_ReturnsHelpRows() // CMD-2410
    {
        using IDbConnection connection = TestDatabase.Open();

        List<string> topics = connection.Query<string>("HELP SELECT").AsList();

        Assert.NotEmpty(topics);
    }
}
