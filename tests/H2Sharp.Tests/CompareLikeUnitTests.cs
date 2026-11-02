/* Copyright 2026 H2Sharp contributors. Licensed under MPL 2.0 or EPL 1.0. */
using H2Sharp.Expressions.Conditions;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;
using static H2Sharp.Expressions.Conditions.CompareLike;

namespace H2Sharp.Tests;

public sealed class CompareLikeUnitTests
{
    private static CompareLike NewLike()
        => new(CompareMode.GetInstance(null, 0), "", null, not: false,
            whenOperand: false, null, null, LikeType.Like);

    private static CompareLike NewRegexp()
        => new(CompareMode.GetInstance(null, 0), "", null, not: false,
            whenOperand: false, null, null, LikeType.Regexp);

    [Fact]
    public void PatternKindsMatch()
    {
        Assert.True(NewLike().Test("Ada", "Ada", '\\'));
        Assert.True(NewLike().Test("A%", "Ada", '\\'));
        Assert.False(NewLike().Test("A%", "Bob", '\\'));
        Assert.True(NewLike().Test("%da", "Ada", '\\'));
        Assert.True(NewLike().Test("%d%", "Ada", '\\'));
        Assert.True(NewLike().Test("A_a", "Ada", '\\'));
        Assert.False(NewLike().Test("A_a", "Ad", '\\'));
        Assert.True(NewLike().Test("", "", '\\'));
        Assert.False(NewLike().Test("", "Ada", '\\'));
    }

    [Fact]
    public void EscapeCharacterMatchesLiterally()
    {
        Assert.True(NewLike().Test("A\\%a", "A%a", '\\'));
        Assert.False(NewLike().Test("A\\%a", "Ada", '\\'));
        Assert.True(NewLike().Test("A\\_a", "A_a", '\\'));
    }

    [Fact]
    public void TrailingEscapeIsAnInvalidPattern()
    {
        Assert.False(NewLike().Test("Ada\\", "Ada\\", '\\'));
    }

    // Note: driving Test() directly only covers LIKE patterns. REGEXP answers
    // through GetValue (a substring find, as in the reference), so it is
    // covered at the SQL level instead.

    [Fact]
    public void InvalidRegexpIsRejected()
    {
        H2SharpException e = Assert.Throws<H2SharpException>(() => NewRegexp().Test("A[", "A[", '\\'));
        Assert.Equal(22025, e.H2ErrorCode);
    }
}
