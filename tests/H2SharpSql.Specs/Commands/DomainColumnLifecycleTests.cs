// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// Full lifecycle of a table column whose type is a domain: create, insert, update, select.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class DomainColumnLifecycleTests
{
    [Fact]
    public void DomainColumnSupportsInsertUpdateAndSelect()
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE DOMAIN D_POSITIVE AS INT DEFAULT 7");
        _ = connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, V D_POSITIVE)");

        _ = connection.Execute("INSERT INTO T(ID) VALUES (1)");
        _ = connection.Execute("INSERT INTO T(ID, V) VALUES (2, 20)");
        _ = connection.Execute("INSERT INTO T(ID) VALUES (3)");

        Assert.Equal(7, connection.QuerySingle<int>("SELECT V FROM T WHERE ID = 1"));
        Assert.Equal(20, connection.QuerySingle<int>("SELECT V FROM T WHERE ID = 2"));

        _ = connection.Execute("UPDATE T SET V = 30 WHERE ID = 1");
        Assert.Equal(30, connection.QuerySingle<int>("SELECT V FROM T WHERE ID = 1"));

        _ = connection.Execute("UPDATE T SET ID = 4 WHERE ID = 2");
        Assert.Equal(20, connection.QuerySingle<int>("SELECT V FROM T WHERE ID = 4"));

        Assert.Equal("30,7,20", string.Join(",",
            connection.Query<int>("SELECT V FROM T ORDER BY ID")));
    }

    [Fact]
    public void AlterDomainDefaultAppliesToNewRowsOnly()
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE DOMAIN D AS INT DEFAULT 7");
        _ = connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, V D)");
        _ = connection.Execute("INSERT INTO T(ID) VALUES (1)");

        _ = connection.Execute("ALTER DOMAIN D SET DEFAULT 42");
        _ = connection.Execute("INSERT INTO T(ID) VALUES (2)");

        Assert.Equal(7, connection.QuerySingle<int>("SELECT V FROM T WHERE ID = 1"));
        Assert.Equal(42, connection.QuerySingle<int>("SELECT V FROM T WHERE ID = 2"));
    }

    [Fact]
    public void DomainOnUpdateRunsForTheDomainColumn()
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE DOMAIN D AS INT ON UPDATE 9");
        _ = connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, V D)");
        _ = connection.Execute("INSERT INTO T(ID, V) VALUES (1, 1)");

        _ = connection.Execute("UPDATE T SET ID = 2 WHERE ID = 1");

        Assert.Equal(9, connection.QuerySingle<int>("SELECT V FROM T WHERE ID = 2"));
    }

    [Fact]
    public void ParentDomainColumnKeepsTheBaseType()
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE DOMAIN D1 AS INT DEFAULT 5");
        _ = connection.Execute("CREATE DOMAIN D2 D1");
        _ = connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, V D2)");
        _ = connection.Execute("INSERT INTO T(ID) VALUES (1)");

        Assert.Equal(5, connection.QuerySingle<int>("SELECT V FROM T"));
    }

    [Fact]
    public void DomainCheckConstraintRunsOnInsertAndUpdate()
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE DOMAIN D AS INT CHECK (VALUE > 0)");
        _ = connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, V D)");

        _ = connection.Execute("INSERT INTO T(ID, V) VALUES (1, 1)");
        _ = Assert.ThrowsAny<Exception>(() =>
            connection.Execute("INSERT INTO T(ID, V) VALUES (2, -1)"));

        _ = Assert.ThrowsAny<Exception>(() =>
            connection.Execute("UPDATE T SET V = -2 WHERE ID = 1"));

        Assert.Equal(1, connection.QuerySingle<int>("SELECT V FROM T WHERE ID = 1"));
    }
}
