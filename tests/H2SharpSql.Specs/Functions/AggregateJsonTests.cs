// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions-aggregate.html — JSON aggregates, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class AggregateJsonTests
{
    private static void GivenPairs(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(10))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'a'), (2, 'b')");
    }

    private static void GivenPairsWithNull(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(10))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'a'), (2, 'b'), (3, NULL)");
    }

    private static void GivenNumbers(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (1), (2), (3)");
    }

    private static void GivenNumbersWithNull(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");
        _ = connection.Execute("INSERT INTO TEST(ID) VALUES (1), (NULL)");
    }

    private static string Json(IDbConnection connection, string sql)
        => System.Text.Encoding.UTF8.GetString(connection.QuerySingle<byte[]>(sql));

    /// <summary>docs/functions-aggregate.html:1295 — JSON_OBJECTAGG(</summary>
    [Fact]
    public void JsonObjectAgg_BuildsAnObject() // AGG-1295
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal("{\"1\":\"a\",\"2\":\"b\"}",
            Json(connection, "SELECT JSON_OBJECTAGG(KEY ID VALUE NAME) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1296 — {[KEY] string VALUE value} | {string : value}</summary>
    [Fact]
    public void JsonObjectAgg_WithColonForm() // AGG-1296
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal("{\"1\":\"a\",\"2\":\"b\"}",
            Json(connection, "SELECT JSON_OBJECTAGG(ID : NAME) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1297 — [ { NULL | ABSENT } ON NULL ]</summary>
    [Fact]
    public void JsonObjectAgg_AbsentOnNull_DropsThePair() // AGG-1297
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairsWithNull(connection);

        Assert.Equal("{\"1\":\"a\",\"2\":\"b\"}",
            Json(connection, "SELECT JSON_OBJECTAGG(KEY ID VALUE NAME ABSENT ON NULL) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1298 — [ { WITH | WITHOUT } UNIQUE KEYS ]</summary>
    [Fact]
    public void JsonObjectAgg_WithUniqueKeys() // AGG-1298
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal("{\"1\":\"a\",\"2\":\"b\"}",
            Json(connection, "SELECT JSON_OBJECTAGG(KEY ID VALUE NAME WITH UNIQUE KEYS) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1299 — )</summary>
    [Fact]
    public void JsonObjectAgg_WithoutClauses() // AGG-1299
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal("{\"1\":\"a\",\"2\":\"b\"}",
            Json(connection, "SELECT JSON_OBJECTAGG(KEY ID VALUE NAME) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1300 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void JsonObjectAgg_WithFilter() // AGG-1300
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenPairs(connection);

        Assert.Equal("{\"1\":\"a\"}",
            Json(connection, "SELECT JSON_OBJECTAGG(KEY ID VALUE NAME) FILTER (WHERE ID = 1) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1323 — JSON_ARRAYAGG( [ DISTINCT|ALL ] expression</summary>
    [Fact]
    public void JsonArrayAgg_BuildsAnArray() // AGG-1323
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal("[1,2,3]", Json(connection, "SELECT JSON_ARRAYAGG(ID) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1324 — [ ORDER BY sortSpecificationList ]</summary>
    [Fact]
    public void JsonArrayAgg_WithOrderBy() // AGG-1324
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal("[3,2,1]",
            Json(connection, "SELECT JSON_ARRAYAGG(ID ORDER BY ID DESC) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1325 — [ { NULL | ABSENT } ON NULL ] )</summary>
    [Fact]
    public void JsonArrayAgg_AbsentOnNull_DropsTheElement() // AGG-1325
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbersWithNull(connection);

        Assert.Equal("[1]",
            Json(connection, "SELECT JSON_ARRAYAGG(ID ABSENT ON NULL) FROM TEST"));
    }

    /// <summary>docs/functions-aggregate.html:1326 — [FILTER (WHERE expression)] [OVER windowNameOrSpecification]</summary>
    [Fact]
    public void JsonArrayAgg_WithFilter() // AGG-1326
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenNumbers(connection);

        Assert.Equal("[2,3]",
            Json(connection, "SELECT JSON_ARRAYAGG(ID) FILTER (WHERE ID > 1) FROM TEST"));
    }
}
