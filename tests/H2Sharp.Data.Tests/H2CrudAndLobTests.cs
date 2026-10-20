/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The engine seen through ADO.NET: a table is created, a row is written, read, changed and
 * deleted, and the large values go through the same way.
 *
 * Nothing here is compared against the reference: the reference reaches a caller through
 * JDBC, and this provider has no Java counterpart. What the test asks is that the round trip
 * through the provider is whole — that what goes in comes out, that an update is an update,
 * and that a CLOB and a BLOB are values and not placeholders.
 *
 * The large values are kept in memory by this port, so what is written into the row is the
 * bytes themselves. A lob that lives in the database — a value too large to sit in a row —
 * needs the storage of the lobs, which is not ported, and the test says so where it matters.
 */
using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2CrudAndLobTests
{
    [Fact]
    public void ARowIsWrittenReadChangedAndDeleted()
    {
        using H2Connection connection = Open("crud");
        Execute(connection, "CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR(50), AMOUNT DECIMAL(10, 2))");

        // Written through parameters, not through the text: a value that is in the text is
        // a value that has to be quoted, and this is where the quoting goes wrong.
        using (DbCommand insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO T VALUES(?, ?, ?)";
            AddParameter(insert, 1);
            AddParameter(insert, "first");
            AddParameter(insert, 12.5m);
            Assert.Equal(1, insert.ExecuteNonQuery());
        }

        using (DbCommand insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO T VALUES(?, ?, ?)";
            AddParameter(insert, 2);
            AddParameter(insert, "second");
            AddParameter(insert, 0.25m);
            Assert.Equal(1, insert.ExecuteNonQuery());
        }

        // Read: the values come back as the types the column has, and the reader knows
        // the names of the columns.
        using (DbCommand select = connection.CreateCommand())
        {
            select.CommandText = "SELECT ID, NAME, AMOUNT FROM T ORDER BY ID";
            using DbDataReader reader = select.ExecuteReader();
            Assert.Equal(3, reader.FieldCount);
            Assert.Equal("ID", reader.GetName(0));
            Assert.Equal("AMOUNT", reader.GetName(2));

            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal("first", reader.GetString(1));
            Assert.Equal(12.5m, reader.GetDecimal(2));

            Assert.True(reader.Read());
            Assert.Equal(2, reader.GetInt32(0));
            Assert.Equal("second", reader.GetString(1));

            Assert.False(reader.Read());
        }

        // Changed: the update reaches the row, and the row is the one that changed.
        using (DbCommand update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE T SET NAME = ? WHERE ID = ?";
            AddParameter(update, "changed");
            AddParameter(update, 1);
            Assert.Equal(1, update.ExecuteNonQuery());
        }

        using (DbCommand select = connection.CreateCommand())
        {
            select.CommandText = "SELECT NAME FROM T WHERE ID = 1";
            using DbDataReader reader = select.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("changed", reader.GetString(0));
            Assert.False(reader.Read());
        }

        // Deleted: the row is gone, and the other row is not.
        using (DbCommand delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM T WHERE ID = ?";
            AddParameter(delete, 1);
            Assert.Equal(1, delete.ExecuteNonQuery());
        }

        using (DbCommand select = connection.CreateCommand())
        {
            select.CommandText = "SELECT ID FROM T ORDER BY ID";
            using DbDataReader reader = select.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(2, reader.GetInt32(0));
            Assert.False(reader.Read());
        }
    }

    [Fact]
    public void AClobAndABlobGoThroughTheProvider()
    {
        using H2Connection connection = Open("lob");
        Execute(
            connection,
            "CREATE TABLE T(ID INT PRIMARY KEY, NOTE CLOB, PICTURE BLOB)");

        string note = "a clob that is long enough to be worth its name: "
            + new string('x', 5_000);
        byte[] picture = [.. Enumerable.Range(0, 2_000).Select(i => (byte)(i % 251))];

        using (DbCommand insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO T VALUES(?, ?, ?)";
            AddParameter(insert, 1);
            AddParameter(insert, note);
            AddParameter(insert, picture);
            Assert.Equal(1, insert.ExecuteNonQuery());
        }

        using (DbCommand select = connection.CreateCommand())
        {
            select.CommandText = "SELECT NOTE, PICTURE FROM T WHERE ID = 1";
            using DbDataReader reader = select.ExecuteReader();
            Assert.True(reader.Read());

            // The columns are the large ones and not the small ones they look like: a clob
            // is a value of its own type, and a test that only checked the text would not
            // know the difference.
            Assert.Equal("CHARACTER LARGE OBJECT", reader.GetDataTypeName(0));
            Assert.Equal("BINARY LARGE OBJECT", reader.GetDataTypeName(1));

            // The text comes back as text and the bytes as bytes: a clob is not a number
            // of characters and a blob is not a string.
            Assert.Equal(note, reader.GetString(0));
            Assert.Equal(picture, (byte[])reader.GetValue(1));
            Assert.False(reader.Read());
        }

        // The same values, changed: the update writes the whole value, not a part of it.
        string changed = "a different clob";
        byte[] changedPicture = [1, 2, 3];

        using (DbCommand update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE T SET NOTE = ?, PICTURE = ? WHERE ID = 1";
            AddParameter(update, changed);
            AddParameter(update, changedPicture);
            Assert.Equal(1, update.ExecuteNonQuery());
        }

        using (DbCommand select = connection.CreateCommand())
        {
            select.CommandText = "SELECT NOTE, PICTURE FROM T WHERE ID = 1";
            using DbDataReader reader = select.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(changed, reader.GetString(0));
            Assert.Equal(changedPicture, (byte[])reader.GetValue(1));
            Assert.False(reader.Read());
        }
    }

    [Fact]
    public void AClobWithNullsAndEmptyValuesIsTheSameOnBothSides()
    {
        using H2Connection connection = Open("lob_nulls");
        Execute(connection, "CREATE TABLE T(ID INT PRIMARY KEY, NOTE CLOB, PICTURE BLOB)");

        using (DbCommand insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO T VALUES(?, ?, ?)";
            AddParameter(insert, 1);
            AddParameter(insert, DBNull.Value);
            AddParameter(insert, DBNull.Value);
            Assert.Equal(1, insert.ExecuteNonQuery());
        }

        using (DbCommand insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO T VALUES(?, ?, ?)";
            AddParameter(insert, 2);
            AddParameter(insert, string.Empty);
            AddParameter(insert, Array.Empty<byte>());
            Assert.Equal(1, insert.ExecuteNonQuery());
        }

        using (DbCommand select = connection.CreateCommand())
        {
            select.CommandText = "SELECT NOTE, PICTURE FROM T ORDER BY ID";
            using DbDataReader reader = select.ExecuteReader();

            Assert.True(reader.Read());
            Assert.True(reader.IsDBNull(0));
            Assert.True(reader.IsDBNull(1));

            Assert.True(reader.Read());
            Assert.Equal(string.Empty, reader.GetString(0));
            Assert.Empty((byte[])reader.GetValue(1));

            Assert.False(reader.Read());
        }
    }

    /// <summary>
    /// Opens a connection to a database of its own.
    /// </summary>
    /// <param name="name">the name of the database</param>
    /// <returns>the connection</returns>
    private static H2Connection Open(string name)
    {
        H2Connection connection = new($"jdbc:h2:mem:ado_{name}");
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Adds a value as a parameter of the command. The parameters are numbered in the order
    /// they are added, which is the order of the question marks in the text.
    /// </summary>
    /// <param name="command">the command</param>
    /// <param name="value">the value</param>
    private static void AddParameter(DbCommand command, object? value)
        => command.Parameters.Add(new H2Parameter { Value = value });

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
}
