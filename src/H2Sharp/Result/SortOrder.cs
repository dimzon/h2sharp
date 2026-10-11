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
 * already spelled out should keep it and re-print it unchanged. The direction and where NULL
 * goes are therefore bits of the same number — ASCENDING is 0 because the common case is the
 * default one and because the reference numbers it that way — and TypeToString puts them back
 * into words for the SQL text.
 *
 * None of the members are ported yet: comparing rows needs the comparison machinery of the
 * session, and printing needs the SQL builder of the expressions. They refuse rather than sort
 * by an invented rule, because a sort that is nearly right produces a result set that is
 * quietly wrong.
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// How the rows of a result are ordered.
/// </summary>
public class SortOrder
{
    /// <summary>Ascending order.</summary>
    public const int Ascending = 0;

    /// <summary>Descending order.</summary>
    public const int Descending = 1;

    /// <summary>NULLs first.</summary>
    public const int NullsFirst = 2;

    /// <summary>NULLs last.</summary>
    public const int NullsLast = 4;

    private readonly SessionLocal session;

    private readonly int[] queryColumnIndexes;

    private readonly int[] sortTypes;

    private readonly List<H2Sharp.Command.Query.QueryOrderBy>? orderList;

    private readonly Expression[] expressions;

    /// <summary>
    /// Creates a sort order for the given columns and sort types.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="sortTypes">sort types of all columns</param>
    /// <returns>a sort order</returns>
    public static SortOrder OfSortTypes(SessionLocal session, int[] sortTypes)
    {
        int length = sortTypes.Length;
        int[] queryColumnIndexes = new int[length];
        for (int i = 0; i < length; i++)
        {
            queryColumnIndexes[i] = i;
        }

        return new SortOrder(session, queryColumnIndexes, sortTypes, null);
    }

    /// <summary>
    /// Creates a sort order with default sort directions.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="queryColumnIndexes">the column index list</param>
    public SortOrder(SessionLocal session, int[] queryColumnIndexes)
        : this(session, queryColumnIndexes, new int[queryColumnIndexes.Length], null)
    {
    }

    /// <summary>
    /// Creates a sort order.
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
        expressions = [];
    }

    /// <summary>
    /// Appends the SQL of this sort order.
    /// </summary>
    /// <param name="builder">the builder</param>
    /// <param name="list">the expressions of the columns</param>
    /// <param name="visible">the number of visible columns</param>
    /// <param name="sqlFlags">the SQL flags</param>
    /// <returns>the builder</returns>
    public StringBuilder GetSql(StringBuilder builder, Expression[] list, int visible, int sqlFlags)
        => throw new TODOException("SortOrder.getSQL: the SQL builder of expressions is not ported");

    /// <summary>
    /// Appends the words of a sort type.
    /// </summary>
    /// <param name="builder">the builder</param>
    /// <param name="type">the sort type</param>
    public static void TypeToString(StringBuilder builder, int type)
        => throw new TODOException("SortOrder.typeToString: not ported");

    /// <summary>
    /// Compares two rows.
    /// </summary>
    /// <param name="a">the first row</param>
    /// <param name="b">the second row</param>
    /// <returns>a negative number, zero or a positive number</returns>
    public int Compare(Value?[] a, Value?[] b)
        => throw new TODOException("SortOrder.compare: the comparison machinery is not ported");

    /// <summary>
    /// Compares the first columns of two rows.
    /// </summary>
    /// <param name="a">the first row</param>
    /// <param name="b">the second row</param>
    /// <param name="count">how many columns to compare</param>
    /// <returns>a negative number, zero or a positive number</returns>
    public int Compare(Value?[] a, Value?[] b, int count)
        => throw new TODOException("SortOrder.compare: the comparison machinery is not ported");

    /// <summary>
    /// Sorts the rows.
    /// </summary>
    /// <param name="rows">the rows</param>
    public void Sort(List<Value?[]> rows)
        => throw new TODOException("SortOrder.sort: the comparison machinery is not ported");

    /// <summary>
    /// Sorts part of the rows.
    /// </summary>
    /// <param name="rows">the rows</param>
    /// <param name="fromInclusive">where to start</param>
    /// <param name="toExclusive">where to stop</param>
    public void Sort(List<Value?[]> rows, int fromInclusive, int toExclusive)
        => throw new TODOException("SortOrder.sort: the comparison machinery is not ported");

    /// <summary>
    /// Gets the index of each sorted column in the row.
    /// </summary>
    /// <returns>the indexes</returns>
    public int[] GetQueryColumnIndexes()
    {
        return queryColumnIndexes;
    }
}
