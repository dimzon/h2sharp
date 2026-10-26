/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using System;
using System.Data.Common;
using Dapper;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2FreshIssuesTests
{
    [Fact]
    public void CountFilterChecksTheRowsInsteadOfTheTableCounter()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE PF(ID INT, H BOOLEAN)");
        connection.Execute("INSERT INTO PF VALUES (1, TRUE), (2, FALSE), (3, FALSE)");
        Assert.Equal(1L, connection.QuerySingle<long>("SELECT COUNT(*) FILTER (WHERE H) FROM PF"));
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FILTER (WHERE H), COUNT(*) FILTER (WHERE NOT H), COUNT(*) FROM PF";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(0)); Assert.Equal(2L, reader.GetInt64(1)); Assert.Equal(3L, reader.GetInt64(2));
    }

    [Fact]
    public void SetOperationsRunWithPagingAndParameters()
    {
        using H2Connection connection = Open();
        Assert.Equal(new[] { 1 }, connection.Query<int>("SELECT ID FROM (SELECT 1 ID) i INTERSECT SELECT ID FROM (SELECT 1 ID) j"));
        Assert.Equal(new[] { 1 }, connection.Query<int>("SELECT ID FROM (SELECT 1 ID) i EXCEPT SELECT ID FROM (SELECT 2 ID) j"));
        Assert.Equal(new[] { 2, 1 }, connection.Query<int>(
            "SELECT CAST($a AS INT) AS ID UNION SELECT CAST($b AS INT) UNION SELECT CAST($c AS INT) ORDER BY ID DESC LIMIT $take OFFSET $skip",
            new { a = 1, b = 2, c = 3, take = 2, skip = 1 }));
    }

    [Fact]
    public void SavepointsUndoOnlyTheChangesAfterTheirPosition()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY)");
        using DbTransaction transaction = connection.BeginTransaction();
        Assert.True(transaction.SupportsSavepoints);
        connection.Execute("INSERT INTO T VALUES (1)", transaction: transaction);
        transaction.Save("s1");
        connection.Execute("INSERT INTO T VALUES (2)", transaction: transaction);
        transaction.Rollback("s1");
        Assert.Equal(new[] { 1 }, connection.Query<int>("SELECT ID FROM T ORDER BY ID", transaction: transaction));
        transaction.Release("s1");
        connection.Execute("INSERT INTO T VALUES (3)", transaction: transaction);
        transaction.Rollback("s1");
        transaction.Commit();
        Assert.Equal(new[] { 1 }, connection.Query<int>("SELECT ID FROM T ORDER BY ID"));
    }

    [Fact]
    public void TwoListaggsKeepTheirOwnOrderAndFilter()
    {
        using H2Connection connection = Open();
        connection.Execute("CREATE TABLE F(\"Id\" INT PRIMARY KEY, \"Name\" VARCHAR)");
        connection.Execute("INSERT INTO F VALUES (1, 'a'), (2, 'b'), (3, 'c')");
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT LISTAGG(\"Name\", ',') WITHIN GROUP (ORDER BY \"Id\" DESC) FILTER (WHERE \"Id\" < 3), "
            + "LISTAGG(\"Name\", ':') WITHIN GROUP (ORDER BY \"Id\") FILTER (WHERE \"Id\" > 1) FROM F";
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("b,a", reader.GetString(0));
        Assert.Equal("b:c", reader.GetString(1));
    }

    [Fact]
    public void EscapePrefixAndScalarCastOfCountFollowH2()
    {
        using H2Connection connection = Open();
        Assert.Equal("a", connection.QuerySingle<string>("SELECT BTRIM(' a ', E' \\t\\n\\r')"));
        Assert.Equal("\\n", connection.QuerySingle<string>("SELECT E'\\n'"));
        connection.Execute("CREATE TABLE F(\"Id\" INT PRIMARY KEY)");
        connection.Execute("INSERT INTO F VALUES (1), (2), (3)");
        Assert.Equal(1, connection.QuerySingle<int>("SELECT (SELECT CAST(COUNT(*) AS INTEGER) FROM F f WHERE f.\"Id\" = 1)"));
        Assert.Equal(new[] { 1, 2, 3 }, connection.Query<int>(
            "SELECT (SELECT CAST(COUNT(*) AS INTEGER) FROM F g WHERE g.\"Id\" <= f.\"Id\") FROM F f ORDER BY f.\"Id\""));
    }

    private static H2Connection Open()
    {
        H2Connection connection = new("jdbc:h2:mem:fresh_ado_" + Guid.NewGuid().ToString("N"));
        connection.Open();
        return connection;
    }
}
