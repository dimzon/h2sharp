/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The sequences, seen through ADO.NET.
 *
 * A sequence is a counter of its own: it hands out 1, 2, 3 to whoever asks, and the statement
 * that asks is NEXT VALUE FOR. The current value is the one it handed out last in this
 * session, and the compatibility spelling of the same thing is NEXTVAL and CURRVAL.
 */
using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2SequenceTests
{
    [Fact]
    public void TheSequenceHandsOutItsNumbers()
    {
        using H2Connection connection = Open("sequence");
        Execute(connection, "CREATE SEQUENCE SEQ_ID");

        // The numbers are 1 and 2: a sequence starts at one.
        Assert.Equal(
            [1L, 2L],
            Read<long>(connection, "SELECT NEXT VALUE FOR SEQ_ID FROM SYSTEM_RANGE(1, 2)"));

        // The current value is the one it handed out last in this session.
        Assert.Equal(
            [2L],
            Read<long>(connection, "SELECT CURRENT VALUE FOR SEQ_ID"));
    }

    [Fact]
    public void TheSequenceStartsWhereItIsTold()
    {
        using H2Connection connection = Open("sequence_start");
        Execute(connection, "CREATE SEQUENCE SEQ_ID START WITH 50 INCREMENT BY 10");

        Assert.Equal(
            [50L, 60L],
            Read<long>(connection, "SELECT NEXT VALUE FOR SEQ_ID FROM SYSTEM_RANGE(1, 2)"));
    }

    [Fact]
    public void TheCompatibilityFunctionsReadTheSameSequence()
    {
        using H2Connection connection = Open("sequence_compat");
        Execute(connection, "CREATE SEQUENCE SEQ_ID");

        Assert.Equal(
            [1L],
            Read<long>(connection, "SELECT NEXTVAL('SEQ_ID')"));
        Assert.Equal(
            [1L],
            Read<long>(connection, "SELECT CURRVAL('SEQ_ID')"));
    }

    [Fact]
    public void TheSequenceIsGoneAfterItIsDropped()
    {
        using H2Connection connection = Open("sequence_drop");
        Execute(connection, "CREATE SEQUENCE SEQ_ID");
        Execute(connection, "DROP SEQUENCE SEQ_ID");

        // The reference answers 90036, and so does the port.
        H2SharpException error = Assert.Throws<H2SharpException>(
            () => Execute(connection, "SELECT NEXT VALUE FOR SEQ_ID"));
        Assert.Equal(90036, error.H2ErrorCode);
    }

    /// <summary>
    /// Opens a connection to a database of its own.
    /// </summary>
    /// <param name="name">the name of the database</param>
    /// <returns>the connection</returns>
    private static H2Connection Open(string name)
    {
        H2Connection connection = new($"Data Source=ado_{name};Storage=Memory");
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Runs a statement that answers nothing.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the statement</param>
    private static void Execute(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads a column of numbers.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="sql">the query</param>
    /// <returns>the numbers</returns>
    private static T[] Read<T>(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using DbDataReader reader = command.ExecuteReader();
        System.Collections.Generic.List<T> values = [];
        while (reader.Read())
        {
            values.Add((T)reader.GetValue(0));
        }

        return [.. values];
    }
}
