/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.command.query.SelectGroups.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Grouped data for aggregates.
 *
 * Call sequence:
 * - reset().
 * - For each source row nextSource() is invoked.
 * - done().
 * - next() is invoked inside a loop until it returns null.
 *
 * Two deviations, both recorded. The group map is a SortedDictionary over the shared
 * session row comparer (the one DISTINCT already sorts by) instead of a TreeMap over
 * the session itself: C# cannot hand a session as a comparer, and two comparers that
 * disagreed on row equality would split groups DISTINCT merges. Window maps and lazy
 * group walking wait for OVER; remove() refuses with them, since only the window path
 * removes groups mid-walk.
 */
using System.Collections.Generic;
using System.Diagnostics;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.Command.Query;

/// <summary>
/// Grouped data for aggregates.
/// </summary>
public abstract class SelectGroups
{
    private sealed class Grouped : SelectGroups
    {
        private readonly int[]? groupIndex;

        /// <summary>
        /// Map of group-by key to group-by expression data e.g. AggregateData
        /// </summary>
        private SortedDictionary<ValueRow, object?[]>? groupByData;

        /// <summary>
        /// Key into groupByData that produces currentGroupByExprData. Not used
        /// in lazy mode.
        /// </summary>
        private ValueRow? currentGroupsKey;

        /// <summary>
        /// Cursor for <see cref="Next"/> method.
        /// </summary>
        private IEnumerator<KeyValuePair<ValueRow, object?[]>>? cursor;

        internal Grouped(SessionLocal session, List<Expression> expressions, int[]? groupIndex)
            : base(session, expressions)
        {
            this.groupIndex = groupIndex;
        }

        /// <inheritdoc/>
        public override void Reset()
        {
            base.Reset();
            groupByData = new SortedDictionary<ValueRow, object?[]>(new LocalResult.SessionRowComparer(session));
            currentGroupsKey = null;
            cursor = null;
        }

        /// <inheritdoc/>
        public override void NextSource()
        {
            ValueRow key;
            if (groupIndex is null)
            {
                key = ValueRow.Empty;
            }
            else
            {
                Value?[] keyValues = new Value?[groupIndex.Length];
                // update group
                for (int i = 0; i < groupIndex.Length; i++)
                {
                    int idx = groupIndex[i];
                    Expression expr = expressions[idx];
                    keyValues[i] = expr.GetValue(session);
                }

                key = ValueRow.Get(keyValues!);
            }

            currentGroupsKey = key;
            if (!groupByData!.TryGetValue(key, out object?[]? values))
            {
                values = CreateRow();
                groupByData[key] = values;
            }

            currentGroupByExprData = values;
            currentGroupRowId++;
        }

        /// <inheritdoc/>
        internal override void UpdateCurrentGroupExprData()
        {
            // this can be null in lazy mode
            if (currentGroupsKey is not null)
            {
                // since we changed the size of the array, update the object in
                // the groups map
                groupByData![currentGroupsKey] = currentGroupByExprData!;
            }
        }

        /// <inheritdoc/>
        public override void Done()
        {
            base.Done();
            if (groupIndex is null && groupByData!.Count == 0)
            {
                groupByData[ValueRow.Empty] = CreateRow();
            }

            cursor = groupByData!.GetEnumerator();
        }

        /// <inheritdoc/>
        public override ValueRow? Next()
        {
            if (cursor!.MoveNext())
            {
                KeyValuePair<ValueRow, object?[]> entry = cursor.Current;
                currentGroupByExprData = entry.Value;
                currentGroupRowId++;
                return entry.Key;
            }

            return null;
        }

        /// <inheritdoc/>
        public override void ResetLazy()
        {
            base.ResetLazy();
            currentGroupsKey = null;
        }
    }

    private sealed class Plain : SelectGroups
    {
        private List<object?[]>? rows;

        /// <summary>
        /// Cursor for <see cref="Next"/> method.
        /// </summary>
        private int cursor;

        internal Plain(SessionLocal session, List<Expression> expressions)
            : base(session, expressions)
        {
        }

        /// <inheritdoc/>
        public override void Reset()
        {
            base.Reset();
            rows = [];
            cursor = 0;
        }

        /// <inheritdoc/>
        public override void NextSource()
        {
            object?[] values = CreateRow();
            rows!.Add(values);
            currentGroupByExprData = values;
            currentGroupRowId++;
        }

        /// <inheritdoc/>
        internal override void UpdateCurrentGroupExprData()
        {
            rows![rows.Count - 1] = currentGroupByExprData!;
        }

        /// <inheritdoc/>
        public override void Done()
        {
            base.Done();
            cursor = 0;
        }

        /// <inheritdoc/>
        public override ValueRow? Next()
        {
            if (cursor < rows!.Count)
            {
                currentGroupByExprData = rows[cursor++];
                currentGroupRowId++;
                return ValueRow.Empty;
            }

            return null;
        }
    }

    /// <summary>
    /// The database session.
    /// </summary>
    protected readonly SessionLocal session;

    /// <summary>
    /// The query's column list, including invisible expressions such as order by expressions.
    /// </summary>
    protected readonly List<Expression> expressions;

    /// <summary>
    /// The array of current group-by expression data e.g. AggregateData.
    /// </summary>
    protected object?[]? currentGroupByExprData;

    /// <summary>
    /// Maps an expression object to an index, to use in accessing the array
    /// pointed to by groupByData.
    /// </summary>
    private readonly Dictionary<Expression, int> exprToIndexInGroupByData = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The id of the current group.
    /// </summary>
    protected int currentGroupRowId;

    /// <summary>
    /// Creates new instance of grouped data.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="expressions">the expressions</param>
    /// <param name="isGroupQuery">is this query is a group query</param>
    /// <param name="groupIndex">the indexes of group expressions, or null</param>
    /// <returns>new instance of the grouped data.</returns>
    public static SelectGroups GetInstance(
        SessionLocal session, List<Expression> expressions, bool isGroupQuery, int[]? groupIndex)
    {
        return isGroupQuery ? new Grouped(session, expressions, groupIndex) : new Plain(session, expressions);
    }

    /// <summary>
    /// Base constructor: the session and the select list the groups are read off.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="expressions">the expressions</param>
    protected SelectGroups(SessionLocal session, List<Expression> expressions)
    {
        this.session = session;
        this.expressions = expressions;
    }

    /// <summary>
    /// Is there currently a group-by active.
    /// </summary>
    /// <returns>true if there is currently a group-by active,
    /// otherwise returns false.</returns>
    public bool IsCurrentGroup() => currentGroupByExprData is not null;

    /// <summary>
    /// Get the group-by data for the current group and the passed in expression.
    /// </summary>
    /// <param name="expr">expression</param>
    /// <returns>expression data or null</returns>
    public object? GetCurrentGroupExprData(Expression expr)
    {
        if (!exprToIndexInGroupByData.TryGetValue(expr, out int index))
        {
            return null;
        }

        return currentGroupByExprData![index];
    }

    /// <summary>
    /// Set the group-by data for the current group and the passed in expression.
    /// </summary>
    /// <param name="expr">expression</param>
    /// <param name="obj">expression data to set</param>
    public void SetCurrentGroupExprData(Expression expr, object obj)
    {
        if (!exprToIndexInGroupByData.TryGetValue(expr, out int index))
        {
            index = exprToIndexInGroupByData.Count;
            exprToIndexInGroupByData[expr] = index;
            if (index >= currentGroupByExprData!.Length)
            {
                System.Array.Resize(ref currentGroupByExprData, currentGroupByExprData.Length * 2);
                UpdateCurrentGroupExprData();
            }
        }
        else
        {
            Debug.Assert(currentGroupByExprData![index] is null);
        }

        currentGroupByExprData![index] = obj;
    }

    /// <summary>
    /// Creates new object arrays to holds group-by data.
    /// </summary>
    /// <returns>new object array to holds group-by data.</returns>
    protected object?[] CreateRow()
    {
        return new object?[System.Math.Max(exprToIndexInGroupByData.Count, expressions.Count)];
    }

    /// <summary>
    /// Update group-by data specified by implementation.
    /// </summary>
    internal abstract void UpdateCurrentGroupExprData();

    /// <summary>
    /// Returns identity of the current row. Used by aggregates to check whether
    /// they already processed this row or not.
    /// </summary>
    /// <returns>identity of the current row</returns>
    public int GetCurrentGroupRowId() => currentGroupRowId;

    /// <summary>
    /// Resets this group data for reuse.
    /// </summary>
    public virtual void Reset()
    {
        currentGroupByExprData = null;
        exprToIndexInGroupByData.Clear();
        currentGroupRowId = 0;
    }

    /// <summary>
    /// Invoked for each source row to evaluate group key and setup all necessary
    /// data for aggregates.
    /// </summary>
    public abstract void NextSource();

    /// <summary>
    /// Invoked after all source rows are evaluated.
    /// </summary>
    public virtual void Done()
    {
        currentGroupRowId = 0;
    }

    /// <summary>
    /// Returns the key of the next group.
    /// </summary>
    /// <returns>the key of the next group, or null</returns>
    public abstract ValueRow? Next();

    /// <summary>
    /// Removes the data for the current key.
    /// </summary>
    /// <remarks>
    /// Only the window path removes groups mid-walk, so this refuses with the missing
    /// machinery named. A C# enumerator cannot remove through the walk the way Java's
    /// iterator does, which is one more reason this waits for windows.
    /// </remarks>
    public virtual void Remove()
    {
        throw new TODOException("SelectGroups.remove: windowed execution is not ported");
    }

    /// <summary>
    /// Resets this group data for reuse in lazy mode.
    /// </summary>
    public virtual void ResetLazy()
    {
        currentGroupByExprData = null;
        currentGroupRowId = 0;
    }

    /// <summary>
    /// Moves group data to the next group in lazy mode.
    /// </summary>
    public void NextLazyGroup()
    {
        currentGroupByExprData = new object?[System.Math.Max(exprToIndexInGroupByData.Count, expressions.Count)];
    }

    /// <summary>
    /// Moves group data to the next row in lazy mode.
    /// </summary>
    public void NextLazyRow()
    {
        currentGroupRowId++;
    }
}
