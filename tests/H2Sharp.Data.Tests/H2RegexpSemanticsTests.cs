/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * REGEXP over ADO.NET, written without reading the implementation. The list of rows an
 * unprepared SELECT WHERE NAME REGEXP 'pattern' returns, and that the pattern may arrive
 * through a DbParameter, is the dialect behavior this pins (one Fact per dataset+pattern case).
 *
 * The reference of the same H2 version answers through java.util.regex.Pattern.compile(p) +
 * Pattern.matcher(value).find() when the compare mode is OFF — a case-sensitive search for the
 * pattern anywhere in the value, not a full match — and an invalid pattern makes it throw
 * DbException LIKE_ESCAPE_ERROR_1 (22025), not answer false.
 *
 * Recorded deviation, accepted by the project owner: the port evaluates REGEXP with the .NET
 * regex engine, so .NET regex syntax is what the tests pin, not Java regex syntax. Matching
 * itself matches the reference: IsMatch is a partial, case-sensitive search (a .NET analogue
 * of find()), so every expectation that follows is engine-independent except
 * Regexp_AcceptsDotNetOnlySyntax, which pins a pattern only .NET accepts.
 *
 * Dapper is used as the caller convenience only; nothing against the provider's own members.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2Sharp;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2RegexpSemanticsTests
{
    private static IDbConnection Open() => OpenNamed("regexp_" + Guid.NewGuid().ToString("N"));

    private static IDbConnection OpenNamed(string name)
    {
        IDbConnection connection = new H2Connection($"Data Source={name};Storage=Memory");
        connection.Open();
        return connection;
    }

    private static void PrepareNames(IDbConnection connection)
    {
        _ = connection.Execute("DROP TABLE IF EXISTS T");
        _ = connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, NAME VARCHAR)");
        _ = connection.Execute("INSERT INTO T VALUES (1, 'abc')");
        _ = connection.Execute("INSERT INTO T VALUES (2, 'ABC')");
        _ = connection.Execute("INSERT INTO T VALUES (3, 'xabcx')");
        _ = connection.Execute("INSERT INTO T VALUES (4, 'ab')");
        _ = connection.Execute("INSERT INTO T VALUES (5, NULL)");
    }

    [Fact]
    public void Regexp_FindsThePatternAnywhere()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        var rows = connection.Query<string>("SELECT NAME FROM T WHERE NAME REGEXP 'ab' ORDER BY ID");
        Assert.Equal(new[] { "abc", "xabcx", "ab" }, rows);
    }

    [Fact]
    public void Regexp_IsCaseSensitive()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        var rows = connection.Query<string>("SELECT NAME FROM T WHERE NAME REGEXP 'AB' ORDER BY ID");
        Assert.Equal(new[] { "ABC" }, rows);
    }

    [Fact]
    public void Regexp_AnchorsPinStartAndEnd()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        var rows = connection.Query<string>("SELECT NAME FROM T WHERE NAME REGEXP '^ab$' ORDER BY ID");
        Assert.Equal(new[] { "ab" }, rows);
    }

    [Fact]
    public void Regexp_NullIsNeverAMatch()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        var rows = connection.Query<int>("SELECT COUNT(*) FROM T WHERE NAME REGEXP '.'");
        Assert.Equal(4, rows.Single());
    }

    [Fact]
    public void Regexp_PatternThroughAParameter()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        using IDbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT NAME FROM T WHERE NAME REGEXP $pattern ORDER BY ID";
        IDbDataParameter parameter = command.CreateParameter();
        parameter.ParameterName = "pattern";
        parameter.Value = "b.";
        command.Parameters.Add(parameter);
        using IDataReader reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        Assert.Equal(new[] { "abc", "xabcx" }, names);
    }

    [Fact]
    public void Regexp_InvalidPatternThrowsLikeEscapeError()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        Exception exception = Assert.ThrowsAny<Exception>(
            () => connection.Execute("SELECT NAME FROM T WHERE NAME REGEXP '['"));
        H2SharpException databaseException = Assert.IsType<H2SharpException>(exception);
        Assert.Equal(22025, databaseException.H2ErrorCode);
    }

    [Fact]
    public void Regexp_AcceptsDotNetOnlySyntax()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        // Variable-length lookbehind is legal .NET and illegal Java; pinning it here makes the
        // recorded deviation observable — the Java engine of the reference would reject it.
        var rows = connection.Query<string>("SELECT NAME FROM T WHERE NAME REGEXP '(?<=(a|ab))bc$' ORDER BY ID");
        // 'bc$' ends with "bc": "xabcx" ends with "cx", so only "abc" matches.
        Assert.Equal(new[] { "abc" }, rows);
    }
}
