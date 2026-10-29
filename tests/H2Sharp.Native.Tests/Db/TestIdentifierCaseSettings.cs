// Copyright 2026 H2Sharp contributors.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// The port always folds unquoted identifiers to upper case and ignores DATABASE_TO_UPPER /
// DATABASE_TO_LOWER. CASE_INSENSITIVE_IDENTIFIERS is the only identifier-case switch. A data
// source owns one Database; raw connections are self-contained, so two raw connections with
// the same memory name do not share storage.
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
    public void DataSourceSharesOneDatabase()
    {
        string name = MemoryName("share");
        using H2DataSource dataSource = new H2DataSourceBuilder(
            "Data Source=" + name + ";Storage=Memory;Mode=Oracle;CASE_INSENSITIVE_IDENTIFIERS=TRUE")
            .Build();
        using H2Connection first = (H2Connection)dataSource.CreateConnection();
        using H2Connection second = (H2Connection)dataSource.CreateConnection();
        first.Open();
        second.Open();

        Assert.Same(first.CurrentDatabase, second.CurrentDatabase);
    }

    [Fact]
    public void RawConnectionsDoNotShareOneMemoryDatabase()
    {
        string name = MemoryName("raw");
        using H2Connection first = Open(name, "Mode=Regular");
        using H2Connection second = Open(name, "Mode=Regular;CASE_INSENSITIVE_IDENTIFIERS=TRUE");
        first.Open();
        second.Open();

        Assert.NotSame(first.CurrentDatabase, second.CurrentDatabase);
    }

    [Fact]
    public void RawConnectionsWithDifferentModesDoNotShareEither()
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
