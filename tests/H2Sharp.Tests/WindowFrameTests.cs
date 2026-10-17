/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The window frame data layer: the units, bound types, exclusion clauses and a bound, pinned
 * by their SQL rendering. The frame navigation and the OVER reader that would use them are the
 * next increment of the window epic.
 */
using System.Text;

using H2Sharp.Expressions.Analysis;
using Xunit;

namespace H2Sharp.Tests;

public sealed class WindowFrameTests
{
    [Fact]
    public void UnitsRenderTheirNames()
    {
        Assert.Equal("ROWS", WindowFrameUnits.Rows.GetSql());
        Assert.Equal("RANGE", WindowFrameUnits.Range.GetSql());
        Assert.Equal("GROUPS", WindowFrameUnits.Groups.GetSql());
    }

    [Fact]
    public void BoundTypesRenderTheirClauses()
    {
        Assert.Equal("UNBOUNDED PRECEDING", WindowFrameBoundType.UnboundedPreceding.GetSql());
        Assert.Equal("PRECEDING", WindowFrameBoundType.Preceding.GetSql());
        Assert.Equal("CURRENT ROW", WindowFrameBoundType.CurrentRow.GetSql());
        Assert.Equal("FOLLOWING", WindowFrameBoundType.Following.GetSql());
        Assert.Equal("UNBOUNDED FOLLOWING", WindowFrameBoundType.UnboundedFollowing.GetSql());
    }

    [Fact]
    public void ExclusionsRenderTheirClauses()
    {
        Assert.Equal("EXCLUDE CURRENT ROW", WindowFrameExclusion.ExcludeCurrentRow.GetSql());
        Assert.Equal("EXCLUDE GROUP", WindowFrameExclusion.ExcludeGroup.GetSql());
        Assert.Equal("EXCLUDE TIES", WindowFrameExclusion.ExcludeTies.GetSql());
        Assert.Equal("EXCLUDE NO OTHERS", WindowFrameExclusion.ExcludeNoOthers.GetSql());
    }

    [Fact]
    public void ExclusionGroupFlag()
    {
        Assert.False(WindowFrameExclusion.ExcludeCurrentRow.IsGroupOrNoOthers());
        Assert.True(WindowFrameExclusion.ExcludeGroup.IsGroupOrNoOthers());
        Assert.False(WindowFrameExclusion.ExcludeTies.IsGroupOrNoOthers());
        Assert.True(WindowFrameExclusion.ExcludeNoOthers.IsGroupOrNoOthers());
    }

    [Fact]
    public void BoundRendersClauseWithoutValue()
    {
        WindowFrameBound bound = new(WindowFrameBoundType.UnboundedPreceding, null);
        Assert.Equal(
            "UNBOUNDED PRECEDING",
            bound.GetSql(new StringBuilder(), following: false, 0).ToString());
        Assert.False(bound.IsParameterized());
    }
}
