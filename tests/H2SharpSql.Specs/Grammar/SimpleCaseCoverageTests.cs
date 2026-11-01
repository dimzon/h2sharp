// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// Additional simple CASE forms: multiple WHEN operands, NULL semantics and type checks.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class SimpleCaseCoverageTests
{
    private static IDbConnection GivenTable()
    {
        IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE T(ID INT, V INT)");
        _ = connection.Execute("INSERT INTO T VALUES (1, NULL), (2, 10), (3, NULL)");
        return connection;
    }

    [Fact]
    public void MultipleWhenOperandsShareOneResult()
    {
        using IDbConnection connection = GivenTable();

        Assert.Equal("odd", connection.QuerySingle<string>(
            "SELECT CASE ID WHEN 1, 3 THEN 'odd' WHEN 2 THEN 'even' END FROM T WHERE ID = 3"));
        Assert.Equal("even", connection.QuerySingle<string>(
            "SELECT CASE ID WHEN 1, 3 THEN 'odd' WHEN 2 THEN 'even' END FROM T WHERE ID = 2"));
    }

    [Fact]
    public void NullOperandDoesNotMatchNullWhenValue()
    {
        using IDbConnection connection = GivenTable();

        Assert.Equal("fallback", connection.QuerySingle<string>(
            "SELECT CASE V WHEN NULL THEN 'matched' ELSE 'fallback' END FROM T WHERE ID = 1"));
        Assert.Equal("fallback", connection.QuerySingle<string>(
            "SELECT CASE NULL WHEN NULL THEN 'matched' ELSE 'fallback' END"));
    }

    [Fact]
    public void MissingElseYieldsNullAndResultTypeIsCombined()
    {
        using IDbConnection connection = GivenTable();

        Assert.Null(connection.QuerySingle<int?>(
            "SELECT CASE ID WHEN 1 THEN 10 END FROM T WHERE ID = 2"));
        Assert.Equal(10, connection.QuerySingle<int>(
            "SELECT CASE ID WHEN 1 THEN NULL WHEN 2 THEN 10 END FROM T WHERE ID = 2"));
    }

    [Fact]
    public void IncomparableWhenValueIsRejected()
    {
        using IDbConnection connection = GivenTable();

        Assert.ThrowsAny<Exception>(() => connection.Execute(
            "SELECT CASE ID WHEN 'x' THEN 1 ELSE 2 END FROM T"));
    }
}
