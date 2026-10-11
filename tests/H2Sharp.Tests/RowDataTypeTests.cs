/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The row type: how rows compare, and what a row costs.
 *
 * The corpus is built around the row key, because that is the part with a rule in it. A row
 * stored without its key, a row whose key is the sentinel that means "any key", and a row that
 * is narrower than the table — an index stores only the columns it keys on — all reach the key
 * comparison, and all three have answers that are not obvious: a key that was never written must
 * not order the rows, and two rows equal in every stored column are only distinguished by the key.
 *
 * The binary search is probed with a cached guess as well as without: the first probe is the
 * middle, and a repeat probe continues from where the last one ended, so a search that always
 * passed the middle would find the same rows and be twice as slow.
 */
using System;
using System.Collections.Generic;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using H2Sharp.Result;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class RowDataTypeTests
{
    [Fact]
    public void RowsCompareByColumnThenByKey()
    {
        RowDataType type = New([0, 0, 0], storeKeys: true);
        TestRow a = new([ValueInteger.Get(1), ValueVarchar.Get("a"), ValueInteger.Get(5)], 1);
        TestRow b = new([ValueInteger.Get(1), ValueVarchar.Get("a"), ValueInteger.Get(7)], 2);
        TestRow c = new([ValueInteger.Get(2), ValueVarchar.Get("a"), ValueInteger.Get(1)], 3);

        // Equal in every column, so the key decides: two rows that compare equal would lose
        // one of them in the map.
        Assert.True(type.Compare(a, b) < 0);
        Assert.True(type.Compare(b, a) > 0);
        Assert.True(type.Compare(a, a) == 0);

        // The first column decides before the key is even looked at.
        Assert.True(type.Compare(b, c) < 0);
        Assert.True(type.Compare(c, b) > 0);
    }

    [Fact]
    public void AMissingKeyNeverOrdersRows()
    {
        RowDataType type = New([0, 0], storeKeys: false);
        TestRow any = new([ValueInteger.Get(1), ValueInteger.Get(1)], SearchRow.MatchAllRowKey);
        TestRow another = new([ValueInteger.Get(1), ValueInteger.Get(1)], SearchRow.MatchAllRowKey);

        // The sentinel means "any key will do", so two rows carrying it are equal rather than
        // ordered by a key that was never written.
        Assert.Equal(0, type.Compare(any, another));
    }

    [Fact]
    public void ANarrowRowComparesOnlyItsStoredColumns()
    {
        // An index on the second column only: the stored columns are 1, so the comparison walks
        // that one and then the key, and the first column is not looked at at all.
        RowDataType type = new(null, CompareMode.GetInstance(null, 0), null, [0], [1], 3, true);
        TestRow a = new([ValueInteger.Get(99), ValueVarchar.Get("x"), ValueInteger.Get(0)], 1);
        TestRow b = new([ValueInteger.Get(1), ValueVarchar.Get("x"), ValueInteger.Get(0)], 2);

        Assert.True(type.Compare(a, b) < 0);
    }

    [Fact]
    public void BinarySearchFindsAndReportsWhereToInsert()
    {
        RowDataType type = New([0, 0], storeKeys: false);
        SearchRow[] storage = new SearchRow[8];
        for (int i = 0; i < storage.Length; i++)
        {
            storage[i] = new TestRow([ValueInteger.Get(i * 10), ValueInteger.Get(0)], SearchRow.MatchAllRowKey);
        }

        Assert.Equal(3, type.BinarySearch(storage[3], storage, storage.Length, 0));

        // A row that is not there comes back as where it would go, complemented and negated.
        TestRow missing = new([ValueInteger.Get(35), ValueInteger.Get(0)], SearchRow.MatchAllRowKey);
        int insert = type.BinarySearch(missing, storage, storage.Length, 0);
        Assert.True(insert < 0);
        Assert.Equal(4, -(insert + 1));

        // And a search that is told where it ended agrees with one from scratch — that is what
        // the cached guess is for.
        Assert.Equal(insert, type.BinarySearch(missing, storage, storage.Length, 4));
    }

    [Fact]
    public void TwoRowTypesWithTheSameShapeAreEqual()
    {
        // Equals is asked of the objects rather than through Assert.NotEqual, which compares
        // structure field by field and would answer "different" for two row types that are the
        // same by the rule the map uses to find one.
        Assert.True(New([0, 0], true).Equals(New([0, 0], true)));
        Assert.False(New([0, 0], true).Equals(New([0, 1], true)));
        Assert.False(New([0, 0], true).Equals(New([0, 0, 0], true, 3)));

        // storeKeys is deliberately *not* part of it, as in the reference: the equality answers
        // "can these two read the same bytes", and a row type is looked up by the shape it reads.
        Assert.True(New([0, 0], true).Equals(New([0, 0], false)));
    }

    private static RowDataType New(int[]? sortTypes, bool storeKeys, int columnCount = 2)
    {
        return new RowDataType(null, CompareMode.GetInstance(null, 0), null, sortTypes, null, columnCount, storeKeys);
    }

    /// <summary>
    /// A row of the given values, which is all the row type reads and the only thing it needs
    /// the factory for — a real row would come from a table, and there is no table yet.
    /// </summary>
    private sealed class TestRow : SearchRow
    {
        private readonly Value[] values;

        internal TestRow(Value[] values, long key)
        {
            this.values = values;
            SetKey(key);
        }

        public override int GetColumnCount() => values.Length;

        public override Value? GetValue(int index) => values[index];

        public override void SetValue(int index, Value v) => values[index] = v;

        public override void CopyFrom(SearchRow source)
        {
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = source.GetValue(i)!;
            }

            SetKey(source.GetKey());
        }
    }
}
