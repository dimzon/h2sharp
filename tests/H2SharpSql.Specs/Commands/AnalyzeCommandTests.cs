// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — ANALYZE command, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class AnalyzeCommandTests
{
    /// <summary>docs/commands.html:1566 — ANALYZE [ TABLE [schemaName.]tableName ] [ SAMPLE_SIZE rowCountInt ]</summary>
    [Fact]
    public void Analyze_Table_GathersStatisticsWithoutTouchingRows() // CMD-1566
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");

        int affected = connection.Execute("ANALYZE TABLE TEST SAMPLE_SIZE 1000");

        Assert.Equal(0, affected);
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST"));
    }
}
