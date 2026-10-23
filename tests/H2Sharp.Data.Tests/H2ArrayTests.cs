/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using Dapper;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2ArrayTests
{
    [Fact]
    public void ParametersAcceptArraysListsAndSinglePassEnumerables()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(ID INT, A INTEGER ARRAY[5])");
        object[] inputs = [new[] { 1, 2 }, new List<int> { 3, 4 }, new SinglePassEnumerable(5, 6)];
        for (int i = 0; i < inputs.Length; i++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO T VALUES (@id, @a)";
            Add(command, "id", i);
            Add(command, "a", inputs[i]);
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        using DbCommand read = connection.CreateCommand();
        read.CommandText = "SELECT A FROM T ORDER BY ID";
        using DbDataReader reader = read.ExecuteReader();
        Assert.Equal(typeof(int[]), reader.GetFieldType(0));
        Assert.Equal("ARRAY", reader.GetDataTypeName(0));
        for (int i = 0; i < inputs.Length; i++)
        {
            Assert.True(reader.Read());
            int[] expected = [i * 2 + 1, i * 2 + 2];
            Assert.Equal(expected, Assert.IsType<int[]>(reader.GetValue(0)));
            Assert.Equal(expected, reader.GetFieldValue<int[]>(0));
            Assert.Equal(expected.Select(x => (long)x), reader.GetFieldValue<long[]>(0));
            object[] values = new object[1];
            Assert.Equal(1, reader.GetValues(values));
            Assert.Equal(expected, Assert.IsType<int[]>(values[0]));
        }

        Assert.False(reader.Read());
    }

    [Fact]
    public void NullElementsEmptyArraysAndNullColumnsRemainDistinct()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(ID INT, A INTEGER ARRAY)");
        object[] inputs = [new int?[] { 1, null, 3 }, Array.Empty<int>(), DBNull.Value, new int?[] { null, null }];
        for (int i = 0; i < inputs.Length; i++)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO T VALUES (@id, @a)";
            Add(command, "id", i);
            Add(command, "a", inputs[i]);
            command.ExecuteNonQuery();
        }

        using DbCommand read = connection.CreateCommand();
        read.CommandText = "SELECT A FROM T ORDER BY ID";
        using DbDataReader reader = read.ExecuteReader();
        Assert.Equal(typeof(int[]), reader.GetFieldType(0)); // Metadata is independent of row NULLs.
        Assert.True(reader.Read());
        Assert.False(reader.IsDBNull(0));
        Assert.Equal(new int?[] { 1, null, 3 }, Assert.IsType<int?[]>(reader.GetValue(0)));
        Assert.Equal(new int?[] { 1, null, 3 }, reader.GetFieldValue<int?[]>(0));
        Assert.Throws<InvalidCastException>(() => reader.GetFieldValue<int[]>(0));
        Assert.Equal(new object[] { 1, DBNull.Value, 3 }, reader.GetFieldValue<object[]>(0));
        Assert.True(reader.Read());
        Assert.Empty(Assert.IsType<int[]>(reader.GetValue(0)));
        Assert.Empty(reader.GetFieldValue<int?[]>(0));
        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(0));
        Assert.Same(DBNull.Value, reader.GetValue(0));
        Assert.True(reader.Read());
        Assert.Equal(new int?[] { null, null }, Assert.IsType<int?[]>(reader.GetValue(0)));
    }

    [Fact]
    public void EmptyParameterRetainsItsClrElementType()
    {
        using H2Connection connection = Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT @a";
        Add(command, "a", new List<Guid>());
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(typeof(Guid[]), reader.GetFieldType(0));
        Assert.Empty(Assert.IsType<Guid[]>(reader.GetValue(0)));
    }

    [Fact]
    public void JaggedArraysAndBinaryArrayElementsRoundTrip()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(A INTEGER ARRAY ARRAY, B VARBINARY ARRAY, C VARCHAR ARRAY)");
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO T VALUES (@a, @b, @c)";
            Add(command, "a", new[] { new[] { 1, 2 }, Array.Empty<int>(), new[] { 3 } });
            Add(command, "b", new byte[][] { [0, 128, 255], [] });
            Add(command, "c", new string?[] { "O'Brien", null, "" });
            command.ExecuteNonQuery();
        }

        using DbCommand read = connection.CreateCommand();
        read.CommandText = "SELECT A, B, C FROM T";
        using DbDataReader reader = read.ExecuteReader();
        Assert.True(reader.Read());
        int[][] nested = Assert.IsType<int[][]>(reader.GetValue(0));
        Assert.Equal(new[] { 1, 2 }, nested[0]);
        Assert.Empty(nested[1]);
        Assert.Equal(new[] { 3 }, nested[2]);
        byte[][] binary = reader.GetFieldValue<byte[][]>(1);
        Assert.Equal(new byte[] { 0, 128, 255 }, binary[0]);
        Assert.Empty(binary[1]);
        Assert.Equal(new string?[] { "O'Brien", null, "" }, reader.GetFieldValue<string[]>(2));
        // Returned arrays are detached from the stored ValueArray.
        nested[0][0] = 99;
        Assert.Equal(1, reader.GetFieldValue<int[][]>(0)[0][0]);
    }

    [Fact]
    public void BinaryAndTextParametersKeepTheirScalarMappings()
    {
        using H2Connection connection = Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT @bytes, @text";
        Add(command, "bytes", new byte[] { 1, 2, 255 });
        Add(command, "text", "abc");
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("BINARY VARYING", reader.GetDataTypeName(0));
        Assert.Equal(new byte[] { 1, 2, 255 }, reader.GetFieldValue<byte[]>(0));
        Assert.Equal("abc", reader.GetString(1));
    }

    [Fact]
    public void DapperWritesOneArrayParameterAndReadsTypedProperties()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, NUMBERS INTEGER ARRAY, TAGS VARCHAR ARRAY)");
        DynamicParameters parameters = new();
        parameters.Add("id", 1);
        // Explicit DbType.Object selects a single native array rather than Dapper's IN-list expansion.
        parameters.Add("numbers", new List<int> { 10, 20 }, DbType.Object);
        parameters.Add("tags", new[] { "a", "b" }, DbType.Object);
        Assert.Equal(1, connection.Execute("INSERT INTO T VALUES (@id, @numbers, @tags)", parameters));
        ArrayRow row = connection.QuerySingle<ArrayRow>("SELECT ID, NUMBERS, TAGS FROM T");
        Assert.Equal(1, row.Id);
        Assert.Equal(new[] { 10, 20 }, row.Numbers);
        Assert.Equal(new[] { "a", "b" }, row.Tags);
        Assert.Equal(new[] { 10, 20 }, connection.QuerySingle<int[]>("SELECT NUMBERS FROM T"));

        parameters = new DynamicParameters();
        parameters.Add("numbers", new SinglePassEnumerable(30, 40, 50), DbType.Object);
        Assert.Equal(1, connection.Execute("UPDATE T SET NUMBERS = @numbers", parameters));
        Assert.Equal(new[] { 30, 40, 50 }, connection.QuerySingle<ArrayRow>("SELECT ID, NUMBERS, TAGS FROM T").Numbers);
    }

    [Fact]
    public void DapperInListsStillExpandIncludingTheEmptyList()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(ID INT)");
        connection.Execute("INSERT INTO T VALUES (1), (2), (3)");
        Assert.Equal(new[] { 1, 3 }, connection.Query<int>("SELECT ID FROM T WHERE ID IN @ids ORDER BY ID",
            new { ids = new[] { 1, 3 } }));
        Assert.Empty(connection.Query<int>("SELECT ID FROM T WHERE ID IN @ids", new { ids = Array.Empty<int>() }));
    }

    [Fact]
    public void ArrayLiteralAndAggregateReadThroughTheProvider()
    {
        using H2Connection connection = Open();
        Assert.Equal(new[] { 1, 2, 3 }, connection.QuerySingle<int[]>("SELECT ARRAY[1, 2, 3]"));
        Assert.Equal(new int?[] { 1, null, 3 }, connection.QuerySingle<int?[]>("SELECT ARRAY[1, NULL, 3]"));
        Assert.Equal(new long[] { 1, 2, 3 }, connection.QuerySingle<long[]>("SELECT ARRAY_AGG(X) FROM SYSTEM_RANGE(1, 3)"));
    }

    [Fact]
    public void ParameterConversionAndCardinalityErrorsLeaveTheTableUnchanged()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(A INTEGER ARRAY[2])");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO T VALUES (@a)";
        Add(command, "a", new[] { 1, 2, 3 });
        Assert.Equal(22001, Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery()).H2ErrorCode);
        command.Parameters[0].Value = new[] { "bad" };
        Assert.Equal(22018, Assert.Throws<H2SharpException>(() => command.ExecuteNonQuery()).H2ErrorCode);
        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM T"));
        command.Parameters[0].Value = new[] { 1, 2 };
        Assert.Equal(1, command.ExecuteNonQuery());
        Assert.Equal(new[] { 1, 2 }, connection.QuerySingle<int[]>("SELECT A FROM T"));
    }

    [Fact]
    public void NullableArraysCanBeReadByDapperAcrossDifferentRows()
    {
        SqlMapper.AddTypeMap(typeof(int?[]), DbType.Object, useGetFieldValue: true);
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(ID INT, NUMBERS INTEGER ARRAY, TAGS VARCHAR ARRAY)");
        connection.Execute("INSERT INTO T VALUES (1, ARRAY[1, 2], ARRAY['a']), (2, ARRAY[3, NULL], NULL), (3, NULL, NULL)");
        List<NullableArrayRow> rows = connection.Query<NullableArrayRow>("SELECT ID, NUMBERS, TAGS FROM T ORDER BY ID").AsList();
        Assert.Equal(new int?[] { 1, 2 }, rows[0].Numbers);
        Assert.Equal(new int?[] { 3, null }, rows[1].Numbers);
        Assert.Null(rows[2].Numbers);
        Assert.Equal(1, connection.Execute("INSERT INTO T(ID, NUMBERS) VALUES (@id, @numbers)",
            new { id = 4, numbers = new int?[] { 4, null } }));
        Assert.Equal(new int?[] { 4, null }, connection.QuerySingle<NullableArrayRow>(
            "SELECT ID, NUMBERS, TAGS FROM T WHERE ID = 4").Numbers);
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("jdbc:h2:mem:array_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        return connection;
    }

    private static void Add(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed class ArrayRow
    {
        public int Id { get; set; }
        public int[]? Numbers { get; set; }
        public string[]? Tags { get; set; }
    }

    private sealed class NullableArrayRow
    {
        public int Id { get; set; }
        public int?[]? Numbers { get; set; }
        public string[]? Tags { get; set; }
    }

    private sealed class SinglePassEnumerable(params int[] values) : IEnumerable<int>
    {
        private bool enumerated;
        public IEnumerator<int> GetEnumerator()
        {
            Assert.False(enumerated, "A lazy parameter must be enumerated exactly once per execution");
            enumerated = true;
            return ((IEnumerable<int>)values).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
