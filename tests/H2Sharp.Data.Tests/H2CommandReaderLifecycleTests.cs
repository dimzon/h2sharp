/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The lifecycle contract between a command and its readers: what may happen while a reader
 * is open, what refuses, and what the dispose of a reader gives back.
 *
 * The split follows SqlClient: assigning CommandText (or anything else that does not touch
 * the connection's wire) while a reader is open is legal, and the conflict with a second
 * open reader surfaces at execution. An embedded engine goes further than SqlClient without
 * MARS in two harmless ways, pinned here on purpose: a second command of the same
 * connection executes while a reader is open, and a non-reader execution on the command
 * itself is allowed - the session serializes the statements and the open result stays
 * valid. The one hard rule is one reader per command.
 */
using System;
using System.Data;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2CommandReaderLifecycleTests : IDisposable
{
    private readonly H2Connection connection = new("Storage=Memory;");

    public H2CommandReaderLifecycleTests()
    {
        connection.Open();
        using H2Command setup = (H2Command)connection.CreateCommand();
        setup.CommandText = "CREATE TABLE t(id int)";
        _ = setup.ExecuteNonQuery();
        setup.CommandText = "INSERT INTO t VALUES (1)";
        _ = setup.ExecuteNonQuery();
    }

    public void Dispose() => connection.Dispose();

    [Fact]
    public void CommandTextIsAssignableWhileOwnReaderIsOpen()
    {
        using H2Command command = (H2Command)connection.CreateCommand();
        command.CommandText = "SELECT id FROM t";
        using H2DataReader reader = (H2DataReader)command.ExecuteReader();
        _ = reader.Read();

        // The assignment is legal: the conflict with an open reader surfaces at execution,
        // the same split SqlClient makes. The new text is not lost.
        command.CommandText = "SELECT id FROM t WHERE id = 1";
        Assert.Equal("SELECT id FROM t WHERE id = 1", command.CommandText);
    }

    [Fact]
    public void NewCommandTextIsUsedAfterTheReaderCloses()
    {
        using H2Command command = (H2Command)connection.CreateCommand();
        command.CommandText = "SELECT id FROM t";
        using (H2DataReader reader = (H2DataReader)command.ExecuteReader())
        {
            _ = reader.Read();
            command.CommandText = "SELECT COUNT(*) FROM t";
        }

        using H2DataReader second = (H2DataReader)command.ExecuteReader();
        _ = second.Read();
        Assert.Equal(1, second.GetInt32(0));
    }

    [Fact]
    public void SecondReaderOnTheSameCommandIsRefused()
    {
        using H2Command command = (H2Command)connection.CreateCommand();
        command.CommandText = "SELECT id FROM t";
        using H2DataReader reader = (H2DataReader)command.ExecuteReader();
        _ = reader.Read();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => _ = (H2DataReader)command.ExecuteReader());
        Assert.Equal("The command already has an open reader", error.Message);
    }

    [Fact]
    public void CommandExecutesAgainAfterTheReaderIsDisposed()
    {
        using H2Command command = (H2Command)connection.CreateCommand();
        command.CommandText = "SELECT id FROM t";
        using (H2DataReader reader = (H2DataReader)command.ExecuteReader())
        {
            _ = reader.Read();
        }

        using H2DataReader second = (H2DataReader)command.ExecuteReader();
        _ = second.Read();
    }

    [Fact]
    public void DisposingTheReaderTwiceIsHarmless()
    {
        using H2Command command = (H2Command)connection.CreateCommand();
        command.CommandText = "SELECT id FROM t";
        H2DataReader reader = (H2DataReader)command.ExecuteReader();
        _ = reader.Read();
        reader.Dispose();
        reader.Dispose();

        using H2DataReader second = (H2DataReader)command.ExecuteReader();
        _ = second.Read();
    }

    [Fact]
    public void AnotherCommandOfTheSameConnectionExecutesWhileAReaderIsOpen()
    {
        using H2Command first = (H2Command)connection.CreateCommand();
        first.CommandText = "SELECT id FROM t";
        using H2DataReader reader = (H2DataReader)first.ExecuteReader();
        _ = reader.Read();

        using H2Command second = (H2Command)connection.CreateCommand();
        second.CommandText = "SELECT COUNT(*) FROM t";
        using H2DataReader secondReader = (H2DataReader)second.ExecuteReader();
        _ = secondReader.Read();
        Assert.Equal(1, secondReader.GetInt32(0));
    }

    [Fact]
    public void NonReaderExecutionWhileOwnReaderIsOpen()
    {
        using H2Command command = (H2Command)connection.CreateCommand();
        command.CommandText = "SELECT id FROM t";
        using H2DataReader reader = (H2DataReader)command.ExecuteReader();
        _ = reader.Read();

        // An execution that creates no reader does not conflict with the open one: the
        // session serializes the statements, and the open result stays valid.
        command.CommandText = "INSERT INTO t VALUES (2)";
        Assert.Equal(1, command.ExecuteNonQuery());
    }

    [Fact]
    public void ConnectionSetterIsRefusedWhileOwnReaderIsOpen()
    {
        using H2Command command = (H2Command)connection.CreateCommand();
        command.CommandText = "SELECT id FROM t";
        using H2DataReader reader = (H2DataReader)command.ExecuteReader();
        _ = reader.Read();

        using H2Connection other = new("Storage=Memory;");
        other.Open();
        Assert.Throws<InvalidOperationException>(() => command.Connection = other);
    }

    [Fact]
    public void CommandBehaviorCloseConnectionClosesTheConnectionWithTheReader()
    {
        // An unnamed memory database is per connection: the table lives here.
        using H2Connection owned = new("Storage=Memory;");
        owned.Open();
        using (H2Command setup = (H2Command)owned.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE t(id int)";
            _ = setup.ExecuteNonQuery();
        }

        using H2Command command = (H2Command)owned.CreateCommand();
        command.CommandText = "SELECT id FROM t";
        using H2DataReader reader = (H2DataReader)command.ExecuteReader(CommandBehavior.CloseConnection);
        _ = reader.Read();
        reader.Dispose();

        Assert.Equal(ConnectionState.Closed, owned.State);
    }

    [Fact]
    public void NullCommandTextBecomesEmpty()
    {
        using H2Command command = (H2Command)connection.CreateCommand();
        command.CommandText = "SELECT 1";
#pragma warning disable CS8625 // the base contract is [AllowNull]
        command.CommandText = null;
#pragma warning restore CS8625
        Assert.Equal(string.Empty, command.CommandText);
    }
}
