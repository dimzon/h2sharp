// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — DROP ALL OBJECTS, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class DropAllObjectsCommandTests
{
    /// <summary>docs/commands.html:1984 — DROP ALL OBJECTS [ DELETE FILES ]</summary>
    [Fact]
    public void DropAllObjects_DropsEveryObject() // CMD-1984
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");

        _ = connection.Execute("DROP ALL OBJECTS");

        Assert.Equal(0, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TEST'"));
    }
}
