/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 104: the ordering the row type gives stored values — rows as search rows,
 * compound values, NULL operands, the NULLS FIRST/LAST masks and descending columns.
 */
using H2Sharp.MvStore.Db;
using H2Sharp.Result;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ValueDataTypeCompareTests
{
    private static ValueDataType NewType(params int[] sortTypes)
        => new(null, CompareMode.GetInstance(null, 0), null, sortTypes);

    [Fact]
    public void CompoundValuesCompareColumnByColumn()
    {
        ValueDataType type = NewType(SortOrder.Ascending, SortOrder.Descending);

        ValueRow first = ValueRow.Get([ValueInteger.Get(1), ValueInteger.Get(2)]);
        ValueRow same = ValueRow.Get([ValueInteger.Get(1), ValueInteger.Get(2)]);
        ValueRow later = ValueRow.Get([ValueInteger.Get(1), ValueInteger.Get(3)]);
        ValueRow earlier = ValueRow.Get([ValueInteger.Get(0), ValueInteger.Get(9)]);
        ValueRow shorter = ValueRow.Get([ValueInteger.Get(1)]);

        Assert.Equal(0, type.Compare(first, same));
        // The second column is descending, so the larger value sorts first.
        Assert.True(type.Compare(first, later) > 0);
        Assert.True(type.Compare(first, earlier) > 0);
        Assert.True(type.Compare(first, shorter) < 0);

        // A NULL inside the pair leaves the comparison to the last values.
        ValueRow withNull = ValueRow.Get([ValueNull.Instance, ValueInteger.Get(1)]);
        ValueRow other = ValueRow.Get([ValueInteger.Get(2), ValueInteger.Get(5)]);
        _ = type.Compare(withNull, other);
    }

    [Fact]
    public void SearchRowsAndNullOperandsCompare()
    {
        ValueDataType type = NewType(SortOrder.Ascending, SortOrder.Descending);

        SearchRow a = new DefaultRow([ValueInteger.Get(1), ValueInteger.Get(5)]);
        SearchRow b = new DefaultRow([ValueInteger.Get(1), ValueInteger.Get(6)]);
        SearchRow c = new DefaultRow([ValueInteger.Get(2), ValueInteger.Get(0)]);
        Assert.Equal(0, type.Compare(a, a));
        Assert.True(type.Compare(a, b) > 0);
        Assert.True(type.Compare(a, c) < 0);

        Assert.Equal(0, type.Compare(null, null));
        Assert.Equal(-1, type.Compare(null, ValueInteger.Get(1)));
        Assert.Equal(1, type.Compare(ValueInteger.Get(1), null));
        // Plain values fall back to a single ascending comparison.
        Assert.True(type.Compare(ValueInteger.Get(1), ValueInteger.Get(2)) < 0);
        Assert.True(type.Compare(ValueInteger.Get(2), ValueInteger.Get(1)) > 0);
    }

    [Fact]
    public void NullsTakeTheMaskedOrder()
    {
        ValueDataType type = NewType(SortOrder.Ascending);

        Assert.Equal(0, type.CompareValues(ValueNull.Instance, ValueNull.Instance, SortOrder.Ascending));
        Assert.Equal(
            0,
            type.CompareValues(ValueInteger.Get(1), ValueInteger.Get(1), SortOrder.Ascending));
        Assert.True(
            type.CompareValues(ValueNull.Instance, ValueInteger.Get(1), SortOrder.Ascending) < 0);
        Assert.True(
            type.CompareValues(ValueNull.Instance, ValueInteger.Get(1), SortOrder.NullsFirst) < 0);
        Assert.True(
            type.CompareValues(ValueNull.Instance, ValueInteger.Get(1), SortOrder.NullsLast) > 0);
        Assert.True(
            type.CompareValues(ValueInteger.Get(1), ValueNull.Instance, SortOrder.NullsFirst) > 0);
        Assert.True(
            type.CompareValues(ValueInteger.Get(2), ValueInteger.Get(1), SortOrder.Descending) < 0);
    }
}
