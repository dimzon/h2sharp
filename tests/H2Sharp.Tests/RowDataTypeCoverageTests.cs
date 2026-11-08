/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of RowDataType: the accessors, the null comparison, the equality
 * branches and the hash code.
 */
using H2Sharp.Engine;
using H2Sharp.MvStore.Db;
using H2Sharp.Result;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class RowDataTypeCoverageTests
{
    [Fact]
    public void RowDataTypeAnswersItsSurface()
    {
        RowDataType type = new(
            provider: null,
            compareMode: CompareMode.GetInstance(null, 0),
            handler: null,
            sortTypes: null,
            indexes: null,
            columnCount: 2,
            storeKeys: true);

        Assert.Null(type.GetIndexes());
        Assert.Equal(2, type.GetColumnCount());
        Assert.True(type.IsStoreKeys());
        Assert.NotNull(type.GetValueDataType());

        // Compare with null: a null left side is smaller.
        SearchRow row = Row.Get(new Value?[] { ValueInteger.Get(1) }, 1);
        Assert.Equal(-1, type.Compare(null, row));
        Assert.Equal(1, type.Compare(row, null));

        // Equals: same instance, different type, different column count, different indexes.
        Assert.True(type.Equals(type));
        Assert.False(type.Equals(null));
        Assert.False(type.Equals("x"));
        Assert.False(type.Equals(new RowDataType(null, CompareMode.GetInstance(null, 0), null, null, null, 3, true)));
        Assert.False(type.Equals(new RowDataType(null, CompareMode.GetInstance(null, 0), null, new[] { 1 }, null, 2, true)));
        Assert.True(type.Equals(new RowDataType(null, CompareMode.GetInstance(null, 0), null, null, null, 2, true)));

        // GetHashCode runs the HashOf (null) branch.
        _ = type.GetHashCode();
    }
}
