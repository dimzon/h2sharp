/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.result.Sparse.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A sparse row stores only indexed columns, with a map from column id to stored position.
 *
 * Ported from org.h2.result.Sparse (H2 2.5.x).
 */
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// A sparse row stores only indexed columns, with a map from column id to stored position.
/// </summary>
/// <remarks>
/// Ported from org.h2.result.Sparse (H2 2.5.x).
/// The map is the inverse of the index array: map[columnId] = storedPosition + 1.
/// A value of 0 in the map means the column is not stored (not part of the index).
/// </remarks>
public sealed class Sparse : DefaultRow
{
    private readonly int columnCount;

    private readonly int[] map;

    /// <summary>
    /// Creates a sparse row.
    /// </summary>
    /// <remarks>
    /// Ported from org.h2.result.Sparse.java:23-27.
    /// The capacity is the number of indexed columns (length of the index array).
    /// The map is shared with the factory and maps column id → stored position + 1.
    /// </remarks>
    /// <param name="columnCount">the number of columns in the table</param>
    /// <param name="capacity">the number of indexed columns</param>
    /// <param name="map">maps column id to stored position + 1 (0 if not stored)</param>
    internal Sparse(int columnCount, int capacity, int[] map)
        : base(new Value?[capacity])
    {
        this.columnCount = columnCount;
        this.map = map;
    }

    /// <summary>
    /// Gets the number of columns in the table.
    /// </summary>
    /// <remarks>
    /// Reference: org.h2.result.Sparse.java:30-32
    /// </remarks>
    public override int GetColumnCount()
    {
        return columnCount;
    }

    /// <summary>
    /// Gets the value at the given column index.
    /// </summary>
    /// <remarks>
    /// For ROWID_INDEX returns the key. For other columns, looks up the map:
    /// map[i] > 0 means the column is stored at position map[i]-1 in the data array.
    /// map[i] == 0 means the column is not part of this index, returns null.
    /// Reference: org.h2.result.Sparse.java:35-41
    /// </remarks>
    /// <param name="i">the column index</param>
    /// <returns>the value, or null if not stored</returns>
    public override Value? GetValue(int i)
    {
        if (i == RowidIndex)
        {
            return ValueBigint.Get(GetKey());
        }

        int index = map[i];
        return index > 0 ? base.GetValue(index - 1) : null;
    }

    /// <summary>
    /// Sets the value at the given column index.
    /// </summary>
    /// <remarks>
    /// For ROWID_INDEX sets the key. For other columns, looks up the map:
    /// map[i] > 0 means store at position map[i]-1.
    /// map[i] == 0 means the column is not part of this index, skip.
    /// Reference: org.h2.result.Sparse.java:44-52
    /// </remarks>
    /// <param name="i">the column index</param>
    /// <param name="v">the value</param>
    public override void SetValue(int i, Value v)
    {
        if (i == RowidIndex)
        {
            SetKey(v.GetLong());
        }

        int index = map[i];
        if (index > 0)
        {
            base.SetValue(index - 1, v);
        }
    }

    /// <summary>
    /// Copies values from another row.
    /// </summary>
    /// <remarks>
    /// Copies the key and only the columns that are stored in this sparse row.
    /// Reference: org.h2.result.Sparse.java:55-63
    /// </remarks>
    /// <param name="source">the source row</param>
    public override void CopyFrom(SearchRow source)
    {
        SetKey(source.GetKey());
        for (int i = 0; i < map.Length; i++)
        {
            int index = map[i];
            if (index > 0)
            {
                base.SetValue(index - 1, source.GetValue(i)!);
            }
        }
    }
}
