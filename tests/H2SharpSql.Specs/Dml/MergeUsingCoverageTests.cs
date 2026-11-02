// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// MERGE USING scenarios from the reference's TestMergeUsing: clause dispatch over a real
// source, the deferred unmatched row, the duplicate target refusal, the plan text and the
// data change delta tables.
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Dml;

public sealed class MergeUsingCoverageTests
{
    private static IDbConnection GivenParent()
    {
        IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE PARENT(ID INT, NAME VARCHAR, PRIMARY KEY(ID))");
        return connection;
    }

    /// <summary>TestMergeUsing: the insert-only clause fills an empty target from a source
    /// of several rows</summary>
    [Fact]
    public void InsertOnlyFillsAnEmptyTarget()
    {
        using IDbConnection connection = GivenParent();

        int count = connection.Execute(
            "MERGE INTO PARENT AS P USING (SELECT X AS ID, 'Marcy'||X AS NAME " +
            "FROM SYSTEM_RANGE(1, 3)) AS S ON (P.ID = S.ID) " +
            "WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)");

        Assert.Equal(3, count);
        Assert.Equal(
            [1, 2, 3],
            connection.Query<int>("SELECT ID FROM PARENT ORDER BY ID").AsList());
        Assert.Equal(
            ["Marcy1", "Marcy2", "Marcy3"],
            connection.Query<string>("SELECT NAME FROM PARENT ORDER BY ID").AsList());
    }

    /// <summary>TestMergeUsing (in Oracle mode there; the post-SET WHERE is a mode flag, the
    /// AND condition is plain SQL): one insert, one update and one delete over the same
    /// source</summary>
    [Fact]
    public void UpdateDeleteAndInsertDispatchOverOneSource()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE PARENT AS (SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1, 2))");

        int count = connection.Execute(
            "MERGE INTO PARENT AS P USING (" +
            "SELECT X AS ID, 'Marcy'||X AS NAME FROM SYSTEM_RANGE(1, 3)) AS S ON (P.ID = S.ID) " +
            "WHEN MATCHED AND P.ID = 2 THEN UPDATE SET P.NAME = S.NAME||S.ID " +
            "WHEN MATCHED AND P.ID = 1 THEN DELETE " +
            "WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)");

        Assert.Equal(3, count);
        Assert.Equal(
            [(2, "Marcy22"), (3, "Marcy3")],
            connection.Query<(int, string)>("SELECT ID, NAME FROM PARENT ORDER BY ID").AsList());
    }

    /// <summary>The reference refuses a second source row that matches a target row the
    /// statement has already processed</summary>
    [Fact]
    public void TwoSourceRowsOnOneTargetRowAreRefused()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE T(ID INT PRIMARY KEY, V INT)");
        _ = connection.Execute("INSERT INTO T VALUES (1, 1), (2, 2)");

        H2Sharp.H2SharpException e = Assert.Throws<H2Sharp.H2SharpException>(
            () => connection.Execute(
                "MERGE INTO T USING (SELECT 1 AS K UNION ALL SELECT 1) S ON T.ID = S.K " +
                "WHEN MATCHED THEN UPDATE SET V = V + 1"));
        Assert.Equal(23505, e.H2ErrorCode);
    }

    /// <summary>The plan names the clauses, so their SQL text is written</summary>
    [Fact]
    public void ThePlanNamesTheClauses()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE PARENT(ID INT PRIMARY KEY, NAME VARCHAR)");
        _ = connection.Execute("CREATE TABLE SOURCE(ID INT, NAME VARCHAR)");

        string plan = connection.QuerySingle<string>(
            "EXPLAIN PLAN FOR MERGE INTO PARENT AS P USING SOURCE AS S ON (P.ID = S.ID) " +
            "WHEN MATCHED AND P.NAME IS NOT NULL THEN UPDATE SET P.NAME = S.NAME " +
            "WHEN MATCHED THEN DELETE " +
            "WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)");

        Assert.Contains("MERGE INTO", plan, StringComparison.Ordinal);
        Assert.Contains("UPDATE", plan, StringComparison.Ordinal);
        Assert.Contains("DELETE", plan, StringComparison.Ordinal);
        Assert.Contains("INSERT", plan, StringComparison.Ordinal);
    }

    /// <summary>An insert clause whose value list does not match its column list is
    /// refused</summary>
    [Fact]
    public void InsertColumnCountMismatchIsRefused()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE PARENT(ID INT PRIMARY KEY, NAME VARCHAR)");
        _ = connection.Execute("CREATE TABLE SOURCE(ID INT)");

        H2Sharp.H2SharpException e = Assert.Throws<H2Sharp.H2SharpException>(
            () => connection.Execute(
                "MERGE INTO PARENT AS P USING SOURCE AS S ON (P.ID = S.ID) " +
                "WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (S.ID)"));
        Assert.Equal(21002, e.H2ErrorCode);
    }

    /// <summary>A failing insert value reports the statement that failed. The value comes
    /// from a table so the conversion fails at run time rather than in the optimizer's
    /// constant folding, and a constant-false ON would empty the source before any row is
    /// processed, as the join the reference builds does.</summary>
    [Fact]
    public void AFailingInsertValueNamesTheInsert()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE PARENT(ID INT PRIMARY KEY)");
        _ = connection.Execute("CREATE TABLE SOURCE(V VARCHAR)");
        _ = connection.Execute("INSERT INTO SOURCE VALUES ('abc')");

        H2Sharp.H2SharpException e = Assert.Throws<H2Sharp.H2SharpException>(
            () => connection.Execute(
                "MERGE INTO PARENT AS P USING SOURCE S ON P.ID = 1 " +
                "WHEN NOT MATCHED THEN INSERT (ID) VALUES (CAST(S.V AS INT))"));
        Assert.Contains("INSERT --", e.Message, StringComparison.Ordinal);
    }

    /// <summary>A parameter in the insert values carries the column it belongs to</summary>
    [Fact]
    public void ParametersCarryTheirColumn()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE PARENT(ID INT PRIMARY KEY, NAME VARCHAR)");
        _ = connection.Execute("CREATE TABLE SOURCE(ID INT)");
        _ = connection.Execute("INSERT INTO SOURCE VALUES (5)");

        int count = connection.Execute(
            "MERGE INTO PARENT AS P USING SOURCE AS S ON (P.ID = S.ID) " +
            "WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES ($id, $name)",
            new { id = 5, name = "five" });

        Assert.Equal(1, count);
        Assert.Equal((5, "five"),
            connection.QuerySingle<(int, string)>("SELECT ID, NAME FROM PARENT"));
    }

    /// <summary>TestMergeUsing.testDataChangeDeltaTable: FINAL TABLE over a merge answers
    /// the rows the merge touched, and the clauses collect their old and new rows</summary>
    [Fact]
    public void FinalTableAnswersTheMergedRows()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT PRIMARY KEY, C INTEGER) AS (VALUES (1, 2), (2, 3), (3, 4))");

        List<int> ids = connection.Query<int>(
            "SELECT TEST.ID FROM FINAL TABLE ( " +
            "MERGE INTO TEST USING (SELECT T.ID, T.C FROM (SELECT 1, 3) T(ID, C)) T ON TEST.ID = T.ID " +
            "WHEN MATCHED AND TEST.ID = 1 THEN UPDATE SET C = T.C " +
            "WHEN NOT MATCHED THEN INSERT (ID, C) VALUES (T.ID, T.C)" +
            ") TEST").AsList();
        Assert.Equal([1], ids);
        Assert.Equal(3, connection.QuerySingle<int>("SELECT C FROM TEST WHERE ID = 1"));
    }

    /// <summary>OLD and NEW tables answer the rows before and after the change</summary>
    [Fact]
    public void OldAndNewTablesAnswerTheDelta()
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT PRIMARY KEY, C INTEGER) AS (VALUES (1, 2), (2, 3))");

        List<int> oldRows = connection.Query<int>(
            "SELECT X.ID FROM OLD TABLE ( " +
            "MERGE INTO TEST USING (SELECT 1 AS ID) T ON TEST.ID = T.ID " +
            "WHEN MATCHED THEN DELETE " +
            ") X").AsList();
        Assert.Equal([1], oldRows);

        List<int> newRows = connection.Query<int>(
            "SELECT X.ID FROM NEW TABLE ( " +
            "MERGE INTO TEST USING (SELECT 7 AS ID) T ON TEST.ID = T.ID " +
            "WHEN NOT MATCHED THEN INSERT (ID) VALUES (T.ID) " +
            ") X").AsList();
        Assert.Equal([7], newRows);
        Assert.Equal([2, 7], connection.Query<int>("SELECT ID FROM TEST ORDER BY ID").AsList());
    }
}
