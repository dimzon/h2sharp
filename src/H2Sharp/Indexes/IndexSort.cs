/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.index.IndexSort.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * CompareTo is subtraction and nothing else, and it has to stay that way: the reference's
 * priority queue over these objects is a binary heap that compares with it, and a comparer that
 * returns -1/0/1 would still order correctly there. So the overflow is kept deliberately rather
 * than tidied away, and the only note is this one: the result is a difference and can wrap.
 */
using System;

namespace H2Sharp.Indexes;

/// <summary>
/// Index-sorting information.
/// </summary>
public sealed class IndexSort : IComparable<IndexSort>
{
    /// <summary>
    /// Used instead of actual number of sorted columns when post-sorting isn't
    /// needed.
    /// </summary>
    public const int FullySorted = int.MaxValue;

    private readonly Index? index;

    /// <summary>
    /// A positive number of sorted columns for partial index sorts, or
    /// <see cref="FullySorted"/> for complete index sorts.
    /// </summary>
    private readonly int sortedColumns;

    /// <summary>
    /// Whether index must be iterated in reverse order.
    /// </summary>
    private readonly bool reverse;

    /// <summary>
    /// Creates an index sorting information for complete index sort.
    /// </summary>
    /// <param name="index">the index</param>
    /// <param name="reverse">whether index must be iterated in reverse order</param>
    public IndexSort(Index? index, bool reverse)
        : this(index, FullySorted, reverse)
    {
    }

    /// <summary>
    /// Creates an index sorting information for index sort.
    /// </summary>
    /// <param name="index">the index</param>
    /// <param name="sortedColumns">
    /// a positive number of sorted columns for partial index sorts,
    /// or <see cref="FullySorted"/> for complete index sorts
    /// </param>
    /// <param name="reverse">whether index must be iterated in reverse order</param>
    public IndexSort(Index? index, int sortedColumns, bool reverse)
    {
        this.index = index;
        this.sortedColumns = sortedColumns;
        this.reverse = reverse;
    }

    /// <summary>
    /// Returns the index.
    /// </summary>
    /// <returns>the index</returns>
    public Index? GetIndex()
    {
        return index;
    }

    /// <summary>
    /// Returns number of sorted columns.
    /// </summary>
    /// <returns>positive number of sorted columns for partial index sorts, or <see cref="FullySorted"/> for complete index sorts</returns>
    public int GetSortedColumns()
    {
        return sortedColumns;
    }

    /// <summary>
    /// Returns whether index must be iterated in reverse order.
    /// </summary>
    /// <returns>true for reverse order, false for natural order</returns>
    public bool IsReverse()
    {
        return reverse;
    }

    /// <inheritdoc/>
    public int CompareTo(IndexSort? other)
    {
        if (other is null)
        {
            return 1;
        }

        return other.sortedColumns - sortedColumns;
    }
}
