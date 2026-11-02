// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// MERGE INTO error paths and plan rendering.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class MergeNegativeTests
{
    [Fact]
    public void MergeInto_NullKey_IsRejected()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(50))");

        Assert.ThrowsAny<Exception>(
            () => connection.Execute("MERGE INTO TEST(ID, NAME) VALUES (NULL, 'x')"));
    }

    [Fact]
    public void MergeInto_GeneratedAlwaysColumn_IsRejected()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute(
            "CREATE TABLE TEST(ID BIGINT GENERATED ALWAYS AS IDENTITY, NAME VARCHAR(50))");

        Assert.ThrowsAny<Exception>(
            () => connection.Execute("MERGE INTO TEST(ID, NAME) VALUES (1, 'x')"));
    }

    [Fact]
    public void MergeInto_OnlyGeneratedColumn_IsRejected()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID BIGINT GENERATED ALWAYS AS IDENTITY)");

        Assert.ThrowsAny<Exception>(
            () => connection.Execute("MERGE INTO TEST(ID) VALUES (1)"));
    }
}
