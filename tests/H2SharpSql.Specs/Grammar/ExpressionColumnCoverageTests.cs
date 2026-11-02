// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// Additional ExpressionColumn paths: schema-qualified names, rowid, aliases,
// quoted identifiers and resolution errors.
using System;
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class ExpressionColumnCoverageTests
{
    [Fact]
    public void SchemaQualifiedAndTableQualifiedColumnsAreRead()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST VALUES (1, 'Ada'), (2, 'Alan')");

        Assert.Equal("Ada", connection.QuerySingle<string>(
            "SELECT PUBLIC.TEST.NAME FROM TEST WHERE ID = 1"));
        Assert.Equal("Alan", connection.QuerySingle<string>(
            "SELECT TEST.NAME FROM TEST WHERE ID = 2"));
    }

    [Fact]
    public void RowIdIsReadableWithAndWithoutTableAlias()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT PRIMARY KEY)");
        _ = connection.Execute("INSERT INTO TEST VALUES (1), (2)");

        Assert.Equal(2, connection.Query("SELECT _ROWID_ FROM TEST").AsList().Count);
        Assert.Equal(2, connection.Query("SELECT TEST._ROWID_ FROM TEST").AsList().Count);
        Assert.Equal(2, connection.Query("SELECT T._ROWID_ FROM TEST AS T").AsList().Count);
    }

    [Fact]
    public void SelectAliasesAreUsableInOrderByAndGroupBy()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST VALUES (2, 'b'), (1, 'a')");

        Assert.Equal("a", connection.Query<string>("SELECT NAME AS N FROM TEST ORDER BY N").First());
        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM (SELECT NAME AS N FROM TEST GROUP BY N)"));
    }

    [Fact]
    public void QuotedColumnNamesRoundTrip()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(\"My Col\" INT)");
        _ = connection.Execute("INSERT INTO TEST VALUES (7)");

        Assert.Equal(7, connection.QuerySingle<int>("SELECT \"My Col\" FROM TEST"));
    }

    [Fact]
    public void MissingColumnIsRejected()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");

        Assert.ThrowsAny<Exception>(() => connection.Execute("SELECT NOPE FROM TEST"));
    }

    [Fact]
    public void AmbiguousColumnIsRejected()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE A(X INT)");
        _ = connection.Execute("CREATE TABLE B(X INT)");
        _ = connection.Execute("INSERT INTO A VALUES (1)");
        _ = connection.Execute("INSERT INTO B VALUES (2)");

        Assert.ThrowsAny<Exception>(() => connection.Execute("SELECT X FROM A, B"));
    }

    [Fact]
    public void BooleanColumnCreatesIndexCondition()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT, ACTIVE BOOLEAN)");
        _ = connection.Execute("INSERT INTO TEST VALUES (1, TRUE), (2, FALSE), (3, TRUE)");
        _ = connection.Execute("CREATE INDEX TEST_ACTIVE ON TEST(ACTIVE)");

        Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM TEST WHERE ACTIVE"));
    }
}
