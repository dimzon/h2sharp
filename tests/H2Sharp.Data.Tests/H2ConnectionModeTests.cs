using System;
using System.Threading.Tasks;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2ConnectionModeTests
{
    [Fact]
    public void ModeIsFixedWhenTheDatabaseIsOpened()
    {
        string name = "mode_fixed_" + Guid.NewGuid().ToString("N");
        string oracle = "Data Source=" + name + ";Storage=Memory;Mode=Oracle";
        using H2Connection first = new(oracle);
        first.Open();

        using (var command = first.CreateCommand())
        {
            command.CommandText = "SET MODE Oracle";
            Assert.Equal(0, command.ExecuteNonQuery());
        }

        using (var command = first.CreateCommand())
        {
            command.CommandText = "SET MODE Regular";
            H2SharpException error = Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery());
            Assert.Equal(ErrorCode.FeatureNotSupported1, error.H2ErrorCode);
        }

        using H2Connection conflicting = new(
            "Data Source=" + name + ";Storage=Memory;Mode=Regular");
        H2SharpException conflict = Assert.Throws<H2SharpException>(() => conflicting.Open());
        Assert.Equal(ErrorCode.DatabaseAlreadyOpen1, conflict.H2ErrorCode);

        first.Close();

        using H2Connection reopened = new(
            "Data Source=" + name + ";Storage=Memory;Mode=Regular");
        reopened.Open();
        Assert.Equal(Mode.GetInstance("Regular"), reopened.CurrentDatabase!.GetMode());
    }

    [Fact]
    public void CredentialsAreNotPartOfTheConnection()
    {
        using H2Connection connection = new(
            "Data Source=no_credentials;Storage=Memory;Password=secret");
        H2SharpException error = Assert.Throws<H2SharpException>(() => connection.Open());
        Assert.Equal(ErrorCode.FeatureNotSupported1, error.H2ErrorCode);
    }

    [Fact]
    public void ConcurrentOpenAndCloseLeavesNoStaleHolder()
    {
        string name = "concurrent_storage_" + Guid.NewGuid().ToString("N");
        string connectionString = "Data Source=" + name + ";Storage=Memory";

        Parallel.For(0, 32, _ =>
        {
            using H2Connection connection = new(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.ExecuteScalar();
        });

        using H2Connection final = new(connectionString);
        final.Open();
        Assert.NotNull(final.CurrentDatabase);
    }

    [Fact]
    public void ShutdownReleasesTheStorageRegistry()
    {
        string name = "shutdown_release_" + Guid.NewGuid().ToString("N");
        string connectionString = "Data Source=" + name + ";Storage=Memory";

        using (H2Connection first = new(connectionString))
        {
            first.Open();
            H2Sharp.Engine.Database? old = first.CurrentDatabase;
            using var command = first.CreateCommand();
            command.CommandText = "SHUTDOWN";
            _ = command.ExecuteNonQuery();
            first.Close();
            Assert.NotNull(old);
            Assert.True(old.IsClosing());
        }

        using H2Connection second = new(connectionString);
        second.Open();
        Assert.NotNull(second.CurrentDatabase);
    }

    [Fact]
    public void DbCloseDelayMinusOneKeepsTheSameStorage()
    {
        string name = "close_delay_" + Guid.NewGuid().ToString("N");
        string connectionString = "Data Source=" + name + ";Storage=Memory";
        H2Sharp.Engine.Database? database;

        using (H2Connection first = new(connectionString))
        {
            first.Open();
            database = first.CurrentDatabase;
            using var command = first.CreateCommand();
            command.CommandText = "SET DB_CLOSE_DELAY -1";
            _ = command.ExecuteNonQuery();
        }

        using (H2Connection second = new(connectionString))
        {
            second.Open();
            Assert.Same(database, second.CurrentDatabase);
            using var command = second.CreateCommand();
            command.CommandText = "SHUTDOWN";
            _ = command.ExecuteNonQuery();
        }

        using H2Connection third = new(connectionString);
        third.Open();
        Assert.NotSame(database, third.CurrentDatabase);
    }

    [Fact]
    public void StringBuilderRoundTripsTypedInformation()
    {
        ConnectionInfo info = new(
            StorageKind.Memory, "round_trip", Mode.GetInstance("MySQL"));
        info.Options.LockMode = 1;
        info.Options.IfExists = true;

        H2ConnectionStringBuilder builder = new();
        builder.LoadFrom(info);

        ConnectionInfo actual = builder.ToConnectionInfo();
        Assert.Equal(StorageKind.Memory, actual.Storage);
        Assert.Equal("round_trip", actual.DataSource);
        Assert.Equal(Mode.GetInstance("MySQL"), actual.Mode);
        Assert.Equal(1, actual.Options.LockMode);
        Assert.True(actual.Options.IfExists);
    }
}
