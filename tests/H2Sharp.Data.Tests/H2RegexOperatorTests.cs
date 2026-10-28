/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0.
 * Regex syntax deliberately remains .NET, by project decision. */
using System;
using Dapper;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2RegexOperatorTests
{
    [Fact]
    public void Issue22TitlesAreMatchedWithAllFourOperators()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE TOPICS(ID INT PRIMARY KEY, TITLE VARCHAR)");
        connection.Execute("INSERT INTO TOPICS VALUES (1, 'abc'), (2, 'ABC'), (3, 'xabcx'), (4, 'ab'), (5, NULL)");
        Assert.Equal(new[] { 1, 4 }, connection.Query<int>("SELECT ID FROM TOPICS WHERE TITLE ~ '^a' ORDER BY ID"));
        Assert.Equal(new[] { 1, 2, 4 }, connection.Query<int>(
            "SELECT \"t\".\"ID\" FROM \"TOPICS\" AS \"t\" WHERE \"t\".\"TITLE\" ~* $pattern ORDER BY \"t\".\"ID\"",
            new { pattern = "^a" }));
        Assert.Equal(new[] { 2, 3 }, connection.Query<int>("SELECT ID FROM TOPICS WHERE TITLE !~ '^a' ORDER BY ID"));
        Assert.Equal(new[] { 3 }, connection.Query<int>("SELECT ID FROM TOPICS WHERE TITLE !~* '^a' ORDER BY ID"));
        Assert.Empty(connection.Query<int>("SELECT ID FROM TOPICS WHERE TITLE ~* $pattern ORDER BY ID", new { pattern = "^missing$" }));
    }

    [Fact]
    public void InlineOptionsAndNullsKeepTheirMeaning()
    {
        using H2Connection connection = Open();
        Assert.False(connection.QuerySingle<bool>("SELECT 'a' || CHR(10) || 'b' ~ 'a.b'"));
        Assert.True(connection.QuerySingle<bool>("SELECT 'a' || CHR(10) || 'b' ~ '(?s)a.b'"));
        Assert.True(connection.QuerySingle<bool>("SELECT 'a' || CHR(10) || 'b' ~ '(?m)^b'"));
        Assert.True(connection.QuerySingle<bool>("SELECT 'ab' ~ '(?x)a b'"));
        Assert.False(connection.QuerySingle<bool>("SELECT 'ABC' ~* '(?-i)^a'"));
        Assert.Null(connection.QuerySingle<bool?>("SELECT NULL ~* '^a'"));
        Assert.Null(connection.QuerySingle<bool?>("SELECT 'abc' ~ $pattern", new { pattern = (string?)null }));
        H2SharpException error = Assert.Throws<H2SharpException>(() => connection.QuerySingle<bool>("SELECT 'abc' ~* '['"));
        Assert.Equal(22025, error.H2ErrorCode);
    }

    [Fact]
    public void OperatorsAcceptDotNetRegexSyntaxByProjectDecision()
    {
        using H2Connection connection = Open();
        // .NET balancing groups are deliberately accepted; java.util.regex rejects this syntax.
        const string Pattern = "^(?'open'a)+(?'-open'b)+(?(open)(?!))$";
        Assert.True(connection.QuerySingle<bool>("SELECT 'aabb' ~ $pattern", new { pattern = Pattern }));
        Assert.True(connection.QuerySingle<bool>("SELECT 'AABB' ~* $pattern", new { pattern = Pattern }));
        Assert.False(connection.QuerySingle<bool>("SELECT 'aab' ~ $pattern", new { pattern = Pattern }));
        Assert.True(connection.QuerySingle<bool>("SELECT 'aab' !~ $pattern", new { pattern = Pattern }));
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("Data Source=regex_operator_ado_" + Guid.NewGuid().ToString("N") + ";Storage=Memory");
        connection.Open();
        return connection;
    }
}
