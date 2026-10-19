// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — RUNSCRIPT command, one test per checklist row.
using System.Data;
using System.IO;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class RunscriptCommandTests
{
    /// <summary>docs/commands.html:941 — RUNSCRIPT FROM fileNameString scriptCompressionEncryption</summary>
    [Fact]
    public void Runscript_FromFile_RunsTheStatementsInIt() // CMD-0941
    {
        using IDbConnection connection = TestDatabase.Open();
        string script = WriteScript("CREATE TABLE TEST(ID INT)");

        _ = connection.Execute($"RUNSCRIPT FROM '{script}'");

        Assert.Equal(1, CountTestTables(connection));
    }

    /// <summary>docs/commands.html:942 — [ CHARSET charsetString ]</summary>
    [Fact]
    public void Runscript_Charset_ReadsTheFileInTheGivenEncoding() // CMD-0942
    {
        using IDbConnection connection = TestDatabase.Open();
        string script = WriteScript("CREATE TABLE TEST(ID INT)");

        _ = connection.Execute($"RUNSCRIPT FROM '{script}' CHARSET 'UTF-8'");

        Assert.Equal(1, CountTestTables(connection));
    }

    /// <summary>docs/commands.html:943 — { [ QUIRKS_MODE ] [ VARIABLE_BINARY ] | FROM_1X }</summary>
    [Fact]
    public void Runscript_QuirksMode_RunsTheStatementsInIt() // CMD-0943
    {
        using IDbConnection connection = TestDatabase.Open();
        string script = WriteScript("CREATE TABLE TEST(ID INT)");

        _ = connection.Execute($"RUNSCRIPT FROM '{script}' QUIRKS_MODE");

        Assert.Equal(1, CountTestTables(connection));
    }

    private static string WriteScript(string sql)
    {
        string path = Path.Combine(Path.GetTempPath(), "h2sharp-runscript.sql");
        File.WriteAllText(path, sql);
        return path;
    }

    private static int CountTestTables(IDbConnection connection) =>
        connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'PUBLIC' AND TABLE_NAME = 'TEST'");
}
