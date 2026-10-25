// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions.html — JSON functions, one test per checklist row.
using System.Data;
using Dapper;
using H2Sharp.TestInfrastructure;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class JsonFunctionTests
{
    /// <summary>docs/functions.html:4133 — JSON_OBJECT(</summary>
    [Fact]
    public void JsonObject_WithKeyValue() // FUN-4133
    {
        using var connection = new DapperJsonFixture(TestDatabase.Open());

        Assert.Equal("{\"id\":1}", connection.QuerySingle<string>("SELECT JSON_OBJECT(KEY 'id' VALUE 1)"));
    }

    /// <summary>docs/functions.html:4134 — [{{[KEY] string VALUE expression} | {string : expression}} [,...] ]</summary>
    [Fact]
    public void JsonObject_WithColonForm() // FUN-4134
    {
        using var connection = new DapperJsonFixture(TestDatabase.Open());

        Assert.Equal("{\"id\":1}", connection.QuerySingle<string>("SELECT JSON_OBJECT('id': 1)"));
    }

    /// <summary>docs/functions.html:4135 — [ { NULL | ABSENT } ON NULL ]</summary>
    [Fact]
    public void JsonObject_AbsentOnNull_DropsTheKey() // FUN-4135
    {
        using var connection = new DapperJsonFixture(TestDatabase.Open());

        Assert.Equal("{}", connection.QuerySingle<string>(
            "SELECT JSON_OBJECT(KEY 'a' VALUE NULL ABSENT ON NULL)"));
    }

    /// <summary>docs/functions.html:4136 — [ { WITH | WITHOUT } UNIQUE KEYS ]</summary>
    [Fact]
    public void JsonObject_WithUniqueKeys() // FUN-4136
    {
        using var connection = new DapperJsonFixture(TestDatabase.Open());

        Assert.Equal("{\"a\":1}", connection.QuerySingle<string>(
            "SELECT JSON_OBJECT(KEY 'a' VALUE 1 WITH UNIQUE KEYS)"));
    }

    /// <summary>docs/functions.html:4137 — )</summary>
    [Fact]
    public void JsonObject_WithoutArguments_IsEmpty() // FUN-4137
    {
        using var connection = new DapperJsonFixture(TestDatabase.Open());

        Assert.Equal("{}", connection.QuerySingle<string>("SELECT JSON_OBJECT()"));
    }

    /// <summary>docs/functions.html:4159 — JSON_ARRAY(</summary>
    [Fact]
    public void JsonArray_WithExpressions() // FUN-4159
    {
        using var connection = new DapperJsonFixture(TestDatabase.Open());

        Assert.Equal("[1,2]", connection.QuerySingle<string>("SELECT JSON_ARRAY(1, 2)"));
    }

    /// <summary>docs/functions.html:4160 — [expression [,...]]|{(query) [FORMAT JSON]}</summary>
    [Fact]
    public void JsonArray_WithQuery() // FUN-4160
    {
        using var connection = new DapperJsonFixture(TestDatabase.Open());

        Assert.Equal("[{\"a\":1}]", connection.QuerySingle<string>(
            "SELECT JSON_ARRAY((SELECT JSON_OBJECT(KEY 'a' VALUE 1) FROM SYSTEM_RANGE(1, 1)) FORMAT JSON)"));
    }

    /// <summary>docs/functions.html:4161 — [ { NULL | ABSENT } ON NULL ]</summary>
    [Fact]
    public void JsonArray_AbsentOnNull_DropsTheElement() // FUN-4161
    {
        using var connection = new DapperJsonFixture(TestDatabase.Open());

        Assert.Equal("[1]", connection.QuerySingle<string>("SELECT JSON_ARRAY(1, NULL ABSENT ON NULL)"));
    }

    /// <summary>docs/functions.html:4162 — )</summary>
    [Fact]
    public void JsonArray_WithoutArguments_IsEmpty() // FUN-4162
    {
        using var connection = new DapperJsonFixture(TestDatabase.Open());

        Assert.Equal("[]", connection.QuerySingle<string>("SELECT JSON_ARRAY()"));
    }
}
