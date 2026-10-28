/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mvstore.db.MVSortedTempResult.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A sorted and/or distinct temporary result.
 */
using System;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.MvStore.Tx;
using H2Sharp.MvStore.Type;
using H2Sharp.Result;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// Sorted temporary result.
/// </summary>
internal sealed class MVSortedTempResult : MVTempResult
{
    /// <summary>
    /// Whether this is a standard DISTINCT result.
    /// </summary>
    private readonly bool distinct;

    /// <summary>
    /// Distinct indexes for DISTINCT ON results.
    /// </summary>
    private readonly int[]? distinctIndexes;

    /// <summary>
    /// Mapping of column indexes to their positions in the store, or null when not reordered.
    /// </summary>
    private readonly int[]? indexes;

    /// <summary>
    /// Map with rows as keys and counts of duplicate rows as values.
    /// </summary>
    private readonly MVMap<object, object> map;

    /// <summary>
    /// Optional index used by DISTINCT with hidden columns, or by <see cref="Contains"/>.
    /// </summary>
    private MVMap<object, object>? index;

    /// <summary>
    /// Used for DISTINCT ON in the presence of ORDER BY.
    /// </summary>
    private ValueDataType? orderedDistinctOnType;

    /// <summary>
    /// Cursor for <see cref="Next"/>.
    /// </summary>
    private Cursor<object, object>? cursor;

    /// <summary>
    /// Current value for <see cref="Next"/>, used with duplicate rows.
    /// </summary>
    private Value?[]? current;

    /// <summary>
    /// Count of remaining duplicate rows.
    /// </summary>
    private long valueCount;

    private MVSortedTempResult(MVSortedTempResult parent)
        : base(parent)
    {
        distinct = parent.distinct;
        distinctIndexes = parent.distinctIndexes;
        indexes = parent.indexes;
        map = parent.map;
        rowCount = parent.rowCount;
    }

    /// <summary>
    /// Creates a new sorted temporary result.
    /// </summary>
    /// <param name="database">the database</param>
    /// <param name="expressions">the column expressions</param>
    /// <param name="distinct">whether this result is DISTINCT</param>
    /// <param name="distinctIndexes">indexes of distinct columns for DISTINCT ON results</param>
    /// <param name="visibleColumnCount">count of visible columns</param>
    /// <param name="resultColumnCount">total count of columns including virtual columns</param>
    /// <param name="sort">the sort order, or null</param>
    public MVSortedTempResult(
        Database database,
        Expression[] expressions,
        bool distinct,
        int[]? distinctIndexes,
        int visibleColumnCount,
        int resultColumnCount,
        SortOrder? sort)
        : base(database, expressions, visibleColumnCount, resultColumnCount)
    {
        this.distinct = distinct;
        this.distinctIndexes = distinctIndexes;
        int[] sortTypes = new int[resultColumnCount];
        int[]? reorder = null;
        if (sort is not null)
        {
            // Sorting reorders the columns into the requested order and carries the sort
            // types with them. Remaining columns keep ascending order.
            reorder = new int[resultColumnCount];
            int[] columnIndexes = sort.GetQueryColumnIndexes();
            int length = columnIndexes.Length;
            BitSet used = new();
            for (int i = 0; i < length; i++)
            {
                int columnIndex = columnIndexes[i];
                used.Set(columnIndex);
                reorder[i] = columnIndex;
                sortTypes[i] = sort.GetSortTypes()[i];
            }

            int next = 0;
            for (int i = length; i < resultColumnCount; i++)
            {
                next = used.NextClearBit(next);
                reorder[i] = next;
                next++;
            }

            bool sameOrder = true;
            for (int i = 0; i < resultColumnCount; i++)
            {
                if (reorder[i] != i)
                {
                    sameOrder = false;
                    break;
                }
            }

            if (sameOrder)
            {
                reorder = null;
            }
        }

        indexes = reorder;
        ValueDataType keyType = new(
            database,
            database.GetCompareMode(),
            null,
            SortOrder.AddNullOrdering(database, sortTypes));
        // The reference's map is MVMap<ValueRow, Long>. C# generics are invariant, so the
        // object-keyed map adapter carries the same data types.
        MVMap<object, object>.Builder<object, object> builder = new MVMap<object, object>.Builder<object, object>()
            .KeyType(new ObjectMapDataType<Value>(keyType))
            .ValueType(new ObjectMapDataType<long>(LongDataType.Instance));
        map = store.OpenMap("tmp", builder);

        if (distinct && resultColumnCount != visibleColumnCount || distinctIndexes is not null)
        {
            int count;
            if (distinctIndexes is not null)
            {
                count = distinctIndexes.Length;
            }
            else
            {
                count = visibleColumnCount;
            }

            ValueDataType distinctType = new(
                database, database.GetCompareMode(), null, new int[count]);
            DataType<Value> distinctValueType = distinctIndexes is not null && sort is not null
                ? (orderedDistinctOnType = keyType)!
                : NullValueDataType.Instance;
            MVMap<object, object>.Builder<object, object> indexBuilder =
                new MVMap<object, object>.Builder<object, object>()
                    .KeyType(new ObjectMapDataType<Value>(distinctType))
                    .ValueType(new ObjectMapDataType<Value>(distinctValueType));
            index = store.OpenMap("idx", indexBuilder);
        }
    }

    /// <inheritdoc/>
    public override int AddRow(Value?[] values)
    {
        ValueRow key = GetKey(values);
        if (distinct || distinctIndexes is not null)
        {
            if (distinctIndexes is not null)
            {
                int count = distinctIndexes.Length;
                Value?[] newValues = new Value?[count];
                for (int i = 0; i < count; i++)
                {
                    newValues[i] = values[distinctIndexes[i]];
                }

                ValueRow distinctRow = ValueRow.Get(newValues);
                if (orderedDistinctOnType is null)
                {
                    if (index!.PutIfAbsent(distinctRow, ValueNull.Instance) is not null)
                    {
                        return rowCount;
                    }
                }
                else
                {
                    object? previousObject = index!.Get(distinctRow);
                    if (previousObject is null)
                    {
                        index.Put(distinctRow, key);
                    }
                    else
                    {
                        ValueRow previous = (ValueRow)previousObject;
                        if (orderedDistinctOnType.Compare(previous, key) > 0)
                        {
                            _ = map.Remove(previous);
                            rowCount--;
                            index.Put(distinctRow, key);
                        }
                        else
                        {
                            return rowCount;
                        }
                    }
                }
            }
            else if (visibleColumnCount != resultColumnCount)
            {
                Value?[] visible = new Value?[visibleColumnCount];
                Array.Copy(values, visible, visibleColumnCount);
                ValueRow distinctRow = ValueRow.Get(visible);
                if (index!.PutIfAbsent(distinctRow, ValueNull.Instance) is not null)
                {
                    return rowCount;
                }
            }

            if (map.PutIfAbsent(key, 1L) is null)
            {
                rowCount++;
            }
        }
        else
        {
            object? old = map.PutIfAbsent(key, 1L);
            if (old is not null)
            {
                map.Put(key, (long)old + 1L);
            }

            rowCount++;
        }

        return rowCount;
    }

    /// <inheritdoc/>
    public override bool Contains(Value?[] values)
    {
        if (parent is not null)
        {
            return parent.Contains(values);
        }

        if (visibleColumnCount != resultColumnCount)
        {
            return index!.ContainsKey(ValueRow.Get(values));
        }

        return map.ContainsKey(GetKey(values));
    }

    /// <inheritdoc/>
    public override ResultExternal? CreateShallowCopy()
    {
        lock (this)
        {
            if (parent is not null)
            {
                return parent.CreateShallowCopy();
            }

            if (closed)
            {
                return null;
            }

            childCount++;
            return new MVSortedTempResult(this);
        }
    }

    private ValueRow GetKey(Value?[] values)
    {
        if (indexes is not null)
        {
            Value?[] reordered = new Value?[indexes.Length];
            for (int i = 0; i < indexes.Length; i++)
            {
                reordered[i] = values[indexes[i]];
            }

            values = reordered;
        }

        return ValueRow.Get(values);
    }

    private Value?[] GetValue(Value?[] key)
    {
        if (indexes is not null)
        {
            Value?[] values = new Value?[indexes.Length];
            for (int i = 0; i < indexes.Length; i++)
            {
                values[indexes[i]] = key[i];
            }

            key = values;
        }

        return key;
    }

    /// <inheritdoc/>
    public override Value?[] Next()
    {
        if (cursor is null)
        {
            cursor = map.Cursor(null);
            current = null;
            valueCount = 0L;
        }

        if (--valueCount > 0)
        {
            return current!;
        }

        if (!cursor.HasNext())
        {
            current = null;
            return null!;
        }

        _ = cursor.Next();
        object value = cursor.GetValue();
        current = GetValue(((ValueRow)cursor.GetKey()).GetList());
        valueCount = (long)value;
        return current;
    }

    /// <inheritdoc/>
    public override int RemoveRow(Value?[] values)
    {
        if (visibleColumnCount != resultColumnCount)
        {
            throw DbException.GetUnsupportedException("removeRow()");
        }

        if (map.Remove(GetKey(values)) is not null)
        {
            rowCount--;
        }

        return rowCount;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        cursor = null;
        current = null;
        valueCount = 0L;
    }
}
