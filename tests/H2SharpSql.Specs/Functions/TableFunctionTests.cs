// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/functions.html — table functions, one test per checklist row.
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Functions;

public sealed class TableFunctionTests
{
    /// <summary>docs/functions.html:4185 — CSVREAD(fileNameString [, columnsString [, csvOptions ] ] )</summary>
    [Fact]
    public void CsvRead_ReadsTheWrittenFile() // FUN-4185
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.QuerySingle<long>(
            "CALL CSVWRITE('test.csv', 'SELECT 1 AS ID UNION ALL SELECT 2 AS ID')");

        Assert.Equal(2L, connection.QuerySingle<long>("SELECT COUNT(*) FROM CSVREAD('test.csv')"));

        _ = connection.QuerySingle<long>(
            "CALL CSVWRITE('test2.csv', 'SELECT 1 AS ID', 'charset=UTF-8 fieldSeparator=|')");
        Assert.Equal(1L, connection.QuerySingle<long>(
            "SELECT COUNT(*) FROM CSVREAD('test2.csv', 'ID', 'charset=UTF-8 fieldSeparator=|')"));
    }

    /// <summary>docs/functions.html:4203 — LINK_SCHEMA (targetSchemaString, driverString, urlString,</summary>
    [Fact]
    public void LinkSchema_CreatesTheTargetSchema() // FUN-4203
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");

        _ = connection.Query("SELECT * FROM LINK_SCHEMA('REMOTE', '', 'jdbc:h2:mem:remote', 'sa', '', 'PUBLIC')")
            .AsList();

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'REMOTE'"));
    }

    /// <summary>docs/functions.html:4204 — userString, passwordString, sourceSchemaString)</summary>
    [Fact]
    public void LinkSchema_WithExplicitDriver() // FUN-4204
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");

        _ = connection.Query(
            "SELECT * FROM LINK_SCHEMA('REMOTE2', 'org.h2.Driver', 'jdbc:h2:mem:remote2', 'sa', '', 'PUBLIC')")
            .AsList();

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'REMOTE2'"));
    }

    /// <summary>docs/functions.html:4223 — { TABLE | TABLE_DISTINCT }</summary>
    [Fact]
    public void TableFunction_BuildsAResultSet() // FUN-4223
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TABLE(V INT = ARRAY[1, 2])"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TABLE_DISTINCT(V INT = ARRAY[1, 1])"));
    }

    /// <summary>docs/functions.html:4224 — ( { name dataTypeOrDomain = {array|rowValueExpression} } [,...] )</summary>
    [Fact]
    public void TableFunction_WithRowExpressions() // FUN-4224
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM TABLE(ID INT = (1, 2), NAME VARCHAR = ('a', 'b'))"));
    }

    /// <summary>docs/functions.html:4243 — UNNEST(arrayExpression, [,...]) [WITH ORDINALITY]</summary>
    [Fact]
    public void Unnest_TurnsAnArrayIntoRows() // FUN-4243
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM UNNEST(ARRAY[1, 2, 3]) WITH ORDINALITY"));
    }
}
