/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The conversion paths of H2DataReader: every getter's accepted source types, the ones it
 * refuses, and the NULL answer. The rows are built over a fresh in-memory database, one
 * statement per shape, so each getter sees the type it is asked about.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2DataReaderCastTests : IDisposable
{
    private readonly H2Connection connection;

    public H2DataReaderCastTests()
    {
        connection = new H2Connection("Data Source=reader_casts;Storage=Memory");
        connection.Open();
        using DbCommand create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE T("
            + "B BOOLEAN, TI TINYINT, I INT, BI BIGINT, D DECIMAL(5,2), R REAL, "
            + "V VARCHAR, C CHAR(1), DT DATE, TSTZ TIMESTAMP WITH TIME ZONE, "
            + "U UUID, VB VARBINARY(16), BL BLOB)";
        create.ExecuteNonQuery();
        using DbCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO T VALUES ("
            + "TRUE, 2, 3, 4, 5.5, 6.5, "
            + "'true', 'A', DATE '2024-01-02', TIMESTAMP WITH TIME ZONE '2024-01-02 03:04:05+03', "
            + "'03020100-0405-0607-0809-0a0b0c0d0e0f', X'000102030405060708090a0b0c0d0e0f', X'01')";
        insert.ExecuteNonQuery();
    }

    public void Dispose()
    {
        connection.Dispose();
    }

    private DbDataReader Read(string columns, string where = "B IS TRUE")
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT " + columns + " FROM T WHERE " + where;
        return command.ExecuteReader();
    }

    [Fact]
    public void BooleanReadsFromBoolTextAndNumbers()
    {
        using DbDataReader reader = Read("B, V, I, D, VB");
        Assert.True(reader.Read());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.GetBoolean(3));
        Assert.Throws<InvalidCastException>(() => reader.GetBoolean(4));
    }

    [Fact]
    public void CharReadsSingleCharacterAndRefusesLongerOnes()
    {
        using DbDataReader reader = Read("C, V");
        Assert.True(reader.Read());
        Assert.Equal('A', reader.GetChar(0));
        Assert.Throws<InvalidCastException>(() => reader.GetChar(1));
    }

    [Fact]
    public void DateTimeReadsDateAndTimestampWithZone()
    {
        using DbDataReader reader = Read("DT, TSTZ");
        Assert.True(reader.Read());
        Assert.Equal(new DateTime(2024, 1, 2), reader.GetDateTime(0));
        Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5), reader.GetDateTime(1));
    }

    [Fact]
    public void GuidReadsUuidBinaryAndText()
    {
        Guid literal = new("03020100-0405-0607-0809-0a0b0c0d0e0f");
        // A 16-byte binary reads through new Guid(byte[]), whose first groups swap.
        Guid fromBinary = new("03020100-0504-0706-0809-0a0b0c0d0e0f");
        using DbDataReader reader = Read("U, VB, V, I");
        Assert.True(reader.Read());
        Assert.Equal(literal, reader.GetGuid(0));
        Assert.Equal(fromBinary, reader.GetGuid(1));
        // The row's V is 'true': not a GUID, so the text path refuses it.
        Assert.Throws<FormatException>(() => reader.GetGuid(2));
        Assert.Throws<InvalidCastException>(() => reader.GetGuid(3));
    }

    [Fact]
    public void BinaryAndTextGettersRefuseWrongKinds()
    {
        using DbDataReader reader = Read("VB, I");
        Assert.True(reader.Read());
        // INTEGER has no byte[] representation; its text form reads through GetString.
        Assert.Throws<InvalidCastException>(() => reader.GetBytes(1, 0, null, 0, 0));
        Assert.Equal("3", reader.GetString(1));
        Assert.Equal(1, reader.GetChars(1, 0, null, 0, 0));
    }

    [Fact]
    public void NullAnswersDBNullAndRefusesReads()
    {
        using DbCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO T (B, I, C) VALUES (FALSE, NULL, NULL)";
        insert.ExecuteNonQuery();
        using DbDataReader reader = Read("I, B, C", "B IS FALSE");
        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(0));
        Assert.False(reader.IsDBNull(1));

        // Numeric reads keep the JDBC answer of zero for NULL.
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Throws<InvalidCastException>(() => reader.GetChar(2));
    }

    [Fact]
    public void GetFieldValueRunsTheGenericConversions()
    {
        using DbDataReader reader = Read("R, I, DT");
        Assert.True(reader.Read());
        Assert.Equal(6.5f, reader.GetFieldValue<float>(0));
        Assert.Equal(3, reader.GetFieldValue<int>(1));
        Assert.Throws<InvalidCastException>(() => reader.GetFieldValue<string>(1));
        Assert.Throws<InvalidCastException>(() => reader.GetFieldValue<Guid>(2));
    }

    [Fact]
    public void OrdinalsResolveByAliasAndRefuseUnknownNames()
    {
        using DbDataReader reader = Read("I AS THE_NUMBER, V AS THE_TEXT");
        Assert.True(reader.Read());
        Assert.Equal(0, reader.GetOrdinal("the_number"));
        Assert.Equal(1, reader.GetOrdinal("THE_TEXT"));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("nope"));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetInt32(9));
    }
}
