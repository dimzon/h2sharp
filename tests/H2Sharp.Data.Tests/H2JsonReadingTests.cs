/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Text;
using System.Text.Json;
using Dapper;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2JsonReadingTests
{
    [Fact]
    public void JsonDocumentReadsAsBytesWithExplicitTextGetters()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE J(D JSON)");
        byte[] expected = Encoding.UTF8.GetBytes("{\"текст\":\"привет 🎉\"}");
        connection.Execute("INSERT INTO J VALUES ($document)", new { document = expected });
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT D FROM J";
        using DbDataReader reader = command.ExecuteReader();
        Assert.Equal("JSON", reader.GetDataTypeName(0));
        Assert.Equal(typeof(byte[]), reader.GetFieldType(0));
        Assert.Equal(typeof(byte[]), reader.GetSchemaTable()!.Rows[0]["DataType"]);
        Assert.Equal(typeof(byte[]), reader.GetColumnSchemaAsync().GetAwaiter().GetResult()[0].DataType);
        Assert.True(reader.Read());
        Assert.Equal(expected, Assert.IsType<byte[]>(reader.GetValue(0)));
        Assert.Equal(expected, reader.GetFieldValue<byte[]>(0));
        Assert.Equal(Encoding.UTF8.GetString(expected), reader.GetString(0));
        Assert.Equal(reader.GetString(0), reader.GetFieldValue<string>(0));
        using TextReader text = reader.GetTextReader(0);
        Assert.Equal(reader.GetString(0), text.ReadToEnd());
        using Stream stream = reader.GetStream(0);
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        Assert.Equal(expected, copy.ToArray());

        byte[] slice = new byte[6];
        Assert.Equal(expected.Length, reader.GetBytes(0, 0, null, 0, 0));
        Assert.Equal(4, reader.GetBytes(0, 2, slice, 1, 4));
        Assert.Equal(expected.AsSpan(2, 4).ToArray(), slice.AsSpan(1, 4).ToArray());
        byte[] exposed = (byte[])reader.GetValue(0);
        exposed[0] = 0;
        object[] values = new object[1];
        Assert.Equal(1, reader.GetValues(values));
        Assert.Equal(expected, Assert.IsType<byte[]>(values[0]));
        Assert.Equal(expected, reader.GetFieldValue<byte[]>(0));
    }

    [Fact]
    public void ByteRoundTripPreservesJsonWhileStringParametersRemainJsonStrings()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE J(ID INT PRIMARY KEY, D JSON)");
        const string Document = "{\"a\": 1}";
        connection.Execute("INSERT INTO J VALUES (1, $document)", new { document = Encoding.UTF8.GetBytes(Document) });
        byte[] value = connection.QuerySingle<byte[]>("SELECT D FROM J WHERE ID = 1");
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(value));
        connection.Execute("INSERT INTO J VALUES (2, $document)", new { document = value });
        Assert.Equal(value, connection.QuerySingle<byte[]>("SELECT D FROM J WHERE ID = 2"));
        Assert.Equal(value, Assert.IsType<byte[]>(connection.ExecuteScalar("SELECT D FROM J WHERE ID = 1")));

        connection.Execute("INSERT INTO J VALUES (3, $document)", new { document = Document });
        Assert.Equal("\"{\\\"a\\\": 1}\"", Encoding.UTF8.GetString(connection.QuerySingle<byte[]>("SELECT D FROM J WHERE ID = 3")));
        // CAST to VARCHAR exposes text for callers that need ordinary Dapper string mapping.
        Assert.Equal("{\"a\":1}", connection.QuerySingle<string>("SELECT CAST(D AS VARCHAR) FROM J WHERE ID = 1"));
    }

    [Fact]
    public void JsonNullAndSqlNullStayDistinctIncludingArrayElements()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE J(ID INT PRIMARY KEY, D JSON)");
        connection.Execute("INSERT INTO J VALUES (1, JSON 'null'), (2, NULL)");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT D FROM J ORDER BY ID";
        using (DbDataReader reader = command.ExecuteReader())
        {
            Assert.True(reader.Read());
            Assert.False(reader.IsDBNull(0));
            Assert.Equal("null", reader.GetFieldValue<string>(0));
            Assert.Equal("null"u8.ToArray(), reader.GetFieldValue<byte[]>(0));
            Assert.True(reader.Read());
            Assert.True(reader.IsDBNull(0));
            Assert.Same(DBNull.Value, reader.GetValue(0));
        }

        command.CommandText = "SELECT ARRAY[JSON '{\"a\":1}', JSON 'null', CAST(NULL AS JSON)]";
        using DbDataReader arrayReader = command.ExecuteReader();
        Assert.Equal(typeof(byte[][]), arrayReader.GetFieldType(0));
        Assert.True(arrayReader.Read());
        byte[][] array = arrayReader.GetFieldValue<byte[][]>(0);
        Assert.Equal("{\"a\":1}"u8.ToArray(), array[0]);
        Assert.Equal("null"u8.ToArray(), array[1]);
        Assert.Null(array[2]);
    }

    [Fact]
    public void SystemTextJsonValuesAreDocumentsAndAreCopiedDuringExecution()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE J(ID INT PRIMARY KEY, D JSON)");
        using DbCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO J VALUES ($id, $document)";
        DbParameter id = insert.CreateParameter();
        id.ParameterName = "id";
        insert.Parameters.Add(id);
        DbParameter json = insert.CreateParameter();
        json.ParameterName = "document";
        json.DbType = DbType.Object;
        insert.Parameters.Add(json);
        using (JsonDocument document = JsonDocument.Parse("{\"a\": 1}"))
        {
            id.Value = 1;
            json.Value = document;
            Assert.Equal(1, insert.ExecuteNonQuery());
            // Mapping never disposes the caller's document.
            Assert.Equal(1, document.RootElement.GetProperty("a").GetInt32());
            id.Value = 2;
            json.Value = document.RootElement.GetProperty("a");
            Assert.Equal(1, insert.ExecuteNonQuery());
        }

        Assert.Equal("{\"a\":1}"u8.ToArray(), connection.QuerySingle<byte[]>("SELECT D FROM J WHERE ID = 1"));
        Assert.Equal("1"u8.ToArray(), connection.QuerySingle<byte[]>("SELECT D FROM J WHERE ID = 2"));
        using JsonDocument nullDocument = JsonDocument.Parse("null");
        id.Value = 3;
        json.Value = nullDocument.RootElement;
        insert.ExecuteNonQuery();
        id.Value = 4;
        json.Value = DBNull.Value;
        insert.ExecuteNonQuery();
        Assert.Equal("null"u8.ToArray(), connection.QuerySingle<byte[]>("SELECT D FROM J WHERE ID = 3"));
        // Dapper maps a scalar DBNull to null; the ADO.NET reader keeps DBNull.
        Assert.Null(connection.ExecuteScalar("SELECT D FROM J WHERE ID = 4"));

        // Explicit DbType.Object lets Dapper pass STJ types as a single value.
        DynamicParameters parameters = new();
        using JsonDocument arrayDocument = JsonDocument.Parse("[1, null, true]");
        parameters.Add("document", arrayDocument, DbType.Object);
        connection.Execute("INSERT INTO J VALUES (5, $document)", parameters);
        parameters = new DynamicParameters();
        parameters.Add("document", arrayDocument.RootElement, DbType.Object);
        connection.Execute("INSERT INTO J VALUES (6, $document)", parameters);
        Assert.Equal("[1,null,true]"u8.ToArray(), connection.QuerySingle<byte[]>("SELECT D FROM J WHERE ID = 5"));
        Assert.Equal("[1,null,true]"u8.ToArray(), connection.QuerySingle<byte[]>("SELECT D FROM J WHERE ID = 6"));
    }

    [Fact]
    public void SystemTextJsonArraysPreserveJsonElementsAndEmptyComponentType()
    {
        using H2Connection connection = Open();
        using JsonDocument document = JsonDocument.Parse("[{\"a\":1}, null]");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ?";
        DbParameter parameter = command.CreateParameter();
        parameter.DbType = DbType.Object;
        parameter.Value = new[] { document.RootElement[0], document.RootElement[1] };
        command.Parameters.Add(parameter);
        using (DbDataReader reader = command.ExecuteReader())
        {
            Assert.Equal(typeof(byte[][]), reader.GetFieldType(0));
            Assert.True(reader.Read());
            byte[][] values = reader.GetFieldValue<byte[][]>(0);
            Assert.Equal("{\"a\":1}"u8.ToArray(), values[0]);
            Assert.Equal("null"u8.ToArray(), values[1]);
        }
        parameter.Value = Array.Empty<JsonElement>();
        using DbDataReader emptyReader = command.ExecuteReader();
        Assert.Equal(typeof(byte[][]), emptyReader.GetFieldType(0));
        Assert.True(emptyReader.Read());
        Assert.Empty(emptyReader.GetFieldValue<byte[][]>(0));
    }

    [Fact]
    public void UndefinedOrDisposedSystemTextJsonInputsDoNotSilentlyBecomeNull()
    {
        using H2Connection connection = Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ?";
        DbParameter parameter = command.CreateParameter();
        parameter.Value = default(JsonElement);
        command.Parameters.Add(parameter);
        Assert.Throws<InvalidOperationException>(() => command.ExecuteScalar());
        JsonDocument disposed = JsonDocument.Parse("{}");
        JsonElement element = disposed.RootElement;
        disposed.Dispose();
        parameter.Value = disposed;
        Assert.Throws<ObjectDisposedException>(() => command.ExecuteScalar());
        parameter.Value = element;
        Assert.Throws<ObjectDisposedException>(() => command.ExecuteScalar());
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("jdbc:h2:mem:json_reading_ado_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        return connection;
    }
}
