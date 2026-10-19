// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/grammar.html — conditions and predicates, one test per checklist row.
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class ConditionTests
{
    /// <summary>docs/grammar.html:2321 — operand [ conditionRightHandSide ]</summary>
    [Fact]
    public void Condition_BareOperand_IsABooleanValue() // GRM-2321
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE TRUE"));
    }

    /// <summary>docs/grammar.html:2322 — | NOT condition</summary>
    [Fact]
    public void Condition_Not_NegatesTheCondition() // GRM-2322
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NOT (ID = 1)"));
    }

    /// <summary>docs/grammar.html:2323 — | EXISTS ( query )</summary>
    [Fact]
    public void Condition_Exists_ChecksTheSubquery() // GRM-2323
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM TEST WHERE EXISTS (SELECT 1 FROM TEST T2 WHERE T2.ID = TEST.ID AND T2.NAME = 'Ada')"));
    }

    /// <summary>docs/grammar.html:2324 — | UNIQUE [ nullsDistinct ] ( query )</summary>
    [Fact]
    public void Condition_Unique_ChecksForDuplicates() // GRM-2324
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.True(connection.QuerySingle<bool>("SELECT UNIQUE (SELECT ID FROM TEST)"));
    }

    /// <summary>docs/grammar.html:2325 — | INTERSECTS (operand, operand)</summary>
    [Fact]
    public void Condition_Intersects_ComparesTwoArrays() // GRM-2325
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT INTERSECTS(ARRAY[1, 2], ARRAY[2, 3])"));
    }

    /// <summary>docs/grammar.html:2347 — comparisonRightHandSide</summary>
    [Fact]
    public void ConditionRightHandSide_Comparison_IsAccepted() // GRM-2347
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE ID = 1"));
    }

    /// <summary>docs/grammar.html:2348 — | quantifiedComparisonRightHandSide</summary>
    [Fact]
    public void ConditionRightHandSide_QuantifiedComparison_IsAccepted() // GRM-2348
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM TEST WHERE ID = ANY (ARRAY[1, 2])"));
    }

    /// <summary>docs/grammar.html:2349 — | nullPredicateRightHandSide</summary>
    [Fact]
    public void ConditionRightHandSide_NullPredicate_IsAccepted() // GRM-2349
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME IS NOT NULL"));
    }

    /// <summary>docs/grammar.html:2350 — | distinctPredicateRightHandSide</summary>
    [Fact]
    public void ConditionRightHandSide_DistinctPredicate_IsAccepted() // GRM-2350
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM TEST WHERE ID IS DISTINCT FROM 1"));
    }

    /// <summary>docs/grammar.html:2351 — | quantifiedDistinctPredicateRightHandSide</summary>
    [Fact]
    public void ConditionRightHandSide_QuantifiedDistinctPredicate_IsAccepted() // GRM-2351
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM TEST WHERE ID IS DISTINCT FROM ANY (ARRAY[1])"));
    }

    /// <summary>docs/grammar.html:2352 — | booleanTestRightHandSide</summary>
    [Fact]
    public void ConditionRightHandSide_BooleanTest_IsAccepted() // GRM-2352
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE (ID = 1) IS TRUE"));
        Assert.True(connection.QuerySingle<bool>("SELECT (NULL = 1) IS UNKNOWN"));
    }

    /// <summary>docs/grammar.html:2353 — | typePredicateRightHandSide</summary>
    [Fact]
    public void ConditionRightHandSide_TypePredicate_IsAccepted() // GRM-2353
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT 1 IS OF (INTEGER)"));
    }

    /// <summary>docs/grammar.html:2354 — | jsonPredicateRightHandSide</summary>
    [Fact]
    public void ConditionRightHandSide_JsonPredicate_IsAccepted() // GRM-2354
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT JSON '{\"a\": 1}' IS JSON OBJECT"));
    }

    /// <summary>docs/grammar.html:2355 — | betweenPredicateRightHandSide</summary>
    [Fact]
    public void ConditionRightHandSide_BetweenPredicate_IsAccepted() // GRM-2355
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE ID BETWEEN 1 AND 2"));
        Assert.True(connection.QuerySingle<bool>("SELECT 2 BETWEEN SYMMETRIC 3 AND 1"));
    }

    /// <summary>docs/grammar.html:2356 — | inPredicateRightHandSide</summary>
    [Fact]
    public void ConditionRightHandSide_InPredicate_IsAccepted() // GRM-2356
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE ID IN (1, 2)"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE ID IN (SELECT 1)"));
    }

    /// <summary>docs/grammar.html:2357 — | likePredicateRightHandSide</summary>
    [Fact]
    public void ConditionRightHandSide_LikePredicate_IsAccepted() // GRM-2357
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME LIKE 'A%'"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME ILIKE 'ada'"));
        Assert.True(connection.QuerySingle<bool>("SELECT 'a_b' LIKE 'a\\_b' ESCAPE '\\'"));
    }

    /// <summary>docs/grammar.html:2358 — | regexpPredicateRightHandSide</summary>
    [Fact]
    public void ConditionRightHandSide_RegexpPredicate_IsAccepted() // GRM-2358
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME REGEXP 'A.*'"));
    }

    /// <summary>docs/grammar.html:2387 — compare operand</summary>
    [Fact]
    public void ComparisonRightHandSide_ComparesTwoOperands() // GRM-2387
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE 1 < ID"));
    }

    /// <summary>docs/grammar.html:2405 — compare { ALL | ANY | SOME } ( { query | { array } } )</summary>
    [Fact]
    public void QuantifiedComparison_AllAndSome_AreAccepted() // GRM-2405
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM TEST WHERE ID <> ALL (ARRAY[1, 3])"));
        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM TEST WHERE ID = SOME (ARRAY[1])"));
    }

    /// <summary>docs/grammar.html:2423 — IS [ NOT ] NULL</summary>
    [Fact]
    public void NullPredicate_IsNotNull_KeepsTheRows() // GRM-2423
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME IS NOT NULL"));
    }

    /// <summary>docs/grammar.html:2441 — IS [ NOT ] [ DISTINCT FROM ] operand</summary>
    [Fact]
    public void DistinctPredicate_NotDistinctFrom_MatchesTheRow() // GRM-2441
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM TEST WHERE ID IS NOT DISTINCT FROM 1"));
    }

    /// <summary>docs/grammar.html:2459 — IS [ NOT ] [ DISTINCT FROM ] { ALL | ANY | SOME } ( { query | array } )</summary>
    [Fact]
    public void QuantifiedDistinctPredicate_Any_MatchesTheArray() // GRM-2459
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM TEST WHERE ID IS NOT DISTINCT FROM ANY (ARRAY[1, 2])"));
    }

    /// <summary>docs/grammar.html:2477 — IS [ NOT ] { TRUE | FALSE | UNKNOWN }</summary>
    [Fact]
    public void BooleanTest_TrueFalseUnknown_AreChecked() // GRM-2477
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT (1 = 1) IS TRUE"));
        Assert.True(connection.QuerySingle<bool>("SELECT (1 = 2) IS FALSE"));
        Assert.True(connection.QuerySingle<bool>("SELECT (NULL = 1) IS UNKNOWN"));
    }

    /// <summary>docs/grammar.html:2495 — IS [ NOT ] OF (dataType [,...])</summary>
    [Fact]
    public void TypePredicate_OneOfTheTypes_IsAccepted() // GRM-2495
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT 1 IS OF (INTEGER, BIGINT)"));
    }

    /// <summary>docs/grammar.html:2513 — IS [ NOT ] JSON [ VALUE | ARRAY | OBJECT | SCALAR ]</summary>
    [Fact]
    public void JsonPredicate_KindIsChecked() // GRM-2513
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT JSON '[1, 2]' IS JSON ARRAY"));
        Assert.False(connection.QuerySingle<bool>("SELECT JSON '[1, 2]' IS JSON OBJECT"));
        Assert.True(connection.QuerySingle<bool>("SELECT JSON '1' IS JSON SCALAR"));
    }

    /// <summary>docs/grammar.html:2514 — [ [ WITH | WITHOUT ] UNIQUE [ KEYS ] ]</summary>
    [Fact]
    public void JsonPredicate_UniqueKeys_AreChecked() // GRM-2514
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>(
            "SELECT JSON '{\"a\": 1, \"b\": 2}' IS JSON OBJECT WITH UNIQUE KEYS"));
    }

    /// <summary>docs/grammar.html:2533 — [ NOT ] BETWEEN [ ASYMMETRIC | SYMMETRIC ] operand AND operand</summary>
    [Fact]
    public void BetweenPredicate_AsymmetricSymmetricAndNot_AreAccepted() // GRM-2533
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM TEST WHERE ID BETWEEN ASYMMETRIC 1 AND 1"));
        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM TEST WHERE ID NOT BETWEEN 2 AND 3"));
    }

    /// <summary>docs/grammar.html:2551 — [ NOT ] IN ( { query | expression [,...] } )</summary>
    [Fact]
    public void InPredicate_ListSubqueryAndNot_AreAccepted() // GRM-2551
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE ID IN (1, 3)"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE ID IN (SELECT 2)"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE ID NOT IN (1, 3)"));
    }

    /// <summary>docs/grammar.html:2569 — [ NOT ] { LIKE | { ILIKE } } operand [ ESCAPE string ]</summary>
    [Fact]
    public void LikePredicate_NotLikeAndEscape_AreAccepted() // GRM-2569
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME NOT LIKE 'A%'"));
        Assert.True(connection.QuerySingle<bool>("SELECT '100%' LIKE '100\\%' ESCAPE '\\'"));
    }

    /// <summary>docs/grammar.html:2587 — { [ NOT ] REGEXP operand }</summary>
    [Fact]
    public void RegexpPredicate_NotRegexp_IsAccepted() // GRM-2587
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM TEST WHERE NAME NOT REGEXP 'A.*'"));
    }

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");
    }
}
