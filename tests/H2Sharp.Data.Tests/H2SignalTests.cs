/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * SIGNAL and the optional ENCRYPT/DECRYPT functions from H2Sharp.Crypto.
 */
using System;
using System.Data.Common;
using H2Sharp.Crypto;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2SignalTests
{
    [Fact]
    public void SignalThrowsWithTheMessage()
    {
        using H2Connection connection = NewConnection();
        connection.Open();

        Exception error = Assert.ThrowsAny<Exception>(
            () => Scalar(connection, "CALL SIGNAL('23505', 'Duplicate user ID')"));
        Assert.Contains("Duplicate user ID", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SignalRefusesAnInvalidSqlState()
    {
        using H2Connection connection = NewConnection();
        connection.Open();

        Assert.ThrowsAny<Exception>(() => Scalar(connection, "CALL SIGNAL('00', 'no')"));
        Assert.ThrowsAny<Exception>(() => Scalar(connection, "CALL SIGNAL('abc', 'no')"));
    }

    [Fact]
    public void CryptFunctionsRoundTripThroughAes()
    {
        using H2Connection connection = NewConnection();
        ((IH2Extensions)connection).UseCrypto();
        connection.Open();

        using (DbCommand encrypt = connection.CreateCommand())
        {
            encrypt.CommandText = "SELECT ENCRYPT('AES', X'00', STRINGTOUTF8('Test'))";
            byte[] cipher = (byte[])encrypt.ExecuteScalar()!;

            using DbCommand decrypt = connection.CreateCommand();
            decrypt.CommandText = "SELECT UTF8TOSTRING(DECRYPT('AES', X'00', ?))";
            decrypt.Parameters.Add(new H2Parameter { Value = cipher });
            object? value = decrypt.ExecuteScalar();
            Assert.Equal("Test", Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!.TrimEnd('\0'));
        }
    }

    private static H2Connection NewConnection()
        => new($"Data Source=signal_{Guid.NewGuid():N};IGNORE_UNKNOWN_SETTINGS=TRUE;Storage=Memory");

    private static string Scalar(H2Connection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? "-"
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "-";
    }
}
