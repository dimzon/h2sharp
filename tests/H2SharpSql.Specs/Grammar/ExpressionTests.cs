// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
//
// docs/grammar.html — expressions and terms, one test per checklist row.
using System;
using System.Data;
using Dapper;
using H2SharpSql.Specs.Infrastructure;
using Xunit;

namespace H2SharpSql.Specs.Grammar;

public sealed class ExpressionTests
{
    /// <summary>docs/grammar.html:2029 — name</summary>
    [Fact]
    public void Alias_IsUsableInOrderBy() // GRM-2029
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.QueryFirst<int>("SELECT ID AS X FROM TEST ORDER BY X DESC"));
    }

    /// <summary>docs/grammar.html:2047 — condition [ { AND condition } [...] ]</summary>
    [Fact]
    public void AndCondition_ChainsConditions() // GRM-2047
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM TEST WHERE ID > 0 AND ID > 1 AND ID < 3"));
    }

    /// <summary>docs/grammar.html:2065 — { array | json } '[' indexInt ']'</summary>
    [Fact]
    public void ArrayElementReference_IndexesArraysAndJson() // GRM-2065
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(20, connection.QuerySingle<int>("SELECT (ARRAY[10, 20])[2]"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT (ARRAY[ARRAY[1, 2], ARRAY[3, 4]])[2][1]"));
        Assert.True(connection.QuerySingle<bool>("SELECT JSON '[10, 20]'[1] IS NOT NULL"));
    }

    /// <summary>docs/grammar.html:2083 — (expression).fieldName</summary>
    [Fact]
    public void FieldReference_ReadsARowFieldAndAJsonField() // GRM-2083
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT (ROW(1, 2)).C1"));
        Assert.True(connection.QuerySingle<bool>("SELECT JSON '{\"a\": 1, \"b\": 2}'.\"b\" IS NOT NULL"));
    }

    /// <summary>docs/grammar.html:2101 — ARRAY (query)</summary>
    [Fact]
    public void ArrayValueConstructorByQuery_CollectsTheRows() // GRM-2101
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(10, connection.QuerySingle<int>(
            "SELECT (ARRAY(SELECT X FROM SYSTEM_RANGE(1, 10)))[10]"));
    }

    /// <summary>docs/grammar.html:2119 — simpleCase | searchedCase</summary>
    [Fact]
    public void CaseExpression_SimpleForm_IsEvaluated() // GRM-2119
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("a", connection.QuerySingle<string>("SELECT CASE 1 WHEN 1 THEN 'a' ELSE 'b' END"));
    }

    /// <summary>docs/grammar.html:2137 — CASE expression</summary>
    [Fact]
    public void SimpleCase_ComparesWithTheColumnValue() // GRM-2137
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal("one", connection.QuerySingle<string>(
            "SELECT CASE ID WHEN 1 THEN 'one' ELSE 'other' END FROM TEST WHERE ID = 1"));
    }

    /// <summary>docs/grammar.html:2138 — { WHEN { expression | conditionRightHandSide } [,...] THEN expression } [...]</summary>
    [Fact]
    public void SimpleCase_MultipleWhenBranches_AreCheckedInOrder() // GRM-2138
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal("two", connection.QuerySingle<string>(
            "SELECT CASE ID WHEN 1 THEN 'one' WHEN 2 THEN 'two' END FROM TEST WHERE ID = 2"));
    }

    /// <summary>docs/grammar.html:2139 — [ ELSE expression ] END</summary>
    [Fact]
    public void SimpleCase_Else_IsTheFallback() // GRM-2139
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal("other", connection.QuerySingle<string>(
            "SELECT CASE ID WHEN 1 THEN 'one' ELSE 'other' END FROM TEST WHERE ID = 2"));
    }

    /// <summary>docs/grammar.html:2159 — CASE { WHEN expression THEN expression } [...]</summary>
    [Fact]
    public void SearchedCase_FirstTrueWhenWins() // GRM-2159
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal("medium", connection.QuerySingle<string>(
            "SELECT CASE WHEN ID > 2 THEN 'big' WHEN ID > 1 THEN 'medium' ELSE 'small' END FROM TEST WHERE ID = 2"));
    }

    /// <summary>docs/grammar.html:2160 — [ ELSE expression ] END</summary>
    [Fact]
    public void SearchedCase_WithoutElse_YieldsNull() // GRM-2160
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Null(connection.QuerySingle<string>(
            "SELECT CASE WHEN ID > 100 THEN 'big' END FROM TEST WHERE ID = 2"));
    }

    /// <summary>docs/grammar.html:2179 — CAST(value AS dataTypeOrDomain [ FORMAT templateString ])</summary>
    [Fact]
    public void CastSpecification_SupportsFormatAndDomain() // GRM-2179
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE DOMAIN EMAIL AS VARCHAR(255)");

        Assert.Equal(12, connection.QuerySingle<int>(
            "SELECT HOUR(CAST('12:00:00 P.M.' AS TIME FORMAT 'HH:MI:SS A.M.'))"));
        Assert.Equal("a@b", connection.QuerySingle<string>("SELECT CAST('a@b' AS EMAIL)"));
    }

    /// <summary>docs/grammar.html:2197 — AES</summary>
    [Fact]
    public void Cipher_Aes_IsAcceptedByScript() // GRM-2197
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT)");

        int affected = connection.Execute("SCRIPT NODATA TO 'test.sql' CIPHER AES PASSWORD 'secret'");

        Assert.Equal(0, affected);
    }

    /// <summary>docs/grammar.html:2303 — &lt;&gt; | &lt;= | &gt;= | = | &lt; | &gt; | { != } | &amp;&amp;</summary>
    [Fact]
    public void Compare_AllOperators_AreAccepted() // GRM-2303
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT 1 != 2"));
        Assert.True(connection.QuerySingle<bool>("SELECT (3 >= 3) AND (2 <= 2) AND (3 > 2) AND (2 < 3)"));
        Assert.True(connection.QuerySingle<bool>("SELECT 2 = 2"));
    }

    /// <summary>docs/grammar.html:2797 — andCondition [ { OR andCondition } [...] ]</summary>
    [Fact]
    public void Expression_OrChainsConditions() // GRM-2797
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.True(connection.QuerySingle<bool>("SELECT (1 = 2) OR (1 = 1)"));
    }

    /// <summary>docs/grammar.html:2815 — term [ { { * | / | { % } } term } [...] ]</summary>
    [Fact]
    public void Factor_MultiplyDivideModulo_AreEvaluated() // GRM-2815
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(1, connection.QuerySingle<int>("SELECT 7 % 3"));
        Assert.Equal(9, connection.QuerySingle<int>("SELECT 6 / 2 * 3"));
    }

    /// <summary>docs/grammar.html:2833 — expression | (expression [, ...]) | ()</summary>
    [Fact]
    public void GroupingElement_ExpressionTupleAndEmptySet_AreAccepted() // GRM-2833
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.Query("SELECT COUNT(*) FROM TEST GROUP BY ID").AsList().Count);
        Assert.Equal(2, connection.Query("SELECT COUNT(*) FROM TEST GROUP BY (ID, NAME)").AsList().Count);
        Assert.Single(connection.Query("SELECT COUNT(*) FROM TEST GROUP BY ()").AsList());
    }

    /// <summary>docs/grammar.html:2851 — [' ' [...]] { { digit | a-f | A-F } [' ' [...]] { digit | a-f | A-F } [' ' [...]] } [...]</summary>
    [Fact]
    public void Hex_SpacesInsideBytesLiteral_AreIgnored() // GRM-2851
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(3, connection.QuerySingle<int>("SELECT OCTET_LENGTH(X'41 42 43')"));
    }

    /// <summary>docs/grammar.html:3031 — summand [ { || summand } [...] ]</summary>
    [Fact]
    public void Operand_Concatenation_WorksForStringsBytesAndArrays() // GRM-3031
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("Hi Eva", connection.QuerySingle<string>("SELECT 'Hi' || ' Eva'"));
        Assert.Equal(2, connection.QuerySingle<int>("SELECT OCTET_LENGTH(X'AB' || X'CD')"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT (ARRAY[1, 2] || 3)[3]"));
        Assert.Equal(4, connection.QuerySingle<int>("SELECT (ARRAY[1, 2] || ARRAY[3, 4])[4]"));
    }

    /// <summary>docs/grammar.html:3425 — factor [ { { + | - } factor } [...] ]</summary>
    [Fact]
    public void Summand_AdditionAndSubtraction_AreEvaluated() // GRM-3425
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(12, connection.QuerySingle<int>("SELECT 10 + 5 - 3"));
    }

    /// <summary>docs/grammar.html:3495 — WITHIN GROUP (ORDER BY sortSpecificationList)</summary>
    [Fact]
    public void WithinGroupSpecification_OrdersTheAggregateInput() // GRM-3495
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal("Alan,Ada", connection.QuerySingle<string>(
            "SELECT LISTAGG(NAME, ',') WITHIN GROUP (ORDER BY ID DESC) FROM TEST"));
    }

    /// <summary>docs/grammar.html:3633 — { value</summary>
    [Fact]
    public void Term_Value_IsUsableInArithmetic() // GRM-3633
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(5, connection.QuerySingle<int>("SELECT 2 + 3"));
    }

    /// <summary>docs/grammar.html:3634 — | column</summary>
    [Fact]
    public void Term_Column_ReadsTheValue() // GRM-3634
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal("Ada", connection.QuerySingle<string>("SELECT NAME FROM TEST WHERE ID = 1"));
    }

    /// <summary>docs/grammar.html:3635 — | ?[ int ]</summary>
    [Fact]
    public void Term_Parameter_IsBoundByPosition() // GRM-3635
    {
        using IDbConnection connection = TestDatabase.Open();
        using IDbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ?1 + ?2";
        IDbDataParameter first = command.CreateParameter();
        first.ParameterName = "first";
        first.Value = 1;
        _ = command.Parameters.Add(first);
        IDbDataParameter second = command.CreateParameter();
        second.ParameterName = "second";
        second.Value = 2;
        _ = command.Parameters.Add(second);

        Assert.Equal(3, Convert.ToInt32(command.ExecuteScalar()));
    }

    /// <summary>docs/grammar.html:3636 — | sequenceValueExpression</summary>
    [Fact]
    public void Term_SequenceValues_AreReadable() // GRM-3636
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE SEQUENCE SEQ_ID");

        Assert.Equal(1, connection.QuerySingle<int>("SELECT NEXT VALUE FOR SEQ_ID"));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT CURRENT VALUE FOR SEQ_ID"));
    }

    /// <summary>docs/grammar.html:3637 — | function</summary>
    [Fact]
    public void Term_Function_ReturnsItsValue() // GRM-3637
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("ABC", connection.QuerySingle<string>("SELECT UPPER('abc')"));
    }

    /// <summary>docs/grammar.html:3638 — | { - | + } term</summary>
    [Fact]
    public void Term_SignedTerm_KeepsTheSign() // GRM-3638
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(-5, connection.QuerySingle<int>("SELECT -5"));
        Assert.Equal(5, connection.QuerySingle<int>("SELECT +5"));
    }

    /// <summary>docs/grammar.html:3639 — | ( expression )</summary>
    [Fact]
    public void Term_ParenthesizedExpression_IsEvaluated() // GRM-3639
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(9, connection.QuerySingle<int>("SELECT (1 + 2) * 3"));
    }

    /// <summary>docs/grammar.html:3640 — | arrayElementReference</summary>
    [Fact]
    public void Term_ArrayElement_IsReadable() // GRM-3640
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(10, connection.QuerySingle<int>("SELECT (ARRAY[10, 20])[1]"));
    }

    /// <summary>docs/grammar.html:3641 — | fieldReference</summary>
    [Fact]
    public void Term_FieldReference_IsReadable() // GRM-3641
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT (ROW(1, 2)).C2"));
    }

    /// <summary>docs/grammar.html:3642 — | ( query )</summary>
    [Fact]
    public void Term_ScalarSubquery_ReturnsTheValue() // GRM-3642
    {
        using IDbConnection connection = TestDatabase.Open();
        GivenTestTable(connection);

        Assert.Equal(2, connection.QuerySingle<int>("SELECT (SELECT MAX(ID) FROM TEST)"));
    }

    /// <summary>docs/grammar.html:3643 — | caseExpression</summary>
    [Fact]
    public void Term_CaseExpression_ReturnsTheBranchValue() // GRM-3643
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal("yes", connection.QuerySingle<string>("SELECT CASE WHEN 1 = 1 THEN 'yes' END"));
    }

    /// <summary>docs/grammar.html:3644 — | castSpecification</summary>
    [Fact]
    public void Term_Cast_ConvertsTheValue() // GRM-3644
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(42, connection.QuerySingle<int>("SELECT CAST('42' AS INT)"));
    }

    /// <summary>docs/grammar.html:3645 — | userDefinedFunctionName }</summary>
    [Fact]
    public void Term_UserDefinedFunction_IsCallable() // GRM-3645
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE ALIAS MY_SQRT FOR 'java.lang.Math.sqrt'");

        Assert.Equal(3.0, connection.QuerySingle<double>("SELECT MY_SQRT(9)"));
    }

    /// <summary>docs/grammar.html:3646 — [ timeZone | intervalQualifier ]</summary>
    [Fact]
    public void Term_TimeZoneAndIntervalQualifier_AreAccepted() // GRM-3646
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(2, connection.QuerySingle<int>("SELECT EXTRACT(HOUR FROM INTERVAL '2' HOUR)"));
        Assert.True(connection.QuerySingle<bool>("SELECT TIMESTAMP '2024-01-01 10:00:00' AT LOCAL IS NOT NULL"));
    }

    /// <summary>docs/grammar.html:3677 — AT { TIME ZONE { intervalHourToMinute | intervalHourToSecond | { string } } | LOCAL }</summary>
    [Fact]
    public void TimeZone_AllForms_ConvertTheValue() // GRM-3677
    {
        using IDbConnection connection = TestDatabase.Open();
        const string zoned = "TIMESTAMP WITH TIME ZONE '2024-01-01 10:00:00Z'";

        Assert.Equal(10, connection.QuerySingle<int>(
            $"SELECT HOUR({zoned} AT TIME ZONE INTERVAL '0:00' HOUR TO MINUTE)"));
        Assert.Equal(10, connection.QuerySingle<int>(
            $"SELECT HOUR({zoned} AT TIME ZONE INTERVAL '0:00:00' HOUR TO SECOND)"));
        Assert.Equal(12, connection.QuerySingle<int>($"SELECT HOUR({zoned} AT TIME ZONE '2')"));
        Assert.Equal(10, connection.QuerySingle<int>($"SELECT HOUR({zoned} AT TIME ZONE 'UTC')"));
    }

    /// <summary>docs/grammar.html:2779 — 0-9</summary>
    [Fact]
    public void Digit_IsPartOfANumber() // GRM-2779
    {
        using IDbConnection connection = TestDatabase.Open();

        Assert.Equal(9, connection.QuerySingle<int>("SELECT 9"));
    }

    /// <summary>docs/grammar.html:3477 — columnName [ '[' int ']' [...] ]</summary>
    [Fact]
    public void UpdateTarget_ArrayElement_IsUpdated() // GRM-3477
    {
        using IDbConnection connection = TestDatabase.Open();
        _ = connection.Execute("CREATE TABLE TEST(ID INT, A INT ARRAY)");
        _ = connection.Execute("INSERT INTO TEST(ID, A) VALUES (1, ARRAY[1, 2, 3])");

        _ = connection.Execute("UPDATE TEST SET A[2] = 9 WHERE ID = 1");

        Assert.Equal(9, connection.QuerySingle<int>("SELECT A[2] FROM TEST WHERE ID = 1"));
    }

    private static void GivenTestTable(IDbConnection connection)
    {
        _ = connection.Execute("CREATE TABLE TEST(ID INT, NAME VARCHAR(50))");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (1, 'Ada')");
        _ = connection.Execute("INSERT INTO TEST(ID, NAME) VALUES (2, 'Alan')");
    }
}
