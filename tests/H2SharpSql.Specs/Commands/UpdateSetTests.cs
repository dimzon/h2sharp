// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — UPDATE row-value assignment and array element assignment.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class UpdateSetTests
{
    private static IDbConnection GivenTable()
    {
        IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(50), V INT)");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME, V) VALUES (1, 'Ada', 1)");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME, V) VALUES (2, 'Alan', 2)");
        return connection;
    }

    /// <summary>docs/commands.html — SET ( columnName [,...] ) = rowValue</summary>
    [Fact]
    public void Update_RowValueAssignment_UpdatesBothColumns()
    {
        using IDbConnection connection = GivenTable();

        _ = connection.Execute("UPDATE TEST SET (NAME, V) = ('Grace', 7) WHERE ID = 2");

        Assert.Equal("Grace", connection.QuerySingle<string>("SELECT NAME FROM TEST WHERE ID = 2"));
        Assert.Equal(7, connection.QuerySingle<int>("SELECT V FROM TEST WHERE ID = 2"));
        Assert.Equal("Ada", connection.QuerySingle<string>("SELECT NAME FROM TEST WHERE ID = 1"));
    }

    /// <summary>docs/commands.html — SET ( columnName [,...] ) = query</summary>
    [Fact]
    public void Update_RowValueAssignment_FromSubquery()
    {
        using IDbConnection connection = GivenTable();
        _ = connection.Execute("CREATE TABLE SOURCE(NAME VARCHAR(50), V INT)");
        _ = connection.Execute("INSERT INTO SOURCE(NAME, V) VALUES ('Hopper', 9)");

        _ = connection.Execute("UPDATE TEST SET (NAME, V) = (SELECT NAME, V FROM SOURCE) WHERE ID = 1");

        Assert.Equal("Hopper", connection.QuerySingle<string>("SELECT NAME FROM TEST WHERE ID = 1"));
        Assert.Equal(9, connection.QuerySingle<int>("SELECT V FROM TEST WHERE ID = 1"));
    }

    /// <summary>docs/commands.html — the row and the column list must match in size</summary>
    [Fact]
    public void Update_RowValueAssignment_CountMismatch_IsRejected()
    {
        using IDbConnection connection = GivenTable();

        Assert.ThrowsAny<Exception>(
            () => connection.Execute("UPDATE TEST SET (NAME, V) = ('Grace') WHERE ID = 2"));
    }

    /// <summary>docs/commands.html — SET columnName [ index ] = expression</summary>
    [Fact]
    public void Update_ArrayElementAssignment_UpdatesTheSlot()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT PRIMARY KEY, TAGS VARCHAR ARRAY)");
        _ = connection.Execute("INSERT INTO TEST(ID, TAGS) VALUES (1, ARRAY['a', 'b'])");

        _ = connection.Execute("UPDATE TEST SET TAGS[1] = 'z' WHERE ID = 1");

        Assert.Equal("z", connection.QuerySingle<string>("SELECT TAGS[1] FROM TEST WHERE ID = 1"));
        Assert.Equal("b", connection.QuerySingle<string>("SELECT TAGS[2] FROM TEST WHERE ID = 1"));
    }

    /// <summary>docs/commands.html:873 — EXPLAIN renders the SET list</summary>
    [Fact]
    public void Explain_Update_ReturnsThePlan()
    {
        using IDbConnection connection = GivenTable();

        string plan = connection.QuerySingle<string>(
            "EXPLAIN PLAN FOR UPDATE TEST SET NAME = 'Grace', V = 7 WHERE ID = 2");

        Assert.Contains("UPDATE", plan);
    }
}
