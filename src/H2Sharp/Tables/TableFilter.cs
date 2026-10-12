/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.TableFilter.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A table as a query sees it: under an alias, at a position in FROM, read by a plan.
 *
 * There is one of these per *use* of a table, not per table. "SELECT * FROM TEST T1, TEST T2"
 * has two filters over one table, each with its own alias, its own position and its own cursor,
 * because each is read independently and a column reference has to say which of them it means.
 * This is why a query holds filters and not tables: T1.NAME and T2.NAME are different columns,
 * and the filter is what tells them apart.
 *
 * The constructor asks the session to check the right to read the table unless the caller says
 * the right was already checked. That check is where a statement that names a table it may not
 * read fails, which is before the table would be opened — a check that happened at run time
 * would have half-read the table first.
 *
 * The hash is an object id of the session, not of the database, because it is not an identity
 * among the catalogue's objects: it tells one filter of a query from another, and it must not
 * spend an id a table would need.
 *
 * This file is the foundation of the class: its fields, its constructor, and the members that
 * only read them. Everything that plans or moves — bestPlanItem, the cursor loop, the joins —
 * arrives member by member, each read from the reference before it is written.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.Tables;

/// <summary>
/// A table filter represents a table that is used in a query. There is one such object for
/// every use of a table. For example, for the query "SELECT * FROM TEST T1, TEST T2", this will
/// contain two filter objects, one for table TEST T1 and one for table TEST T2.
/// </summary>
public class TableFilter : ColumnResolver
{
    private const int BeforeFirst = 0;

    private const int Found = 1;

    private const int AfterLast = 2;

    private const int NullRow = 3;

    /// <summary>
    /// Comparator that uses order in FROM clause as a sort key.
    /// </summary>
    public static readonly Comparison<TableFilter> OrderInFromComparator =
        (left, right) => left.orderInFrom.CompareTo(right.orderInFrom);

    /// <summary>
    /// Whether this filter is reached only through another one, which is what makes an outer
    /// join's outer side outer.
    /// </summary>
    protected bool joinOuterIndirect;

    private SessionLocal session;

    private readonly Table table;

    private readonly Command.Query.Select? select;

    private string? alias;

    private readonly IndexHints? indexHints;

    /// <summary>
    /// The index conditions used for direct index lookup (start or end).
    /// </summary>
    /// <remarks>
    /// Held from the start because the reference holds them from the start. They are filled by
    /// <c>AddIndexCondition</c> as the query is read, which is the next member to be ported.
    /// </remarks>
    private readonly List<IndexCondition> indexConditions = [];

    private readonly int hashCode;

    private readonly int orderInFrom;

    /// <summary>
    /// Creates a filter over a table.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="table">the table</param>
    /// <param name="alias">the alias, or null</param>
    /// <param name="rightsChecked">whether the right to read the table was already checked</param>
    /// <param name="select">the select this filter belongs to, or null</param>
    /// <param name="orderInFrom">the position of this table in the FROM clause</param>
    /// <param name="indexHints">which indexes the caller allowed, or null</param>
    public TableFilter(
        SessionLocal session,
        Table table,
        string? alias,
        bool rightsChecked,
        Command.Query.Select? select,
        int orderInFrom,
        IndexHints? indexHints)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(table);
        this.session = session;
        this.table = table;
        this.alias = alias;
        this.select = select;

        if (!rightsChecked)
        {
            session.CheckReadRight(table);
        }

        hashCode = session.NextObjectId();
        this.orderInFrom = orderInFrom;
        this.indexHints = indexHints;
    }

    /// <summary>
    /// Get the order number (index) of this table filter in the "from" clause of the query.
    /// </summary>
    /// <returns>the index (0, 1, 2,...)</returns>
    public int GetOrderInFrom() => orderInFrom;

    /// <summary>
    /// Get the select statement this table filter belongs to, or null.
    /// </summary>
    /// <returns>the select statement, or null</returns>
    public Command.Query.Select? GetSelect() => select;

    /// <summary>
    /// Get the table.
    /// </summary>
    /// <returns>the table</returns>
    public Table GetTable() => table;

    /// <summary>
    /// Whether this filter has an alias.
    /// </summary>
    /// <returns>true if it has</returns>
    public bool HasAlias() => alias is not null;

    /// <summary>
    /// Get the table alias: the alias, or the table's own name when there is none.
    /// </summary>
    /// <remarks>
    /// The fallback is not a convenience. "SELECT NAME FROM TEST" reads through the same path
    /// as "SELECT T.NAME FROM TEST T": a column that was written without a qualifier still has
    /// to be answered as a column *of this table*, and the table's own name is the only name
    /// that must always mean it.
    /// </remarks>
    /// <returns>the alias</returns>
    public string GetTableAlias()
    {
        if (alias is not null)
        {
            return alias;
        }

        return table.GetTableName();
    }

    /// <summary>
    /// Whether this is the outer side of an outer join, directly or through a nested one.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsJoinOuterIndirect() => joinOuterIndirect;

    /// <inheritdoc/>
    /// <remarks>
    /// The columns of the table, as in the reference. A derived table contributes its own, and
    /// that is the only case where this is answered differently — see <see cref="FindColumn"/>
    /// for why the two go together.
    /// </remarks>
    public Column[] GetColumns() => table.GetColumns();

    /// <summary>
    /// Finds a column by name: a derived column first, then the table's own.
    /// </summary>
    /// <remarks>
    /// Not ported. The derived map is where a view's own names live, and it is filled while the
    /// view is read; without it a name that only exists inside a view would fall through to the
    /// table behind it and be answered with the wrong column.
    /// </remarks>
    /// <param name="name">the name</param>
    /// <returns>the column</returns>
    public Column? FindColumn(string? name)
        => throw new TODOException("TableFilter.findColumn: the derived map is not filled yet");

    /// <summary>
    /// Reads a column out of the row the cursor is on.
    /// </summary>
    /// <remarks>
    /// Not ported. It is the row's own value, and answering it needs the search row the cursor
    /// found and the rules for a rowid — which is <c>ValueBigint.get(currentSearchRow.getKey())</c>
    /// when the column asks for the key rather than a value. Until that is here, a column read
    /// outside a scan would give the same answer as a failed read inside one, which is the
    /// failure this refuses rather than reports.
    /// </remarks>
    /// <param name="column">the column</param>
    /// <returns>the value</returns>
    public Value GetValue(Column column)
        => throw new TODOException("TableFilter.getValue: the cursor's row is not readable yet");

    /// <summary>
    /// Finds the rowid column of the table this filter reads.
    /// </summary>
    /// <remarks>
    /// Not ported. It is <c>table.getRowIdColumn()</c>, and that method is not ported either:
    /// answering without it would need knowing which column is the rowid, and there are three
    /// candidates the reference treats differently depending on the table.
    /// </remarks>
    /// <returns>the column</returns>
    public Column? GetRowIdColumn()
        => throw new TODOException("TableFilter.getRowIdColumn: Table.getRowIdColumn is not ported");

    /// <summary>
    /// Gets the filter this resolver reads from: itself.
    /// </summary>
    /// <returns>this filter</returns>
    public TableFilter? GetTableFilter() => this;

    /// <inheritdoc/>
    public override int GetHashCode() => hashCode;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    /// <summary>
    /// Adds a condition to the filter, to be answered without reading a row or not.
    /// </summary>
    /// <remarks>
    /// Not ported. It is where the query hands the filter the conditions it will have to answer,
    /// and a filter that swallowed them would answer every row in and every condition out.
    /// </remarks>
    /// <param name="condition">the condition</param>
    /// <param name="isJoin">whether this is part of the join</param>
    public void AddFilterCondition(Expression? condition, bool isJoin)
        => throw new TODOException("TableFilter.addFilterCondition: the planner keeps them");

    /// <summary>
    /// Gets the session this filter runs on.
    /// </summary>
    /// <returns>the session</returns>
    public SessionLocal GetSession() => session;

    /// <summary>
    /// Sets the session this filter runs on.
    /// </summary>
    /// <param name="s">the session</param>
    public void SetSession(SessionLocal s)
    {
        ArgumentNullException.ThrowIfNull(s);
        session = s;
    }
}
