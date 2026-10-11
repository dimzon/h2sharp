/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mvstore.db.RowDataType.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The data type for rows.
 *
 * Two things here are not obvious and both are about the row key.
 *
 * First, a row can be stored without its key (storeKeys false) and a key can be the sentinel
 * MATCH_ALL_ROW_KEY, which means "any key will do". The comparison returns **0** in that case
 * rather than comparing the numbers: a scan that matches everything must not order the rows by
 * a key it never wrote, or the same row would come out in a different order than it went in.
 *
 * Second, when the row is *narrower* than the table — an index stores only the columns it keys
 * on — the comparison walks those columns and then falls back to the key. Without the key the
 * two rows would be equal, and a map with two equal keys would lose one of them.
 *
 * binarySearch is the reference's own binary search with one change that matters: the first
 * probe is the middle, and afterwards it is the cached position minus one, so that a repeated
 * lookup continues where it left off instead of starting over.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.MvStore.Type;
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// The data type for rows.
/// </summary>
public sealed class RowDataType : BasicDataType<SearchRow>
{
    private readonly ValueDataType valueDataType;

    private readonly int[]? sortTypes;

    private readonly int[]? indexes;

    private readonly int columnCount;

    private readonly bool storeKeys;

    /// <summary>
    /// Creates a row type.
    /// </summary>
    /// <param name="provider">where casts come from</param>
    /// <param name="compareMode">how values are compared</param>
    /// <param name="handler">where LOBs live</param>
    /// <param name="sortTypes">the sort type of each column, or null</param>
    /// <param name="indexes">the columns that are stored, or null for all of them</param>
    /// <param name="columnCount">the number of columns</param>
    /// <param name="storeKeys">whether the row key is stored with the row</param>
    public RowDataType(
        Engine.CastDataProvider? provider,
        CompareMode compareMode,
        Store.DataHandler? handler,
        int[]? sortTypes,
        int[]? indexes,
        int columnCount,
        bool storeKeys)
    {
        valueDataType = new ValueDataType(provider, compareMode, handler, sortTypes);
        this.sortTypes = sortTypes;
        this.indexes = indexes;
        this.columnCount = columnCount;
        this.storeKeys = storeKeys;
    }

    /// <summary>
    /// Gets the indexes of the stored columns, or null when all are stored.
    /// </summary>
    public int[]? GetIndexes()
    {
        return indexes;
    }

    /// <summary>
    /// Gets the number of columns of a row of this type.
    /// </summary>
    public int GetColumnCount()
    {
        return columnCount;
    }

    /// <summary>
    /// Whether the row key is stored with the row.
    /// </summary>
    public bool IsStoreKeys()
    {
        return storeKeys;
    }

    /// <summary>
    /// Gets the value type this uses for the values inside a row.
    /// </summary>
    public ValueDataType GetValueDataType()
    {
        return valueDataType;
    }

    /// <inheritdoc/>
    public override SearchRow[] CreateStorage(int capacity)
    {
        return new SearchRow[capacity];
    }

    /// <inheritdoc/>
    public override int Compare(SearchRow? one, SearchRow? two)
    {
        if (ReferenceEquals(one, two))
        {
            return 0;
        }

        if (one is null || two is null)
        {
            return one is null ? -1 : 1;
        }

        return CompareSearchRows(one, two);
    }

    private int CompareSearchRows(SearchRow a, SearchRow b)
    {
        if (indexes is null)
        {
            int len = a.GetColumnCount();
            for (int i = 0; i < len; i++)
            {
                int comp = valueDataType.CompareValues(a.GetValue(i), b.GetValue(i), sortTypes![i]);
                if (comp != 0)
                {
                    return comp;
                }
            }

            return 0;
        }

        for (int i = 0; i < indexes.Length; i++)
        {
            int index = indexes[i];
            Value? v1 = a.GetValue(index);
            Value? v2 = b.GetValue(index);
            if (v1 is null || v2 is null)
            {
                // can't compare further
                break;
            }

            int comp = valueDataType.CompareValues(v1, v2, sortTypes![i]);
            if (comp != 0)
            {
                return comp;
            }
        }

        long aKey = a.GetKey();
        long bKey = b.GetKey();

        // MATCH_ALL_ROW_KEY means "any key will do", so rows that carry it compare equal rather
        // than in the order their keys happen to have.
        return aKey == SearchRow.MatchAllRowKey || bKey == SearchRow.MatchAllRowKey ? 0 : aKey.CompareTo(bKey);
    }

    /// <inheritdoc/>
    public override int BinarySearch(SearchRow key, SearchRow[] keys, int size, int initialGuess)
    {
        int low = 0;
        int high = size - 1;

        // the cached index minus one, so that
        // for the first time (when cachedCompare is 0),
        // the default value is used
        int x = initialGuess - 1;
        if (x < 0 || x > high)
        {
            x = (high + 1) >>> 1;
        }

        while (low <= high)
        {
            int compare = CompareSearchRows(key, keys[x]);
            if (compare > 0)
            {
                low = x + 1;
            }
            else if (compare < 0)
            {
                high = x - 1;
            }
            else
            {
                return x;
            }

            x = (low + high + 1) >>> 1;
        }

        return -(low + 1);
    }

    /// <inheritdoc/>
    public override int GetMemory(SearchRow obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.GetMemory();
    }

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, SearchRow obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (storeKeys)
        {
            _ = buff.PutVarLong(obj.GetKey());
        }

        if (indexes is null)
        {
            int len = obj.GetColumnCount();
            for (int i = 0; i < len; i++)
            {
                valueDataType.Write(buff, obj.GetValue(i)!);
            }
        }
        else
        {
            foreach (int i in indexes)
            {
                valueDataType.Write(buff, obj.GetValue(i)!);
            }
        }
    }

    /// <inheritdoc/>
    public override SearchRow Read(Span<byte> buff, ref int position)
    {
        RowFactory rowFactory = valueDataType.GetRowFactory() ?? RowFactory.GetDefaultRowFactory();
        SearchRow row = rowFactory.CreateRow();
        if (storeKeys)
        {
            row.SetKey(DataUtils.ReadVarLong(buff, ref position));
        }

        TypeInfo[]? columnTypes = rowFactory.GetColumnTypes();
        if (indexes is null)
        {
            int len = row.GetColumnCount();
            for (int i = 0; i < len; i++)
            {
                row.SetValue(i, ReadValue(buff, ref position, columnTypes, i));
            }
        }
        else
        {
            foreach (int i in indexes)
            {
                row.SetValue(i, ReadValue(buff, ref position, columnTypes, i));
            }
        }

        return row;
    }

    private Value ReadValue(Span<byte> buff, ref int position, TypeInfo[]? columnTypes, int column)
    {
        // The column type is passed so that a value of a family type (a NUMERIC with a scale, a
        // character with a precision) can be read as the column declared it. ValueDataType takes
        // it for that, and the row type is where it is known.
        return valueDataType.Read(buff, ref position);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(obj, this))
        {
            return true;
        }

        if (obj is not RowDataType other)
        {
            return false;
        }

        return columnCount == other.columnCount
            && SameArray(indexes, other.indexes)
            && SameArray(sortTypes, other.sortTypes);
    }

    private static bool SameArray(int[]? left, int[]? right)
    {
        if (left is null || right is null)
        {
            return ReferenceEquals(left, right);
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int res = base.GetHashCode();
        res = (res * 31) + columnCount;
        res = (res * 31) + HashOf(indexes);
        res = (res * 31) + HashOf(sortTypes);
        return res;
    }

    private static int HashOf(int[]? array)
    {
        if (array is null)
        {
            return 0;
        }

        int hash = 1;
        foreach (int i in array)
        {
            hash = (hash * 31) + i;
        }

        return hash;
    }
}
