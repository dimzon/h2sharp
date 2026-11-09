/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 92: the members of a compound value that SQL rarely reaches directly —
 * the cached hash (the first call computes it, the next one returns it), the
 * comparison against NULL, the ROW column-count error, and the first-NULL rule
 * that decides whether the difference belongs to the left or the right value.
 */
using H2Sharp.Api;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ValueCollectionMembersTests
{
    private static readonly CompareMode Mode = CompareMode.GetInstance(null, 0);

    [Fact]
    public void TheHashIsComputedOnceAndThenReused()
    {
        ValueArray a = ValueArray.Get([ValueInteger.Get(1), ValueNull.Instance], null);
        ValueArray b = ValueArray.Get([ValueInteger.Get(1), ValueInteger.Get(2)], null);

        int first = a.GetHashCode();
        Assert.NotEqual(0, first);
        Assert.Equal(first, a.GetHashCode());
        Assert.NotEqual(first, b.GetHashCode());
    }

    [Fact]
    public void ComparingACollectionWithNullIsUnsupported()
        => Assert.Equal(
            int.MinValue,
            ValueArray.Get([ValueInteger.Get(1)], null)
                .CompareWithNull(ValueNull.Instance, false, null, Mode));

    [Fact]
    public void RowsOfDifferentLengthAreAColumnCountError()
    {
        ValueRow shorter = ValueRow.Get([ValueInteger.Get(1)]);
        ValueRow longer = ValueRow.Get([ValueInteger.Get(1), ValueInteger.Get(2)]);

        H2SharpException e = Assert.Throws<H2SharpException>(
            () => shorter.CompareWithNull(longer, false, null, Mode));
        Assert.Equal(ErrorCode.ColumnCountDoesNotMatch, e.H2ErrorCode);
    }

    [Fact]
    public void TheFirstNullDecidesTheDifference()
    {
        ValueArray leftNull = ValueArray.Get([ValueNull.Instance, ValueInteger.Get(2)], null);
        ValueArray rightValue = ValueArray.Get([ValueInteger.Get(1), ValueInteger.Get(2)], null);
        ValueArray rightNull = ValueArray.Get([ValueNull.Instance, ValueInteger.Get(2)], null);
        ValueArray leftValue = ValueArray.Get([ValueInteger.Get(1), ValueInteger.Get(2)], null);

        // The null on the left is earlier: the answer is the left value itself.
        Assert.Same(leftNull, leftNull.GetValueWithFirstNull(rightValue));
        // The null on the right is earlier: the answer is the parameter.
        Assert.Same(rightNull, leftValue.GetValueWithFirstNull(rightNull));
        // Both start with NULL: neither is first, so there is no answer.
        Assert.Null(
            ValueArray.Get([ValueNull.Instance], null)
                .GetValueWithFirstNull(ValueArray.Get([ValueNull.Instance], null)));
    }
}
