/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * WHERE ... LIKE, NOT LIKE, ILIKE and the ESCAPE clause over ADO.NET, written without
 * reading the implementation. The rows a SELECT returns for a fixed dataset and pattern is
 * the dialect behavior this pins.
 *
 * The oracle is not this port: the reference of the same H2 version answers through
 * CompareLike's compiled pattern when the compare mode is OFF —
 *
 * - '%' matches any run of characters including an empty one, '_' exactly one character,
 *   otherwise the characters match literally (CompareLike.initPattern);
 * - LIKE is case-sensitive, ILIKE ignores case;
 * - a NULL on either side, a NULL pattern or a NULL escape makes the predicate UNKNOWN, so
 *   the row is never returned (CompareLike.optimize and getValue answer NULL, not false);
 * - the default escape character is '\', so 'a\_c' matches only a value with a literal
 *   underscore, and a pattern that ends with the escape character makes the predicate
 *   UNKNOWN rather than an error (initPattern marks it invalid, getValue answers NULL);
 * - an explicit ESCAPE valued longer than one character throws DbException
 *   LIKE_ESCAPE_ERROR_1 (22025) (CompareLike.getEscapeChar).
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

public sealed class H2LikeSemanticsTests
{
    private static IDbConnection Open() => OpenNamed("like_" + Guid.NewGuid().ToString("N"));

    private static IDbConnection OpenNamed(string name)
    {
        IDbConnection connection = new H2Connection($"jdbc:h2:mem:{name}");
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
        _ = connection.Execute("INSERT INTO T VALUES (6, 'aac')");
        _ = connection.Execute("INSERT INTO T VALUES (7, 'a_c')");
    }

    private static List<string> NamesLike(IDbConnection connection, string suffix)
    {
        var rows = connection.Query<string>($"SELECT NAME FROM T WHERE NAME LIKE {suffix} ORDER BY ID");
        return rows.ToList();
    }

    [Fact]
    public void Like_PercentMatchesAnyRun()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        Assert.Equal(new[] { "abc", "ab" }, NamesLike(connection, "'ab%'"));
        // '%bc' ends with "bc": "xabcx" ends with "cx", so only "abc" matches.
        Assert.Equal(new[] { "abc" }, NamesLike(connection, "'%bc'"));
    }

    [Fact]
    public void Like_UnderscoreMatchesExactlyOne()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        Assert.Equal(new[] { "abc", "aac", "a_c" }, NamesLike(connection, "'a_c'"));
        Assert.Equal(Array.Empty<string>(), NamesLike(connection, "'______'"));
    }

    [Fact]
    public void Like_IsCaseSensitive()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        Assert.Equal(new[] { "ABC" }, NamesLike(connection, "'AB%'"));
    }

    [Fact]
    public void ILike_IgnoresCase()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        var rows = connection.Query<string>("SELECT NAME FROM T WHERE NAME ILIKE 'AB%' ORDER BY ID");
        Assert.Equal(new[] { "abc", "ABC", "ab" }, rows);
    }

    [Fact]
    public void NotLike_InvertsTheMatch()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        var rows = connection.Query<string>("SELECT NAME FROM T WHERE NOT NAME LIKE 'ab%' ORDER BY ID");
        Assert.Equal(new[] { "ABC", "xabcx", "aac", "a_c" }, rows);
    }

    [Fact]
    public void Like_EscapeQuotesTheMetacharacter()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        Assert.Equal(new[] { "a_c" }, NamesLike(connection, "'a\\_c'"));
        Assert.Equal(new[] { "a_c" }, NamesLike(connection, "'a?_c' ESCAPE '?'"));
    }

    [Fact]
    public void Like_PatternThroughAParameter()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        using IDbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT NAME FROM T WHERE NAME LIKE @pattern ORDER BY ID";
        IDbDataParameter parameter = command.CreateParameter();
        parameter.ParameterName = "pattern";
        parameter.Value = "ab%";
        command.Parameters.Add(parameter);
        using IDataReader reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        Assert.Equal(new[] { "abc", "ab" }, names);
    }

    [Fact]
    public void Like_NullOnEitherSideIsNeverMatched()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        // '%' would match every non-null name; the NULL row stays out all the same.
        Assert.Equal(7, connection.Query<int>("SELECT COUNT(*) FROM T").Single());
        Assert.Equal(6, connection.Query<int>("SELECT COUNT(*) FROM T WHERE NAME LIKE '%'").Single());
        // A NULL pattern makes the predicate UNKNOWN for every known name.
        using IDbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM T WHERE NAME LIKE @pattern";
        IDbDataParameter parameter = command.CreateParameter();
        parameter.ParameterName = "pattern";
        parameter.Value = DBNull.Value;
        command.Parameters.Add(parameter);
        Assert.Equal(0, Convert.ToInt32(command.ExecuteScalar()));
    }

    [Fact]
    public void Like_TrailingEscapeMakesThePredicateUnknown()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        // The pattern ends with the default escape character: not an error, the predicate is
        // UNKNOWN for every row, so no row is returned.
        Assert.Equal(0, connection.Query<int>("SELECT COUNT(*) FROM T WHERE NAME LIKE 'X\\'").Single());
    }

    [Fact]
    public void Like_InvalidEscapeClauseThrowsLikeEscapeError()
    {
        using IDbConnection connection = Open();
        PrepareNames(connection);
        Exception exception = Assert.ThrowsAny<Exception>(
            () => connection.Execute("SELECT NAME FROM T WHERE NAME LIKE 'a%' ESCAPE 'ab'"));
        H2SharpException databaseException = Assert.IsType<H2SharpException>(exception);
        Assert.Equal(22025, databaseException.H2ErrorCode);
    }
}
