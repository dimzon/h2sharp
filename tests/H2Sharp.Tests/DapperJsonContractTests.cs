/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * Each test owns its Dapper load context. Native ADO.NET getter behavior is pinned against
 * the H2 jar independently of Dapper's explicitly configured typed projections.
 */
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Dapper;
using H2Sharp.Data;
using H2Sharp.TestInfrastructure;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DapperJsonContractTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ObjectKeyValue", "SELECT JSON_OBJECT(KEY 'id' VALUE 1)", "{\"id\":1}"];
        yield return ["ObjectColon", "SELECT JSON_OBJECT('id': 1)", "{\"id\":1}"];
        yield return ["ObjectAbsent", "SELECT JSON_OBJECT(KEY 'a' VALUE NULL ABSENT ON NULL)", "{}"];
        yield return ["ObjectUnique", "SELECT JSON_OBJECT(KEY 'a' VALUE 1 WITH UNIQUE KEYS)", "{\"a\":1}"];
        yield return ["ObjectEmpty", "SELECT JSON_OBJECT()", "{}"];
        yield return ["ArrayValues", "SELECT JSON_ARRAY(1, 2)", "[1,2]"];
        yield return ["ArrayQuery", "SELECT JSON_ARRAY((SELECT JSON_OBJECT(KEY 'a' VALUE 1) FROM SYSTEM_RANGE(1, 1)) FORMAT JSON)", "[{\"a\":1}]"];
        yield return ["ArrayAbsent", "SELECT JSON_ARRAY(1, NULL ABSENT ON NULL)", "[1]"];
        yield return ["ArrayEmpty", "SELECT JSON_ARRAY()", "[]"];
        yield return ["Unicode", "SELECT JSON_OBJECT('текст': 'привет 🎉')", "{\"текст\":\"привет 🎉\"}"];
        yield return ["JsonNull", "SELECT JSON 'null'", "null"];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void AllGetterVariantsMatchTheJar(string name, string sql, string expected)
    {
        _ = name;
        using DapperJsonFixture fixture = new(Open());
        using DbDataReader reader = fixture.ExecuteReader(sql);
        Assert.Equal("JSON", reader.GetDataTypeName(0));
        Assert.Equal(typeof(byte[]), reader.GetFieldType(0));
        Assert.True(reader.Read());

        byte[] bytes = Encoding.UTF8.GetBytes(expected);
        string text = ReadField<string>(reader);
        byte[] typedBytes = ReadField<byte[]>(reader);
        byte[] typedObject = Assert.IsType<byte[]>(ReadField<object>(reader));
        byte[] raw = Assert.IsType<byte[]>(reader.GetValue(0));
        Assert.Equal(expected, text);
        Assert.Equal(bytes, typedBytes);
        Assert.Equal(bytes, typedObject);
        Assert.Equal(bytes, raw);

        List<string> actual =
        [
            "TYPE:JSON:BYTES",
            "STRING:" + OracleRunner.Hex(text),
            "BYTES:" + Convert.ToHexString(typedBytes),
            "OBJECT:" + Convert.ToHexString(typedObject),
            "RAW:" + Convert.ToHexString(raw),
        ];
        List<string> java = OracleRunner.Run([sql], """
            try (java.sql.Connection c = java.sql.DriverManager.getConnection("jdbc:h2:mem:dapper_json_getters")) {
              try (java.sql.ResultSet r = c.createStatement().executeQuery(inputs[0])) {
                java.sql.ResultSetMetaData m = r.getMetaData();
                System.out.println("TYPE:" + m.getColumnTypeName(1) + ":" + (m.getColumnClassName(1).equals("[B") ? "BYTES" : m.getColumnClassName(1)));
                r.next();
                String text = r.getObject(1, String.class);
                if (!text.equals(r.getString(1))) throw new AssertionError("Typed and untyped string getters differ");
                byte[] typed = r.getObject(1, byte[].class);
                if (!java.util.Arrays.equals(typed, r.getBytes(1))) throw new AssertionError("Byte getters differ");
                System.out.println("STRING:" + hex(text));
                System.out.println("BYTES:" + java.util.HexFormat.of().withUpperCase().formatHex(typed));
                // .NET GetFieldValue<object> defaults to the native GetValue, whose JDBC
                // counterpart is getObject(column), not Java's serialization Object.class path.
                System.out.println("OBJECT:" + java.util.HexFormat.of().withUpperCase().formatHex((byte[])r.getObject(1)));
                System.out.println("RAW:" + java.util.HexFormat.of().withUpperCase().formatHex((byte[])r.getObject(1)));
              }
            }
            """, extraJavaArgs: ["-Duser.language=en"]);
        Assert.Equal(java, actual);

        // Every byte[] getter returns data the caller may mutate without changing the value.
        typedBytes[0] = typedObject[0] = raw[0] = 0;
        Assert.Equal(expected, ReadField<string>(reader));
        Assert.Equal(bytes, ReadField<byte[]>(reader));
        Assert.Equal(bytes, Assert.IsType<byte[]>(ReadField<object>(reader)));
        Assert.Equal(bytes, Assert.IsType<byte[]>(reader.GetValue(0)));
        Assert.False(reader.Read());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TypedDapperProjectionsKeepTheNativeObjectContract(string name, string sql, string expected)
    {
        _ = name;
        object before = DefaultTypeMap();
        byte[] bytes = Encoding.UTF8.GetBytes(expected);
        using (DapperJsonFixture fixture = new(Open()))
        {
            Assert.NotSame(typeof(SqlMapper).Assembly, fixture.MapperAssembly);
            Assert.Equal(expected, fixture.QuerySingle<string>(sql));
            Assert.Equal(bytes, fixture.QuerySingle<byte[]>(sql));
            Assert.Equal(bytes, Assert.IsType<byte[]>(fixture.ExecuteScalar<object>(sql)));

            using DbCommand direct = ((DbConnection)fixture.Connection).CreateCommand();
            direct.CommandText = sql;
            Assert.Equal(bytes, Assert.IsType<byte[]>(direct.ExecuteScalar()));
            Assert.Same(before, DefaultTypeMap());
        }
        Assert.Same(before, DefaultTypeMap());

        // The unchanged default Dapper and provider still expose the native byte[] contract.
        using H2Connection original = Open();
        Assert.Equal(bytes, original.QuerySingle<byte[]>(sql));
        Assert.Equal(bytes, Assert.IsType<byte[]>(original.ExecuteScalar<object>(sql)));
        Assert.Throws<System.Data.DataException>(() => original.QuerySingle<string>(sql));
    }

    [Fact]
    public void EachFixtureHasItsOwnDapperAndCannotAlterOtherTests()
    {
        const string sql = "SELECT JSON_OBJECT('a':1)";
        object before = DefaultTypeMap();
        using H2Connection original = Open();
        using DapperJsonFixture second = new(Open());
        using (DapperJsonFixture first = new(Open()))
        {
            Assert.NotSame(first.MapperAssembly, second.MapperAssembly);
            Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(first.MapperAssembly));
            Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(second.MapperAssembly));
            Assert.Equal("{\"a\":1}", first.QuerySingle<string>(sql));
            Assert.Equal("{\"a\":1}", second.QuerySingle<string>(sql));
            Assert.Throws<System.Data.DataException>(() => original.QuerySingle<string>(sql));
            Assert.Same(before, DefaultTypeMap());
        }
        Assert.Equal("{\"a\":1}", second.QuerySingle<string>(sql));
        Assert.Throws<System.Data.DataException>(() => original.QuerySingle<string>(sql));
        Assert.Same(before, DefaultTypeMap());
    }

    [Fact]
    public void SqlNullAndJsonNullStayDifferentForObjectAndTypedDapper()
    {
        using DapperJsonFixture fixture = new(Open());
        using (DbDataReader reader = fixture.ExecuteReader("SELECT CAST(NULL AS JSON), JSON 'null'"))
        {
            Assert.True(reader.Read());
            Assert.True(reader.IsDBNull(0));
            Assert.Same(DBNull.Value, reader.GetValue(0));
            Assert.Same(DBNull.Value, reader.GetFieldValue<object>(0));
            Assert.False(reader.IsDBNull(1));
            Assert.Equal("null", reader.GetFieldValue<string>(1));
            Assert.Equal("null"u8.ToArray(), reader.GetFieldValue<byte[]>(1));
            Assert.Equal("null"u8.ToArray(), Assert.IsType<byte[]>(reader.GetFieldValue<object>(1)));
            Assert.Equal("null"u8.ToArray(), Assert.IsType<byte[]>(reader.GetValue(1)));
        }
        Assert.Null(fixture.ExecuteScalar<object?>("SELECT CAST(NULL AS JSON)"));
        Assert.Null(fixture.QuerySingle<string?>("SELECT CAST(NULL AS JSON)"));
        Assert.Equal("null"u8.ToArray(), Assert.IsType<byte[]>(fixture.ExecuteScalar<object>("SELECT JSON 'null'")));
    }

    private static T ReadField<T>(DbDataReader reader) => reader.GetFieldValue<T>(0);

    private static object DefaultTypeMap()
        => typeof(SqlMapper).GetField("typeMap", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static H2Connection Open()
    {
        H2Connection connection = new("Data Source=dapper_json_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        return connection;
    }
}
