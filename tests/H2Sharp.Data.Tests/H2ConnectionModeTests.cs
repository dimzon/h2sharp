using System;
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
