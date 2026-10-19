// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/commands.html — MERGE INTO and MERGE USING commands, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Commands;

public sealed class MergeCommandTests
{
    /// <summary>docs/commands.html:893 — MERGE INTO [schemaName.]tableName [ ( columnName [,...] ) ]</summary>
    [Fact]
    public void MergeInto_ColumnList_UpdatesTheRowWithTheSameKey() // CMD-0893
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT PRIMARY KEY, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");

        _ = connection.Execute("MERGE INTO TEST(ID, NAME) VALUES (2, 'Grace')");

        Assert.Equal("Grace", connection.QuerySingle<string>("SELECT NAME FROM TEST WHERE ID = 2"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST"));
    }

    /// <summary>docs/commands.html:894 — [ KEY ( columnName [,...] ) ]</summary>
    [Fact]
    public void MergeInto_KeyClause_FindsTheRowByItsKey() // CMD-0894
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");

        _ = connection.Execute("MERGE INTO TEST KEY(ID) VALUES (2, 'Grace')");

        Assert.Equal("Grace", connection.QuerySingle<string>("SELECT NAME FROM TEST WHERE ID = 2"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST"));
    }

    /// <summary>docs/commands.html:895 — { insertValues | query }</summary>
    [Fact]
    public void MergeInto_Query_UpdatesFromTheSelectResult() // CMD-0895
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");
        _ = connection.Execute("CREATE TABLE SOURCE(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO SOURCE(ID, NAME) VALUES (2, 'Grace')");

        _ = connection.Execute("MERGE INTO TEST KEY(ID) SELECT ID, NAME FROM SOURCE");

        Assert.Equal("Grace", connection.QuerySingle<string>("SELECT NAME FROM TEST WHERE ID = 2"));
    }

    /// <summary>docs/commands.html:916 — MERGE INTO [schemaName.]targetTableName [ [AS] targetAlias]</summary>
    [Fact]
    public void MergeUsing_TargetAlias_UpdatesAndInsertsThroughTheAlias() // CMD-0916
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTargetAndSource(connection);

        _ = connection.Execute(
            "MERGE INTO TARGET AS T USING SOURCE AS S ON T.ID = S.ID " +
            "WHEN MATCHED THEN UPDATE SET T.NAME = S.NAME " +
            "WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)");

        Assert.Equal("Grace", connection.QuerySingle<string>("SELECT NAME FROM TARGET WHERE ID = 1"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TARGET"));
    }

    /// <summary>docs/commands.html:917 — USING tableExpression</summary>
    [Fact]
    public void MergeUsing_DerivedTable_CanBeTheSource() // CMD-0917
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTargetAndSource(connection);

        _ = connection.Execute(
            "MERGE INTO TARGET AS T USING (SELECT ID, NAME FROM SOURCE) AS S ON T.ID = S.ID " +
            "WHEN MATCHED THEN UPDATE SET T.NAME = S.NAME " +
            "WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)");

        Assert.Equal("Grace", connection.QuerySingle<string>("SELECT NAME FROM TARGET WHERE ID = 1"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TARGET"));
    }

    /// <summary>docs/commands.html:918 — ON expression</summary>
    [Fact]
    public void MergeUsing_OnExpression_DecidesWhichRowsMatch() // CMD-0918
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTargetAndSource(connection);

        _ = connection.Execute(
            "MERGE INTO TARGET AS T USING SOURCE AS S ON T.ID = S.ID AND T.ID > 0 " +
            "WHEN MATCHED THEN UPDATE SET T.NAME = S.NAME " +
            "WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)");

        Assert.Equal("Grace", connection.QuerySingle<string>("SELECT NAME FROM TARGET WHERE ID = 1"));
        Assert.Equal("Alan", connection.QuerySingle<string>("SELECT NAME FROM TARGET WHERE ID = 2"));
    }

    /// <summary>docs/commands.html:919 — mergeWhenClause [,...]</summary>
    [Fact]
    public void MergeUsing_WhenClauses_UpdateDeleteAndInsertInOneStatement() // CMD-0919
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TARGET(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TARGET(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TARGET(ID, NAME) VALUES (2, 'Keep')");
        _ = connection.Execute("CREATE TABLE SOURCE(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO SOURCE(ID, NAME) VALUES (1, 'Grace')");
        _ = connection.Execute("INSERT INTO SOURCE(ID, NAME) VALUES (2, 'drop')");
        _ = connection.Execute("INSERT INTO SOURCE(ID, NAME) VALUES (3, 'Alan')");

        _ = connection.Execute(
            "MERGE INTO TARGET AS T USING SOURCE AS S ON T.ID = S.ID " +
            "WHEN MATCHED AND S.NAME = 'drop' THEN DELETE " +
            "WHEN MATCHED THEN UPDATE SET T.NAME = S.NAME " +
            "WHEN NOT MATCHED THEN INSERT (ID, NAME) VALUES (S.ID, S.NAME)");

        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TARGET"));
        Assert.Equal("Grace", connection.QuerySingle<string>("SELECT NAME FROM TARGET WHERE ID = 1"));
        Assert.Equal("Alan", connection.QuerySingle<string>("SELECT NAME FROM TARGET WHERE ID = 3"));
    }

    private static void GivenTargetAndSource(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TARGET(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TARGET(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("CREATE TABLE SOURCE(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO SOURCE(ID, NAME) VALUES (1, 'Grace')");
        _ = connection.Execute("INSERT INTO SOURCE(ID, NAME) VALUES (2, 'Alan')");
    }
}
