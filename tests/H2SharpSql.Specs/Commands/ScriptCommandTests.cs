// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — SCRIPT command, one test per checklist row.
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class ScriptCommandTests
{
    /// <summary>docs/commands.html:964 — SCRIPT { [ NODATA ] | [ SIMPLE ] [ COLUMNS ] }</summary>
    [Fact]
    public void Script_NoData_ReturnsTheSchemaWithoutRows() // CMD-0964
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<string> script = connection.Query<string>("SCRIPT NODATA").AsList();

        Assert.NotEmpty(script);
    }

    /// <summary>docs/commands.html:965 — [ NOPASSWORDS ] [ NOSETTINGS ]</summary>
    [Fact]
    public void Script_NoPasswordsNoSettings_ReturnsTheScript() // CMD-0965
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<string> script = connection.Query<string>("SCRIPT NOPASSWORDS NOSETTINGS").AsList();

        Assert.NotEmpty(script);
    }

    /// <summary>docs/commands.html:966 — [ DROP ] [ BLOCKSIZE blockSizeInt ]</summary>
    [Fact]
    public void Script_DropAndBlockSize_ReturnsTheScript() // CMD-0966
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<string> script = connection.Query<string>("SCRIPT DROP BLOCKSIZE 1024").AsList();

        Assert.NotEmpty(script);
    }

    /// <summary>docs/commands.html:967 — [ TO fileNameString scriptCompressionEncryption</summary>
    [Fact]
    public void Script_ToFile_WritesTheScriptToIt() // CMD-0967
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        string path = ScriptPath("h2sharp-script.sql");

        _ = connection.Execute($"SCRIPT TO '{path}'");

        Assert.True(File.Exists(path));
    }

    /// <summary>docs/commands.html:968 — [ CHARSET charsetString ] ]</summary>
    [Fact]
    public void Script_Charset_WritesTheFileInTheGivenEncoding() // CMD-0968
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        string path = ScriptPath("h2sharp-script-utf8.sql");

        _ = connection.Execute($"SCRIPT TO '{path}' CHARSET 'UTF-8'");

        Assert.True(File.Exists(path));
    }

    /// <summary>docs/commands.html:969 — [ TABLE [schemaName.]tableName [, ...] ]</summary>
    [Fact]
    public void Script_Table_ContainsOnlyTheNamedTable() // CMD-0969
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<string> script = connection.Query<string>("SCRIPT TABLE TEST").AsList();

        string joined = string.Join("\n", script);
        Assert.Contains("TEST", joined);
        Assert.DoesNotContain("OTHER", joined);
    }

    /// <summary>docs/commands.html:970 — [ SCHEMA schemaName [, ...] ]</summary>
    [Fact]
    public void Script_Schema_ContainsTheSchemaObjects() // CMD-0970
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        List<string> script = connection.Query<string>("SCRIPT SCHEMA PUBLIC").AsList();

        Assert.Contains("TEST", string.Join("\n", script));
    }

    private static string ScriptPath(string name) => Path.Combine(Path.GetTempPath(), name);

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("CREATE TABLE OTHER(ID INT)");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");
    }
}
