/*
 * Sprint 244: the direct LIKE matcher the SQL condition delegates to, and the table value
 * constructor in its row and TABLE(...) spellings.
 */
using H2Sharp.Expressions;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class TableValueConstructorOracleTests : ParserCoverageOracleBase
{
    [JavaOracleFact]
    public void ValueRowsInEveryShape()
    {
        AssertSameAsReference("table_value_ctor",
            "SELECT * FROM VALUES (1, 'a'), (2, 'b')",
            "VALUES ROW(1), ROW(2)",
            "SELECT * FROM (VALUES (1, 2)) V(A, B)",
            "SELECT 1 WHERE 1 IN (VALUES 1, 2)",
            "CREATE TABLE T(A INT)",
            "INSERT INTO T VALUES (1), (2)",
            "SELECT * FROM T ORDER BY A");
    }
}

public sealed class PatternAndValuesSurfaceTests
{
    [Fact]
    public void DirectLikeMatcherCoversShortcutsAndInvalidPatterns()
    {
        CompareLike like = new(CompareMode.GetInstance(null, 0), ValueExpression.Null, false, false,
            ValueExpression.Null, null, CompareLike.LikeType.Like);
        Assert.True(like.Test("ab%", "abcdef", '\\'));
        Assert.True(like.Test("%ef", "abcdef", '\\'));
        Assert.True(like.Test("%cd%", "abcdef", '\\'));
        Assert.True(like.Test("a_c", "abc", '\\'));
        Assert.True(like.Test("a\\%c", "a%c", '\\'));
        Assert.False(like.Test("a\\", "a", '\\'));
        Assert.Equal(2, like.GetSubexpressionCount());
        Assert.Equal(3, new CompareLike(CompareMode.GetInstance(null, 0), ValueExpression.Null,
            false, false, ValueExpression.Null, ValueExpression.Null,
            CompareLike.LikeType.Like).GetSubexpressionCount());
    }
}
