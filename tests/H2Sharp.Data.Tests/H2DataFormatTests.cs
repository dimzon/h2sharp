/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Text;
using System.Text.Json;
using Dapper;
using H2Sharp.Data;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2DataFormatTests
{
    [Fact]
    public void DefaultsKeepTheReferenceProjection()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(B BLOB, C CLOB, J JSON, A INTEGER ARRAY)");
        connection.Execute("INSERT INTO T VALUES (X'0102', 'text', JSON '{\"a\":1}', ARRAY[1, 2])");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT B, C, J, A FROM T";
        using DbDataReader reader = command.ExecuteReader();
        AssertColumnMetadata(reader, 0, typeof(byte[]), "BINARY LARGE OBJECT");
        AssertColumnMetadata(reader, 1, typeof(string), "CHARACTER LARGE OBJECT");
        AssertColumnMetadata(reader, 2, typeof(byte[]), "JSON");
        AssertColumnMetadata(reader, 3, typeof(int[]), "INTEGER ARRAY");
        Assert.True(reader.Read());
        Assert.Equal(new byte[] { 1, 2 }, Assert.IsType<byte[]>(reader.GetValue(0)));
        Assert.Equal("text", Assert.IsType<string>(reader.GetValue(1)));
        Assert.Equal("{\"a\":1}"u8.ToArray(), Assert.IsType<byte[]>(reader.GetValue(2)));
        Assert.Equal(new[] { 1, 2 }, Assert.IsType<int[]>(reader.GetValue(3)));
    }

    [Fact]
    public void BlobReadsAsAStreamWithConsistentMetadata()
    {
        using H2Connection connection = Open(f => f.BlobFormat = H2BlobFormat.Stream);
        connection.Execute("CREATE TABLE T(ID INT, B BLOB)");
        connection.Execute("INSERT INTO T VALUES (1, X'414243'), (2, NULL)");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT B FROM T ORDER BY ID";
        using (DbDataReader reader = command.ExecuteReader())
        {
            AssertColumnMetadata(reader, 0, typeof(Stream), "BINARY LARGE OBJECT");
            Assert.True(reader.Read());
            using (Stream stream = Assert.IsAssignableFrom<Stream>(reader.GetValue(0)))
            using (StreamReader text = new(stream, Encoding.UTF8))
            {
                Assert.Equal("ABC", text.ReadToEnd());
            }

            Assert.Equal("ABC"u8.ToArray(), reader.GetFieldValue<byte[]>(0));
            Assert.Equal("ABC", reader.GetString(0));
            using (TextReader text = reader.GetTextReader(0))
            {
                Assert.Equal("ABC", text.ReadToEnd());
            }

            using (Stream stream = reader.GetStream(0))
            using (MemoryStream copy = new())
            {
                stream.CopyTo(copy);
                Assert.Equal("ABC"u8.ToArray(), copy.ToArray());
            }

            object[] values = new object[1];
            Assert.Equal(1, reader.GetValues(values));
            Assert.IsAssignableFrom<Stream>(values[0]);
            Assert.True(reader.Read());
            Assert.True(reader.IsDBNull(0));
            Assert.Same(DBNull.Value, reader.GetValue(0));
        }

        command.CommandText = "SELECT B FROM T WHERE ID = 1";
        object? scalar = command.ExecuteScalar();
        using MemoryStream detached = Assert.IsType<MemoryStream>(scalar);
        Assert.Equal("ABC"u8.ToArray(), detached.ToArray());
    }

    [Fact]
    public void ClobReadsAsATextReaderWithConsistentMetadata()
    {
        using H2Connection connection = Open(f => f.ClobFormat = H2ClobFormat.TextReader);
        connection.Execute("CREATE TABLE T(ID INT, C CLOB)");
        connection.Execute("INSERT INTO T VALUES (1, 'hello'), (2, NULL)");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT C FROM T ORDER BY ID";
        using (DbDataReader reader = command.ExecuteReader())
        {
            AssertColumnMetadata(reader, 0, typeof(TextReader), "CHARACTER LARGE OBJECT");
            Assert.True(reader.Read());
            using (TextReader text = Assert.IsAssignableFrom<TextReader>(reader.GetValue(0)))
            {
                Assert.Equal("hello", text.ReadToEnd());
            }

            Assert.Equal("hello", reader.GetString(0));
            Assert.Equal("hello", reader.GetFieldValue<string>(0));
            Assert.Equal("hello"u8.ToArray(), reader.GetFieldValue<byte[]>(0));
            using (TextReader text = reader.GetTextReader(0))
            {
                Assert.Equal("hello", text.ReadToEnd());
            }

            byte[] buffer = new byte[5];
            Assert.Equal(5, reader.GetBytes(0, 0, buffer, 0, buffer.Length));
            Assert.Equal("hello"u8.ToArray(), buffer);
            Assert.True(reader.Read());
            Assert.True(reader.IsDBNull(0));
            Assert.Same(DBNull.Value, reader.GetValue(0));
        }

        command.CommandText = "SELECT C FROM T WHERE ID = 1";
        using StringReader detached = Assert.IsType<StringReader>(command.ExecuteScalar());
        Assert.Equal("hello", detached.ReadToEnd());
    }

    [Fact]
    public void JsonReadsAsTextOrStreamWithConsistentMetadata()
    {
        using (H2Connection connection = Open(f => f.JsonFormat = H2JsonFormat.String))
        {
            connection.Execute("CREATE TABLE J(ID INT, D JSON)");
            connection.Execute("INSERT INTO J VALUES (1, JSON '{\"a\":1}'), (2, JSON 'null'), (3, NULL)");
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT D FROM J ORDER BY ID";
            using DbDataReader reader = command.ExecuteReader();
            AssertColumnMetadata(reader, 0, typeof(string), "JSON");
            Assert.True(reader.Read());
            Assert.Equal("{\"a\":1}", Assert.IsType<string>(reader.GetValue(0)));
            Assert.Equal("{\"a\":1}"u8.ToArray(), reader.GetFieldValue<byte[]>(0));
            Assert.Equal("{\"a\":1}", reader.GetString(0));
            using (TextReader text = reader.GetTextReader(0))
            {
                Assert.Equal("{\"a\":1}", text.ReadToEnd());
            }

            Assert.True(reader.Read());
            Assert.Equal("null", Assert.IsType<string>(reader.GetValue(0)));
            Assert.True(reader.Read());
            Assert.True(reader.IsDBNull(0));
            Assert.Same(DBNull.Value, reader.GetValue(0));
        }

        using (H2Connection connection = Open(f => f.JsonFormat = H2JsonFormat.TextReader))
        {
            connection.Execute("CREATE TABLE J(D JSON)");
            connection.Execute("INSERT INTO J VALUES (JSON '{\"a\":1}')");
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT D FROM J";
            using DbDataReader reader = command.ExecuteReader();
            AssertColumnMetadata(reader, 0, typeof(TextReader), "JSON");
            Assert.True(reader.Read());
            using TextReader text = Assert.IsAssignableFrom<TextReader>(reader.GetValue(0));
            Assert.Equal("{\"a\":1}", text.ReadToEnd());
        }

        using (H2Connection connection = Open(f => f.JsonFormat = H2JsonFormat.Stream))
        {
            connection.Execute("CREATE TABLE J(D JSON)");
            connection.Execute("INSERT INTO J VALUES (JSON '{\"a\":1}')");
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT D FROM J";
            using (DbDataReader reader = command.ExecuteReader())
            {
                AssertColumnMetadata(reader, 0, typeof(Stream), "JSON");
                Assert.True(reader.Read());
                using Stream stream = Assert.IsAssignableFrom<Stream>(reader.GetValue(0));
                using MemoryStream copy = new();
                stream.CopyTo(copy);
                Assert.Equal("{\"a\":1}"u8.ToArray(), copy.ToArray());
            }

            command.CommandText = "SELECT D FROM J";
            using MemoryStream detached = Assert.IsType<MemoryStream>(command.ExecuteScalar());
            Assert.Equal("{\"a\":1}"u8.ToArray(), detached.ToArray());
        }
    }

    [Fact]
    public void JsonReadsAsAnIndependentDocumentOwnedByTheCaller()
    {
        using H2Connection connection = Open(f => f.JsonFormat = H2JsonFormat.JsonDocument);
        connection.Execute("CREATE TABLE J(ID INT, D JSON)");
        connection.Execute("INSERT INTO J VALUES (1, JSON '{\"a\":1}'), (2, JSON 'null'), (3, NULL)");
        JsonDocument document;
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT D FROM J ORDER BY ID";
            using (DbDataReader reader = command.ExecuteReader())
            {
                AssertColumnMetadata(reader, 0, typeof(JsonDocument), "JSON");
                Assert.True(reader.Read());
                document = Assert.IsType<JsonDocument>(reader.GetValue(0));
                Assert.True(reader.Read());
                using (JsonDocument jsonNull = Assert.IsType<JsonDocument>(reader.GetValue(0)))
                {
                    Assert.Equal(JsonValueKind.Null, jsonNull.RootElement.ValueKind);
                }

                Assert.True(reader.Read());
                Assert.True(reader.IsDBNull(0));
                Assert.Same(DBNull.Value, reader.GetValue(0));
            }
        }

        using (document)
        {
            // The document is a parse of the value, not a view over the reader's row.
            Assert.Equal(1, document.RootElement.GetProperty("a").GetInt32());
            using DbCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO J VALUES (10, $document)";
            DbParameter parameter = insert.CreateParameter();
            parameter.ParameterName = "document";
            parameter.DbType = DbType.Object;
            parameter.Value = document;
            insert.Parameters.Add(parameter);
            Assert.Equal(1, insert.ExecuteNonQuery());
        }

        using (DbCommand check = connection.CreateCommand())
        {
            check.CommandText = "SELECT D FROM J WHERE ID = 10";
            using DbDataReader checkReader = check.ExecuteReader();
            Assert.True(checkReader.Read());
            Assert.Equal("{\"a\":1}"u8.ToArray(), checkReader.GetFieldValue<byte[]>(0));
        }

        using DbCommand scalar = connection.CreateCommand();
        scalar.CommandText = "SELECT D FROM J WHERE ID = 1";
        using JsonDocument detached = Assert.IsType<JsonDocument>(scalar.ExecuteScalar());
        Assert.Equal(1, detached.RootElement.GetProperty("a").GetInt32());
    }

    [Fact]
    public void ArrayReadsAsListExceptForBinaryComponents()
    {
        using H2Connection connection = Open(f =>
        {
            f.ArrayFormat = H2ArrayFormat.List;
            f.JsonFormat = H2JsonFormat.String;
        });
        connection.Execute(
            "CREATE TABLE T(A INTEGER ARRAY, V VARCHAR ARRAY, B BINARY ARRAY, J JSON ARRAY, N INTEGER ARRAY ARRAY)");
        connection.Execute(
            "INSERT INTO T VALUES (ARRAY[1, 2], ARRAY['a', 'b'], ARRAY[X'01', X'02'], "
            + "ARRAY[JSON '{\"a\":1}'], ARRAY[ARRAY[1, 2], ARRAY[3]])");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT A, V, B, J, N FROM T";
        using DbDataReader reader = command.ExecuteReader();
        AssertColumnMetadata(reader, 0, typeof(List<int>), "INTEGER ARRAY");
        AssertColumnMetadata(reader, 1, typeof(List<string>), "CHARACTER VARYING ARRAY");
        AssertColumnMetadata(reader, 2, typeof(byte[][]), "BINARY ARRAY");
        AssertColumnMetadata(reader, 3, typeof(byte[][]), "JSON ARRAY");
        AssertColumnMetadata(reader, 4, typeof(List<int[]>), "INTEGER ARRAY ARRAY");
        Assert.True(reader.Read());
        Assert.Equal(new List<int> { 1, 2 }, Assert.IsType<List<int>>(reader.GetValue(0)));
        Assert.Equal(new List<string> { "a", "b" }, Assert.IsType<List<string>>(reader.GetValue(1)));
        byte[][] binary = Assert.IsType<byte[][]>(reader.GetValue(2));
        Assert.Equal(new byte[] { 1 }, binary[0]);
        Assert.Equal(new byte[] { 2 }, binary[1]);
        byte[][] json = Assert.IsType<byte[][]>(reader.GetValue(3));
        Assert.Equal("{\"a\":1}"u8.ToArray(), json[0]);
        List<int[]> nested = Assert.IsType<List<int[]>>(reader.GetValue(4));
        Assert.Equal(new[] { 1, 2 }, nested[0]);
        Assert.Equal(new[] { 3 }, nested[1]);

        // Explicit array/list getters do not depend on the configured outer shape.
        Assert.Equal(new[] { 1, 2 }, reader.GetFieldValue<int[]>(0));
        Assert.Equal(new List<int> { 1, 2 }, reader.GetFieldValue<List<int>>(0));
        Assert.Equal(new List<string> { "a", "b" }, reader.GetFieldValue<List<string>>(1));
        Assert.Equal(new List<int[]> { new[] { 1, 2 }, new[] { 3 } },
            reader.GetFieldValue<List<int[]>>(4));
    }

    [Fact]
    public void ArrayNullElementsStayNullableAndNullColumnsStayDbNull()
    {
        using H2Connection connection = Open(f => f.ArrayFormat = H2ArrayFormat.List);
        connection.Execute("CREATE TABLE T(ID INT, A INTEGER ARRAY)");
        connection.Execute("INSERT INTO T VALUES (1, ARRAY[1, NULL]), (2, NULL)");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT A FROM T ORDER BY ID";
        using DbDataReader reader = command.ExecuteReader();
        Assert.Equal(typeof(List<int>), reader.GetFieldType(0)); // Declared metadata, as with arrays.
        Assert.True(reader.Read());
        Assert.Equal(new int?[] { 1, null }, Assert.IsType<List<int?>>(reader.GetValue(0)));
        Assert.Equal(new List<int?> { 1, null }, reader.GetFieldValue<List<int?>>(0));
        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(0));
        Assert.Same(DBNull.Value, reader.GetValue(0));
    }

    [Fact]
    public void TemporalShapesAreConfiguredPerType()
    {
        TimeZoneProvider zone = TimeZoneProvider.OfOffset(2 * 60 * 60);
        using H2Connection connection = Open(
            f =>
            {
                f.DateFormat = H2TemporalFormat.DateTimeOffset;
                f.TimeFormat = H2TimeFormat.DateTimeUtc;
                f.TimeTzFormat = H2TemporalFormat.DateTimeUnspecified;
                f.TimestampFormat = H2TemporalFormat.DateTimeOffset;
                f.TimestampTzFormat = H2TemporalFormat.DateTimeUtc;
            },
            zone);
        connection.Execute(
            "CREATE TABLE T(D DATE, TM TIME, TT TIME WITH TIME ZONE, TS TIMESTAMP, TSTZ TIMESTAMP WITH TIME ZONE)");
        connection.Execute(
            "INSERT INTO T VALUES (DATE '2024-03-10', TIME '12:34:56', "
            + "TIME WITH TIME ZONE '12:34:56+03:00', TIMESTAMP '2024-03-10 12:34:56', "
            + "TIMESTAMP WITH TIME ZONE '2024-03-10 12:34:56+03:00')");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT D, TM, TT, TS, TSTZ FROM T";
        using DbDataReader reader = command.ExecuteReader();
        AssertColumnMetadata(reader, 0, typeof(DateTimeOffset), "DATE");
        AssertColumnMetadata(reader, 1, typeof(DateTime), "TIME");
        AssertColumnMetadata(reader, 2, typeof(DateTime), "TIME WITH TIME ZONE");
        AssertColumnMetadata(reader, 3, typeof(DateTimeOffset), "TIMESTAMP");
        AssertColumnMetadata(reader, 4, typeof(DateTime), "TIMESTAMP WITH TIME ZONE");
        Assert.True(reader.Read());

        DateTimeOffset date = Assert.IsType<DateTimeOffset>(reader.GetValue(0));
        Assert.Equal(new DateTime(2024, 3, 10), date.DateTime);
        Assert.Equal(TimeSpan.FromHours(2), date.Offset);

        DateTime time = Assert.IsType<DateTime>(reader.GetValue(1));
        Assert.Equal(DateTimeKind.Utc, time.Kind);
        Assert.Equal(DateTime.MinValue.Date.AddHours(12).AddMinutes(34).AddSeconds(56), time);

        DateTime timeTz = Assert.IsType<DateTime>(reader.GetValue(2));
        Assert.Equal(DateTimeKind.Unspecified, timeTz.Kind);
        Assert.Equal(DateTime.MinValue.Date.AddHours(12).AddMinutes(34).AddSeconds(56), timeTz);

        DateTimeOffset timestamp = Assert.IsType<DateTimeOffset>(reader.GetValue(3));
        Assert.Equal(new DateTime(2024, 3, 10, 12, 34, 56), timestamp.DateTime);
        Assert.Equal(TimeSpan.FromHours(2), timestamp.Offset);

        DateTime timestampTz = Assert.IsType<DateTime>(reader.GetValue(4));
        Assert.Equal(DateTimeKind.Utc, timestampTz.Kind);
        Assert.Equal(new DateTime(2024, 3, 10, 9, 34, 56), timestampTz);
    }

    [Fact]
    public void DateTimeKindForZoneLessValuesIsALabelNotAShift()
    {
        using H2Connection connection = Open(f =>
        {
            f.DateFormat = H2TemporalFormat.DateTimeUtc;
            f.TimestampFormat = H2TemporalFormat.DateTimeLocal;
            f.TimestampTzFormat = H2TemporalFormat.DateTimeUnspecified;
            f.TimeTzFormat = H2TemporalFormat.DateTimeUnspecified;
        });
        connection.Execute(
            "CREATE TABLE T(D DATE, TS TIMESTAMP, TSTZ TIMESTAMP WITH TIME ZONE, TT TIME WITH TIME ZONE)");
        connection.Execute(
            "INSERT INTO T VALUES (DATE '2024-03-10', TIMESTAMP '2024-03-10 12:34:56', "
            + "TIMESTAMP WITH TIME ZONE '2024-03-10 12:34:56+03:00', "
            + "TIME WITH TIME ZONE '12:34:56+03:00')");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT D, TS, TSTZ, TT FROM T";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        DateTime date = Assert.IsType<DateTime>(reader.GetValue(0));
        Assert.Equal(DateTimeKind.Utc, date.Kind);
        Assert.Equal(new DateTime(2024, 3, 10), date);

        DateTime timestamp = Assert.IsType<DateTime>(reader.GetValue(1));
        Assert.Equal(DateTimeKind.Local, timestamp.Kind);
        Assert.Equal(new DateTime(2024, 3, 10, 12, 34, 56), timestamp);

        DateTime timestampTz = Assert.IsType<DateTime>(reader.GetValue(2));
        Assert.Equal(DateTimeKind.Unspecified, timestampTz.Kind);
        Assert.Equal(new DateTime(2024, 3, 10, 12, 34, 56), timestampTz);

        DateTime timeTz = Assert.IsType<DateTime>(reader.GetValue(3));
        Assert.Equal(DateTimeKind.Unspecified, timeTz.Kind);
        Assert.Equal(DateTime.MinValue.Date.AddHours(12).AddMinutes(34).AddSeconds(56), timeTz);
    }

    [Fact]
    public void ZonedTemporalLocalShapeReturnsLocalDateTime()
    {
        using H2Connection connection = Open(f =>
        {
            f.TimeTzFormat = H2TemporalFormat.DateTimeLocal;
            f.TimestampTzFormat = H2TemporalFormat.DateTimeLocal;
        });
        connection.Execute("CREATE TABLE T(TT TIME WITH TIME ZONE, TS TIMESTAMP WITH TIME ZONE)");
        connection.Execute(
            "INSERT INTO T VALUES (TIME WITH TIME ZONE '12:34:56+03:00', "
            + "TIMESTAMP WITH TIME ZONE '2024-03-10 12:34:56+03:00')");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT TT, TS FROM T";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        DateTime timeTz = Assert.IsType<DateTime>(reader.GetValue(0));
        Assert.Equal(DateTimeKind.Local, timeTz.Kind);
        DateTime timestampTz = Assert.IsType<DateTime>(reader.GetValue(1));
        Assert.Equal(DateTimeKind.Local, timestampTz.Kind);
        DateTime expected = new DateTimeOffset(
            2024, 3, 10, 12, 34, 56, TimeSpan.FromHours(3)).LocalDateTime;
        Assert.Equal(expected, timestampTz);
    }

    [Fact]
    public void ExplicitGettersIgnoreTheConfiguredProjection()
    {
        using H2Connection connection = Open(f =>
        {
            f.BlobFormat = H2BlobFormat.Stream;
            f.ClobFormat = H2ClobFormat.TextReader;
            f.JsonFormat = H2JsonFormat.String;
            f.ArrayFormat = H2ArrayFormat.List;
        });
        connection.Execute("CREATE TABLE T(B BLOB, C CLOB, J JSON, A INTEGER ARRAY)");
        connection.Execute("INSERT INTO T VALUES (X'414243', 'hello', JSON '{\"a\":1}', ARRAY[1, 2])");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT B, C, J, A FROM T";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("ABC"u8.ToArray(), reader.GetFieldValue<byte[]>(0));
        Assert.Equal("ABC", reader.GetString(0));
        Assert.Equal("hello"u8.ToArray(), reader.GetFieldValue<byte[]>(1));
        Assert.Equal("hello", reader.GetString(1));
        Assert.Equal("{\"a\":1}"u8.ToArray(), reader.GetFieldValue<byte[]>(2));
        Assert.Equal("{\"a\":1}", reader.GetFieldValue<string>(2));
        using (JsonDocument document = reader.GetFieldValue<JsonDocument>(2))
        {
            Assert.Equal(1, document.RootElement.GetProperty("a").GetInt32());
        }

        using (Stream stream = reader.GetStream(0))
        using (MemoryStream copy = new())
        {
            stream.CopyTo(copy);
            Assert.Equal("ABC"u8.ToArray(), copy.ToArray());
        }

        using (TextReader text = reader.GetTextReader(0))
        {
            Assert.Equal("ABC", text.ReadToEnd());
        }

        using (Stream stream = reader.GetStream(1))
        using (MemoryStream copy = new())
        {
            stream.CopyTo(copy);
            Assert.Equal("hello"u8.ToArray(), copy.ToArray());
        }

        using (TextReader text = reader.GetTextReader(1))
        {
            Assert.Equal("hello", text.ReadToEnd());
        }

        using (Stream stream = reader.GetStream(2))
        using (MemoryStream copy = new())
        {
            stream.CopyTo(copy);
            Assert.Equal("{\"a\":1}"u8.ToArray(), copy.ToArray());
        }

        using (TextReader text = reader.GetTextReader(2))
        {
            Assert.Equal("{\"a\":1}", text.ReadToEnd());
        }

        Assert.Equal(new[] { 1, 2 }, reader.GetFieldValue<int[]>(3));
        Assert.Equal(new List<int> { 1, 2 }, reader.GetFieldValue<List<int>>(3));
    }

    [Fact]
    public void SettingsArePerConnectionAndRequireAClosedConnection()
    {
        string name = "format_scope_" + Guid.NewGuid().ToString("N");
        using H2DataSource dataSource = new H2DataSourceBuilder("Data Source=" + name + ";Storage=Memory")
            .ConfigureDataFormat(f =>
            {
                f.BlobFormat = H2BlobFormat.Stream;
                f.JsonFormat = H2JsonFormat.String;
            })
            .Build();
        using H2Connection first = (H2Connection)dataSource.CreateConnection();
        Assert.Equal(H2BlobFormat.Stream, ((IH2DataFormat)first).BlobFormat);
        Assert.Equal(H2JsonFormat.String, ((IH2DataFormat)first).JsonFormat);
        first.Open();
        first.Execute("CREATE TABLE T(B BLOB, J JSON)");
        first.Execute("INSERT INTO T VALUES (X'414243', JSON '{\"a\":1}')");

        using H2Connection second = (H2Connection)dataSource.CreateConnection();
        ((IH2DataFormat)second).BlobFormat = H2BlobFormat.ByteArray;
        ((IH2DataFormat)second).JsonFormat = H2JsonFormat.ByteArray;
        second.Open();
        using (DbCommand command = second.CreateCommand())
        {
            command.CommandText = "SELECT B, J FROM T";
            using DbDataReader reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.IsType<byte[]>(reader.GetValue(0));
            Assert.IsType<byte[]>(reader.GetValue(1));
        }

        using (DbCommand command = second.CreateCommand())
        {
            command.CommandText = "SELECT B, J FROM T";
            Assert.Throws<InvalidOperationException>(
                () => ((IH2DataFormat)second).BlobFormat = H2BlobFormat.Stream);
            using DbDataReader reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.IsType<byte[]>(reader.GetValue(0));
            Assert.IsType<byte[]>(reader.GetValue(1));
        }

        using (DbCommand command = first.CreateCommand())
        {
            command.CommandText = "SELECT B, J FROM T";
            using DbDataReader reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.IsAssignableFrom<Stream>(reader.GetValue(0));
            Assert.IsType<string>(reader.GetValue(1));
        }

        first.Close();
        ((IH2DataFormat)first).BlobFormat = H2BlobFormat.ByteArray;
        first.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => ((IH2DataFormat)first).BlobFormat = H2BlobFormat.Stream);
    }

    private static H2Connection Open(
        Action<IH2DataFormat>? configure = null,
        TimeZoneProvider? timeZone = null)
    {
        H2Connection connection = new(
            "Data Source=format_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        if (timeZone is not null)
        {
            ((IH2Test)connection).TimeZoneProviderProvider = () => timeZone;
        }

        configure?.Invoke((IH2DataFormat)connection);
        connection.Open();
        return connection;
    }

    private static void AssertColumnMetadata(
        DbDataReader reader, int ordinal, Type dataType, string dataTypeName)
    {
        Assert.Equal(dataType, reader.GetFieldType(ordinal));
        Assert.Equal(dataTypeName, reader.GetDataTypeName(ordinal));
        Assert.Equal(dataType, reader.GetSchemaTable()!.Rows[ordinal]["DataType"]);
        Assert.Equal(dataType, reader.GetColumnSchemaAsync().GetAwaiter().GetResult()[ordinal].DataType);
    }
}
