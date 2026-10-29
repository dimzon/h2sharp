// Copyright 2026 H2Sharp contributors.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// The port always folds unquoted identifiers to upper case and ignores DATABASE_TO_UPPER /
// DATABASE_TO_LOWER. CASE_INSENSITIVE_IDENTIFIERS is the only identifier-case switch. The
// database registry keys an instance by storage, name, mode and that switch, so only
// connections with the same conflicting parameters share one Database instance.
using H2Sharp.Api;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Native.Tests.Db;

public sealed class TestIdentifierCaseSettings
{
    private static string MemoryName(string suffix)
        => "identifierCase_" + suffix + "_" + System.Guid.NewGuid().ToString("N");

    private static H2Connection Open(string name, string options)
        => new("Data Source=" + name + ";Storage=Memory;" + options);

    [Fact]
    public void SameParametersShareOneDatabase()
    {
        string name = MemoryName("share");
        using H2Connection first = Open(name, "Mode=Oracle;CASE_INSENSITIVE_IDENTIFIERS=TRUE");
        using H2Connection second = Open(name, "Mode=Oracle;CASE_INSENSITIVE_IDENTIFIERS=TRUE");
        first.Open();
        second.Open();

        Assert.Same(first.CurrentDatabase, second.CurrentDatabase);
    }

    [Fact]
    public void DifferentCaseSettingGetsAnotherDatabase()
    {
        string name = MemoryName("case");
        using H2Connection first = Open(name, "Mode=Regular");
        using H2Connection second = Open(name, "Mode=Regular;CASE_INSENSITIVE_IDENTIFIERS=TRUE");
        first.Open();
        second.Open();

        Assert.NotSame(first.CurrentDatabase, second.CurrentDatabase);
    }

    [Fact]
    public void DifferentModeGetsAnotherDatabase()
    {
        string name = MemoryName("mode");
        using H2Connection first = Open(name, "Mode=Regular");
        using H2Connection second = Open(name, "Mode=Oracle");
        first.Open();
        second.Open();

        Assert.NotSame(first.CurrentDatabase, second.CurrentDatabase);
    }

    [Fact]
    public void DifferentSettingsOnTheSameFileConflict()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "h2sharp_case_" + System.Guid.NewGuid().ToString("N"));
        try
        {
            using H2Connection first = new(
                "Data Source=" + path + ";Storage=File;CASE_INSENSITIVE_IDENTIFIERS=TRUE");
            first.Open();
            using H2Connection second = new(
                "Data Source=" + path + ";Storage=File");
            _ = Assert.Throws<H2SharpException>(() => second.Open());
        }
        finally
        {
            System.IO.File.Delete(path + ".mv.db");
        }
    }

    [Fact]
    public void DefaultIdentifiersAreCaseSensitive()
    {
        string name = MemoryName("default");
        using H2Connection connection = Open(name, "Mode=Regular");
        connection.Open();
        using DbCommand command = connection.CreateCommand();

        command.CommandText = "CREATE TABLE \"Foo\"(ID INT)";
        command.ExecuteNonQuery();
        command.CommandText = "CREATE TABLE \"foo\"(ID INT)";
        command.ExecuteNonQuery();

        command.CommandText = "SELECT * FROM \"Foo\"";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT * FROM \"foo\"";
        command.ExecuteNonQuery();
    }

    [Fact]
    public void QuotedAndUnquotedDifferByDefault()
    {
        string name = MemoryName("quoted");
        using H2Connection connection = Open(name, "Mode=Regular");
        connection.Open();
        using DbCommand command = connection.CreateCommand();

        command.CommandText = "CREATE TABLE Foo(ID INT)";
        command.ExecuteNonQuery();

        command.CommandText = "SELECT * FROM FOO";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT * FROM foo";
        command.ExecuteNonQuery();

        command.CommandText = "SELECT * FROM \"Foo\"";
        H2SharpException error = Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery());
        Assert.Equal(ErrorCode.TableOrViewNotFound1, error.H2ErrorCode);
    }

    [Fact]
    public void CaseInsensitiveIdentifiersMatchQuotedAndUnquoted()
    {
        string name = MemoryName("insensitive");
        using H2Connection connection = Open(name,
            "Mode=Regular;CASE_INSENSITIVE_IDENTIFIERS=TRUE");
        connection.Open();
        using DbCommand command = connection.CreateCommand();

        command.CommandText = "CREATE TABLE \"Foo\"(ID INT)";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT * FROM \"foo\"";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT * FROM foo";
        command.ExecuteNonQuery();
    }

    [Fact]
    public void DatabaseToLowerIsIgnored()
    {
        string name = MemoryName("lower");
        using H2Connection connection = Open(name, "Mode=Regular;DATABASE_TO_LOWER=TRUE");
        connection.Open();
        using DbCommand command = connection.CreateCommand();

        command.CommandText = "CREATE TABLE Foo(ID INT)";
        command.ExecuteNonQuery();

        command.CommandText = "SELECT * FROM FOO";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT * FROM foo";
        command.ExecuteNonQuery();

        command.CommandText = "SELECT * FROM \"foo\"";
        H2SharpException error = Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery());
        Assert.Equal(ErrorCode.TableOrViewNotFound1, error.H2ErrorCode);
    }

    [Fact]
    public void DatabaseToUpperFalseIsIgnored()
    {
        string name = MemoryName("upper");
        using H2Connection connection = Open(name, "Mode=Regular;DATABASE_TO_UPPER=FALSE");
        connection.Open();
        using DbCommand command = connection.CreateCommand();

        command.CommandText = "CREATE TABLE Foo(ID INT)";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT * FROM foo";
        command.ExecuteNonQuery();

        command.CommandText = "SELECT * FROM \"Foo\"";
        H2SharpException error = Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery());
        Assert.Equal(ErrorCode.TableOrViewNotFound1, error.H2ErrorCode);
    }
}
