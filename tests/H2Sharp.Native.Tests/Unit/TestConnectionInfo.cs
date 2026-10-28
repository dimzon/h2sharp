// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// The port no longer parses JDBC URLs. This test covers the typed connection information
// produced by H2ConnectionStringBuilder.
using System;
using H2Sharp.Data;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Tests the typed connection information and the classic builder.
/// </summary>
public sealed class TestConnectionInfo : TestDb
{
    /// <inheritdoc/>
    [H2Fact]
    public override void Test()
    {
    }

    [H2Fact]
    public void MemoryConnectionIsTyped()
    {
        H2ConnectionStringBuilder builder = new(
            "Data Source=test;Storage=Memory;Mode=Oracle;IFEXISTS=TRUE");

        ConnectionInfo info = builder.ToConnectionInfo();

        AssertEquals(StorageKind.Memory, info.Storage);
        AssertEquals("test", info.DataSource);
        AssertEquals(Mode.GetInstance("Oracle"), info.Mode);
        AssertTrue(info.Options.IfExists);
    }

    [H2Fact]
    public void FileConnectionIsTyped()
    {
        ConnectionInfo info = new H2ConnectionStringBuilder(
            "Data Source=./test;Storage=File").ToConnectionInfo();

        AssertEquals(StorageKind.File, info.Storage);
        AssertEquals("./test", info.DataSource);
        AssertEquals(Mode.GetRegular(), info.Mode);
    }

    [H2Fact]
    public void BuilderRoundTripsAllTypedValues()
    {
        ConnectionInfo info = new()
        {
            Storage = StorageKind.Memory,
            DataSource = "round_trip",
            Mode = Mode.GetInstance("MySQL")!,
            Options =
            {
                IfExists = true,
                ForbidCreation = true,
                Cipher = "AES",
                Init = "CREATE TABLE T(ID INT)",
                OpenNew = true,
                IgnoreUnknownSettings = true,
                LockMode = 1,
                PageSize = 8192,
                AccessModeData = "r",
                AutoServer = true,
                FileLock = H2Sharp.Store.FileLockMethod.No,
            },
        };

        H2ConnectionStringBuilder builder = new();
        builder.LoadFrom(info);
        ConnectionInfo actual = builder.ToConnectionInfo();

        AssertEquals(StorageKind.Memory, actual.Storage);
        AssertEquals("round_trip", actual.DataSource);
        AssertEquals(Mode.GetInstance("MySQL"), actual.Mode);
        AssertEquals(true, actual.Options.IfExists);
        AssertEquals(true, actual.Options.ForbidCreation);
        AssertEquals("AES", actual.Options.Cipher);
        AssertEquals("CREATE TABLE T(ID INT)", actual.Options.Init);
        AssertEquals(true, actual.Options.OpenNew);
        AssertEquals(true, actual.Options.IgnoreUnknownSettings);
        AssertEquals(1, actual.Options.LockMode);
        AssertEquals(8192, actual.Options.PageSize);
        AssertEquals("r", actual.Options.AccessModeData);
        AssertEquals(true, actual.Options.AutoServer);
        AssertEquals(H2Sharp.Store.FileLockMethod.No, actual.Options.FileLock);
    }

    [H2Fact]
    public void UnnamedMemoryIsRepresentedWithoutStringParsing()
    {
        ConnectionInfo info = new H2ConnectionStringBuilder("Storage=Memory").ToConnectionInfo();

        AssertEquals(StorageKind.Memory, info.Storage);
        AssertEquals((string?)null, info.DataSource);
        AssertTrue(info.IsUnnamedInMemory);
        AssertEquals(string.Empty, info.Name);
    }

    [H2Fact]
    public void CredentialsAreRejected()
    {
        AssertThrows(typeof(H2SharpException), () =>
            _ = new H2ConnectionStringBuilder("Data Source=x;Storage=Memory;Password=p")
                .ToConnectionInfo());
    }
}
