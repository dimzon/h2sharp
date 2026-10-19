// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/grammar.html — query shapes, one test per checklist row.
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class QueryGrammarTests
{
    /// <summary>docs/grammar.html:3067 — select | explicitTable | tableValue</summary>
    [Fact]
    public void Query_SelectTableAndValues_AllReturnRows() // GRM-3067
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Single(connection.Query("SELECT 1 AS ID").AsList());
        Assert.Equal(2, connection.Query("TABLE TEST").AsList().Count);
        Assert.Equal(2, connection.Query("VALUES (1, 2), (3, 4)").AsList().Count);
    }

    /// <summary>docs/grammar.html:3201 — { expression | { int } } [ ASC | DESC ] [ NULLS { FIRST | LAST } ]</summary>
    [Fact]
    public void SelectOrder_PositionExpressionAndNulls_AreAccepted() // GRM-3201
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (NULL, 'Nobody')");

        Assert.Equal(2, connection.QueryFirst<int>("SELECT ID FROM TEST ORDER BY 1 DESC NULLS LAST"));
        Assert.Null(connection.QueryFirst<int?>("SELECT ID FROM TEST ORDER BY ID ASC NULLS FIRST"));
    }

    /// <summary>docs/grammar.html:3219 — ROW (expression, [,...])</summary>
    [Fact]
    public void RowValueExpression_RowConstructor_MakesARow() // GRM-3219
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.Query("VALUES ROW(1, 2), ROW(3, 4)").AsList().Count);
    }

    /// <summary>docs/grammar.html:3220 — | ( [ expression, expression [,...] ] )</summary>
    [Fact]
    public void RowValueExpression_ParenthesizedList_IsARow() // GRM-3220
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT (1, 2) = (1, 2)"));
    }

    /// <summary>docs/grammar.html:3221 — | expression</summary>
    [Fact]
    public void RowValueExpression_PlainExpression_IsARow() // GRM-3221
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.Query("VALUES 1, 2, 3").AsList().Count);
    }

    /// <summary>docs/grammar.html:3241 — wildcardExpression | expression [ [ AS ] columnAlias ]</summary>
    [Fact]
    public void SelectExpression_WildcardAndAlias_AreAccepted() // GRM-3241
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        IDictionary<string, object> row = (IDictionary<string, object>)connection
            .Query("SELECT ID AS DOCUMENT_ID FROM TEST").First();
        Assert.True(row.ContainsKey("DOCUMENT_ID"));

        Assert.Equal(2, connection.Query("SELECT * FROM TEST").AsList().Count);
    }

    /// <summary>docs/grammar.html:3513 — [[schemaName.]tableAlias.]*</summary>
    [Fact]
    public void WildcardExpression_QualifiedStar_ReturnsAllColumns() // GRM-3513
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.Query("SELECT T.* FROM TEST T").AsList().Count);
    }

    /// <summary>docs/grammar.html:3514 — [EXCEPT ([[schemaName.]tableAlias.]columnName, [,...])]</summary>
    [Fact]
    public void WildcardExpression_Except_DropsTheColumn() // GRM-3514
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        IDictionary<string, object> row = (IDictionary<string, object>)connection
            .Query("SELECT * EXCEPT (NAME) FROM TEST").First();

        Assert.True(row.ContainsKey("ID"));
        Assert.False(row.ContainsKey("NAME"));
    }

    /// <summary>docs/grammar.html:3013 — { { A-Z|_ } [ { A-Z|_|0-9 } [...] ] } | quotedName</summary>
    [Fact]
    public void Name_UnquotedIdentifier_IsUppercased() // GRM-3013
    {
        using IDbConnection connection = TestDatabase.Open();

        _ = connection.Execute("CREATE TABLE Test_Name(ID INT)");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TEST_NAME'"));
    }

    /// <summary>docs/grammar.html:3085 — "anything"</summary>
    [Fact]
    public void QuotedName_KeepsTheSpaces() // GRM-3085
    {
        using IDbConnection connection = TestDatabase.Open();

        IDictionary<string, object> row = (IDictionary<string, object>)connection
            .Query("SELECT 1 AS \"Quoted Name\"").First();

        Assert.True(row.ContainsKey("Quoted Name"));
    }

    /// <summary>docs/grammar.html:3086 — | U&amp;"anything" [ UESCAPE 'anything' ]</summary>
    [Fact]
    public void QuotedName_UnicodeEscape_IsDecoded() // GRM-3086
    {
        using IDbConnection connection = TestDatabase.Open();

        IDictionary<string, object> row = (IDictionary<string, object>)connection
            .Query("SELECT 1 AS U&\"d|0061ta\" UESCAPE '|'").First();

        Assert.True(row.ContainsKey("data"));
    }

    /// <summary>docs/grammar.html:2267 — bracketedComment | -- anything | // anything</summary>
    [Fact]
    public void Comment_AllThreeForms_AreIgnored() // GRM-2267
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT 1 /* comment */"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT 2 -- comment"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT 3 // comment"));
    }

    /// <summary>docs/grammar.html:2285 — /* [ [ bracketedComment ] [ anything ] [...] ] */</summary>
    [Fact]
    public void BracketedComment_MayNest() // GRM-2285
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT 1 /* outer /* inner */ still outer */"));
    }

    /// <summary>docs/grammar.html:2933 — ON expression | USING (columnName [,...])</summary>
    [Fact]
    public void JoinSpecification_OnAndUsing_AreAccepted() // GRM-2933
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenJoinedTables(connection);

        Assert.Equal("Ada", connection.QuerySingle<string>(
            "SELECT T2.NAME FROM T1 JOIN T2 ON T1.PID = T2.ID WHERE T1.ID = 2"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM T1 JOIN T2 USING(ID)"));
    }

    /// <summary>docs/grammar.html:3443 — { [ schemaName. ] tableName</summary>
    [Fact]
    public void TableExpression_QualifiedTableName_IsRead() // GRM-3443
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM PUBLIC.TEST"));
    }

    /// <summary>docs/grammar.html:3444 — | ( query )</summary>
    [Fact]
    public void TableExpression_ParenthesizedQuery_IsRead() // GRM-3444
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM (SELECT * FROM TEST WHERE ID > 1) T"));
    }

    /// <summary>docs/grammar.html:3445 — | unnest</summary>
    [Fact]
    public void TableExpression_Unnest_ExpandsTheArray() // GRM-3445
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT COUNT(*) FROM UNNEST(ARRAY[1, 2, 3])"));
    }

    /// <summary>docs/grammar.html:3446 — | table</summary>
    [Fact]
    public void TableExpression_ExplicitTable_IsRead() // GRM-3446
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM (TABLE TEST)"));
    }

    /// <summary>docs/grammar.html:3447 — | dataChangeDeltaTable }</summary>
    [Fact]
    public void TableExpression_DataChangeDelta_ReturnsTheChangedRows() // GRM-3447
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM FINAL TABLE (INSERT INTO TEST(ID, NAME) VALUES (3, 'Grace'))"));
    }

    /// <summary>docs/grammar.html:3448 — [ [ AS ] newTableAlias [ ( columnName [,...] ) ] ]</summary>
    [Fact]
    public void TableExpression_AliasWithColumnList_RenamesTheColumns() // GRM-3448
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT X.A FROM (SELECT 1 AS OLD_NAME) AS X(A)"));
    }

    /// <summary>docs/grammar.html:3449 — [ USE INDEX ([ indexName [,...] ]) ]</summary>
    [Fact]
    public void TableExpression_UseIndex_HintsTheIndex() // GRM-3449
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("CREATE INDEX IDXNAME ON TEST(NAME)");

        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST USE INDEX (IDXNAME)"));
    }

    /// <summary>docs/grammar.html:3450 — [ { { LEFT | RIGHT } [ OUTER ] | [ INNER ] | CROSS | NATURAL }</summary>
    [Fact]
    public void TableExpression_JoinKinds_AreAccepted() // GRM-3450
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenJoinedTables(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM T1 LEFT OUTER JOIN T2 ON T1.PID = T2.ID"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM T1 RIGHT JOIN T2 ON T1.PID = T2.ID"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM T1 CROSS JOIN T2"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM T1 NATURAL JOIN T2"));
    }

    /// <summary>docs/grammar.html:3451 — JOIN tableExpression [ joinSpecification ] ]</summary>
    [Fact]
    public void TableExpression_InnerJoin_IsAccepted() // GRM-3451
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenJoinedTables(connection);

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM T1 INNER JOIN T2 USING(ID)"));
    }

    /// <summary>docs/grammar.html:3695 — [[schemaName.]tableAlias.] { columnName | { _ROWID_ } }</summary>
    [Fact]
    public void Column_RowIdPseudoColumn_IsReadable() // GRM-3695
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.Query("SELECT T._ROWID_ FROM TEST AS T").AsList().Count);
    }

    /// <summary>docs/grammar.html:2869 — columnName [ ASC | DESC ] [ NULLS { FIRST | LAST } ]</summary>
    [Fact]
    public void IndexColumn_OrderAndNulls_AreAccepted() // GRM-2869
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        _ = connection.Execute("CREATE INDEX IDXNAME ON TEST(NAME DESC NULLS LAST)");

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.INDEXES WHERE INDEX_NAME = 'IDXNAME'"));
    }

    /// <summary>docs/grammar.html:3389 — expression [ ASC | DESC ] [ NULLS { FIRST | LAST } ]</summary>
    [Fact]
    public void SortSpecification_DescNullsLast_IsHonoured() // GRM-3389
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (3, NULL)");

        Assert.Equal("Alan", connection.QueryFirst<string>(
            "SELECT NAME FROM TEST ORDER BY NAME DESC NULLS LAST"));
    }

    /// <summary>docs/grammar.html:3407 — sortSpecification [,...]</summary>
    [Fact]
    public void SortSpecificationList_SecondKeyBreaksTheTies() // GRM-3407
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Zoe')");

        Assert.Equal("Zoe", connection.QueryFirst<string>(
            "SELECT NAME FROM TEST ORDER BY ID DESC, NAME ASC"));
    }

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");
    }

    private static void GivenJoinedTables(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE T1(ID INT, PID INT)");
        _ = connection.Execute("CREATE TABLE T2(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO T1(ID, PID) VALUES (1, NULL)");
        _ = connection.Execute("INSERT INTO T1(ID, PID) VALUES (2, 1)");
        _ = connection.Execute("INSERT INTO T2(ID, NAME) VALUES (1, 'Ada')");
    }
}
