/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.result.SortOrder.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * How a query's rows are to be put in order.
 *
 * The order is held as a number per column, not as a type: the caller of a query and the
 * reference's callers both pass these numbers around, and a query that arrives with an order
 * already spelled out should keep it and re-print it unchanged. Direction and where NULL goes
 * are therefore bits of the same number, which is why ASCENDING is 0 — the common case is the
 * default one and the reference numbers it that way.
 *
 * Three things here are easy to get wrong, so they are written down.
 *
 * **The indexes are into the query's columns, not into the select list.** For
 * "SELECT name, id FROM test ORDER BY id, name" the indexes are {1, 0}. Sorting is done on the
 * row the query produced, which may carry hidden columns for ORDER BY and DISTINCT ON.
 *
 * **The SQL is printed by position, not by expression.** Inside the select list the item is a
 * number — the position of that column — because a caller has to be able to read back a query
 * it did not write; outside it, the expression is printed. Same order, two spellings.
 *
 * **Partial sorting is not partial sorting.** Sort(rows, from, to) only sorts the window, but
 * the rows outside it are expected to be already in order — that is what makes ORDER BY ... FETCH
 * cheap. It is a promise about the caller's data, not a weaker sort, and it is why the single-row
 * case is special-cased to a minimum instead of running a sort at all.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;

using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// How the rows of a result are ordered.
/// </summary>
public sealed class SortOrder
{
    /// <summary>
    /// This bit mask means the values should be sorted in ascending order.
    /// </summary>
    public const int Ascending = 0;

    /// <summary>
    /// This bit mask means the values should be sorted in descending order.
    /// </summary>
    public const int Descending = 1;

    /// <summary>
    /// This bit mask means NULLs should be sorted before other data, no matter if ascending or
    /// descending order is used.
    /// </summary>
    public const int NullsFirst = 2;

    /// <summary>
    /// This bit mask means NULLs should be sorted after other data, no matter if ascending or
    /// descending order is used.
    /// </summary>
    public const int NullsLast = 4;

    private readonly SessionLocal session;

    /// <summary>
    /// The column indexes of the order by expressions within the query.
    /// </summary>
    private readonly int[] queryColumnIndexes;

    /// <summary>
    /// The sort type bit mask (Descending, NullsFirst, NullsLast).
    /// </summary>
    private readonly int[] sortTypes;

    /// <summary>
    /// The order list.
    /// </summary>
    private readonly List<H2Sharp.Command.Query.QueryOrderBy>? orderList;

    /// <summary>
    /// The row comparison as a delegate, built once: every sort would otherwise allocate one
    /// from the method group per call.
    /// </summary>
    private Comparison<Value?[]>? comparison;

    /// <summary>
    /// Construct a new sort order object with specified sort types.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="sortTypes">sort types of all columns</param>
    /// <returns>a sort order</returns>
    public static SortOrder OfSortTypes(SessionLocal session, int[] sortTypes)
    {
        ArgumentNullException.ThrowIfNull(sortTypes);
        int length = sortTypes.Length;
        int[] queryColumnIndexes = new int[length];
        for (int i = 0; i < length; i++)
        {
            queryColumnIndexes[i] = i;
        }

        return new SortOrder(session, queryColumnIndexes, sortTypes, null);
    }

    /// <summary>
    /// Construct a new sort order object with default sort directions.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="queryColumnIndexes">the column index list</param>
    public SortOrder(SessionLocal session, int[] queryColumnIndexes)
        : this(session, queryColumnIndexes, new int[queryColumnIndexes.Length], null)
    {
    }

    /// <summary>
    /// Construct a new sort order object.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="queryColumnIndexes">the column index list</param>
    /// <param name="sortType">the sort order bit masks</param>
    /// <param name="orderList">the original query order list (if this is a query)</param>
    public SortOrder(
        SessionLocal session,
        int[] queryColumnIndexes,
        int[] sortType,
        List<H2Sharp.Command.Query.QueryOrderBy>? orderList)
    {
        this.session = session;
        this.queryColumnIndexes = queryColumnIndexes;
        sortTypes = sortType;
        this.orderList = orderList;
    }

    /// <summary>
    /// Create the SQL snippet that describes this sort order. This is the SQL snippet that
    /// usually appears after the ORDER BY clause.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="list">the expression list</param>
    /// <param name="visible">the number of columns in the select list</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the specified string builder</returns>
    public StringBuilder GetSql(StringBuilder builder, Expression[] list, int visible, int sqlFlags)
    {
        int i = 0;
        foreach (int idx in queryColumnIndexes)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            if (idx < visible)
            {
                _ = builder.Append(idx + 1);
            }
            else
            {
                _ = list[idx].GetUnenclosedSql(builder, sqlFlags);
            }

            TypeToString(builder, sortTypes[i++]);
        }

        return builder;
    }

    /// <summary>
    /// Appends type information (DESC, NULLS FIRST, NULLS LAST) to the specified statement
    /// builder.
    /// </summary>
    /// <param name="builder">string builder</param>
    /// <param name="type">sort type</param>
    public static void TypeToString(StringBuilder builder, int type)
    {
        if ((type & Descending) != 0)
        {
            _ = builder.Append(" DESC");
        }

        if ((type & NullsFirst) != 0)
        {
            _ = builder.Append(" NULLS FIRST");
        }
        else if ((type & NullsLast) != 0)
        {
            _ = builder.Append(" NULLS LAST");
        }
    }

    /// <summary>
    /// Compares two expression lists.
    /// </summary>
    /// <param name="a">the first expression list</param>
    /// <param name="b">the second expression list</param>
    /// <returns>the result of the comparison</returns>
    public int Compare(Value?[] a, Value?[] b)
    {
        return CompareImpl(a, b, queryColumnIndexes.Length);
    }

    /// <summary>
    /// Compares two expression lists.
    /// </summary>
    /// <param name="a">the first expression list</param>
    /// <param name="b">the second expression list</param>
    /// <param name="count">number of columns to compare</param>
    /// <returns>the result of the comparison</returns>
    public int Compare(Value?[] a, Value?[] b, int count)
    {
        return CompareImpl(a, b, count);
    }

    /// <summary>
    /// The comparison itself, shared by the two entry points.
    /// </summary>
    /// <param name="a">the first expression list</param>
    /// <param name="b">the second expression list</param>
    /// <param name="count">number of columns to compare</param>
    /// <returns>the result of the comparison</returns>
    private int CompareImpl(Value?[] a, Value?[] b, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int idx = queryColumnIndexes[i];
            int type = sortTypes[i];
            Value? ao = a[idx];
            Value? bo = b[idx];
            bool aNull = ao is null || ReferenceEquals(ao, ValueNull.Instance);
            bool bNull = bo is null || ReferenceEquals(bo, ValueNull.Instance);
            if (aNull || bNull)
            {
                if (aNull == bNull)
                {
                    continue;
                }

                return DefaultNullOrderingRules.CompareNull(session.GetDatabase().GetDefaultNullOrdering(), aNull, type);
            }

            int comp = session.Compare(ao!, bo!);
            if (comp != 0)
            {
                return (type & Descending) == 0 ? comp : -comp;
            }
        }

        return 0;
    }

    /// <summary>
    /// Sort a list of rows.
    /// </summary>
    /// <param name="rows">the list of rows</param>
    public void Sort(List<Value?[]> rows)
    {
        // Java's List.sort is stable and the reference relies on it: tied rows keep the order
        // they were read in, which is what a limit then trims. List.Sort is introspective and
        // makes no such promise, so the order goes through the stable LINQ sort instead.
        Value?[][] ordered = rows
            .OrderBy(static row => row, Comparer<Value?[]>.Create(comparison ??= Compare))
            .ToArray();
        for (int i = 0; i < ordered.Length; i++)
        {
            rows[i] = ordered[i];
        }
    }

    /// <summary>
    /// Sort a list of rows using offset and limit.
    /// </summary>
    /// <remarks>
    /// Only the window is sorted: the rows outside it are expected to be in order already, which
    /// is what the caller promised by handing over a range. One row in, the answer is the
    /// smallest one and no sort is run at all.
    /// </remarks>
    /// <param name="rows">the list of rows</param>
    /// <param name="fromInclusive">the start index, inclusive</param>
    /// <param name="toExclusive">the end index, exclusive</param>
    public void Sort(List<Value?[]> rows, int fromInclusive, int toExclusive)
    {
        if (toExclusive == 1 && fromInclusive == 0)
        {
            Value?[] min = rows[0];
            for (int i = 1; i < rows.Count; i++)
            {
                if (Compare(rows[i], min) < 0)
                {
                    min = rows[i];
                }
            }

            rows[0] = min;
            return;
        }

        Value?[][] array = new Value?[rows.Count][];
        rows.CopyTo(array);
        Utils.SortTopN(array, fromInclusive, toExclusive, comparison ??= Compare);
        for (int i = fromInclusive; i < toExclusive; i++)
        {
            rows[i] = array[i];
        }
    }

    /// <summary>
    /// Gets the column index list. This is the column indexes of the order by expressions within
    /// the query.
    /// <para>
    /// For the query "select name, id from test order by id, name" this is {1, 0} as the first
    /// order by expression (the column "id") is the second column of the query, and the second
    /// order by expression ("name") is the first column of the query.
    /// </para>
    /// </summary>
    /// <returns>the list</returns>
    public int[] GetQueryColumnIndexes()
    {
        return queryColumnIndexes;
    }

    /// <summary>
    /// Gets the column for the given table filter, if the sort column is for this filter.
    /// </summary>
    /// <param name="index">the column index (0, 1,...)</param>
    /// <param name="filter">the table filter</param>
    /// <returns>the column, or null</returns>
    public Column? GetColumn(int index, TableFilter filter)
    {
        if (orderList is null || index >= orderList.Count)
        {
            return null;
        }

        Expression? expr = orderList[index].expression;
        expr = expr?.GetNonAliasExpression();
        if (expr is null || expr.IsConstant())
        {
            return null;
        }

        if (expr is not Expressions.ExpressionColumn exprCol)
        {
            return null;
        }

        // The reference asks which filter the expression reads from, and answers null when it is
        // another table's. This port maps a column by resolver rather than by filter, so the
        // question has no honest answer here and it refuses rather than guessing from the alias.
        _ = exprCol.GetTableFilter();
        return exprCol.GetColumn();
    }

    /// <summary>
    /// Gets the sort order bit masks.
    /// </summary>
    /// <returns>the list</returns>
    public int[] GetSortTypes()
    {
        return sortTypes;
    }

    /// <summary>
    /// Returns the original query order list.
    /// </summary>
    /// <returns>the original query order list</returns>
    public List<H2Sharp.Command.Query.QueryOrderBy>? GetOrderList()
    {
        return orderList;
    }

    /// <summary>
    /// Returns sort order bit masks with <c>NULLS_FIRST</c> or <c>NULLS_LAST</c> explicitly set.
    /// </summary>
    /// <returns>bit masks with either <c>NULLS_FIRST</c> or <c>NULLS_LAST</c> explicitly set</returns>
    public int[] GetSortTypesWithNullOrdering()
    {
        return AddNullOrdering(session.GetDatabase(), (int[])sortTypes.Clone());
    }

    /// <summary>
    /// Adds explicit <c>NULLS_FIRST</c> or <c>NULLS_LAST</c> where they aren't already specified.
    /// </summary>
    /// <param name="database">the database</param>
    /// <param name="sortTypes">bit masks</param>
    /// <returns>the specified array with possibly modified bit masks</returns>
    public static int[] AddNullOrdering(Database database, int[] sortTypes)
    {
        DefaultNullOrdering defaultNullOrdering = database.GetDefaultNullOrdering();
        for (int i = 0, length = sortTypes.Length; i < length; i++)
        {
            sortTypes[i] = DefaultNullOrderingRules.AddExplicitNullOrdering(defaultNullOrdering, sortTypes[i]);
        }

        return sortTypes;
    }

    /// <summary>
    /// Turns a sort type with a null ordering into its opposite.
    /// </summary>
    /// <param name="sortTypeWithNull">the sort type</param>
    /// <returns>the opposite sort type</returns>
    public static int Inverse(int sortTypeWithNull)
    {
        return sortTypeWithNull ^ (Descending | NullsFirst | NullsLast);
    }

    /// <summary>
    /// Returns comparator for row values.
    /// </summary>
    /// <returns>comparator for row values</returns>
    public Comparison<Value> GetRowValueComparator()
    {
        return (o1, o2) => Compare(
            ((ValueRow)o1).GetList(),
            ((ValueRow)o2).GetList());
    }
}
