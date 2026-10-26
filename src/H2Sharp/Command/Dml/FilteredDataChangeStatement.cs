/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.FilteredDataChangeStatement.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What UPDATE and DELETE have in common, and INSERT does not.
 *
 * Both of them scan one table and act on the rows a condition selects, so the base holds the
 * table and the condition and leaves everything else to the two of them. The table is reached
 * through a filter rather than directly: the reference keeps the filter because it carries the
 * table's alias and the sort order of the scan, and a plan that dropped the alias would print
 * wrong SQL for a statement that used one.
 *
 * Not ported: the members are the reference's and each one refuses.
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Tables;

namespace H2Sharp.Command.Dml;

/// <summary>
/// A data change statement that scans one table for the rows a condition selects.
/// </summary>
public abstract class FilteredDataChangeStatement : DataChangeStatement
{
    private TableFilter? tableFilter;

    private Expression? condition;

    /// <summary>The optional maximum number of rows, as written after TOP.</summary>
    protected Expression? fetchExpr;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    protected FilteredDataChangeStatement(SessionLocal session)
        : base(session)
    {
    }

    /// <inheritdoc/>
    public override Table? GetTable() => tableFilter?.GetTable();

    /// <summary>
    /// Sets the filter of the table being scanned.
    /// </summary>
    /// <param name="tableFilter">the filter</param>
    public void SetTableFilter(TableFilter tableFilter)
    {
        this.tableFilter = tableFilter;
    }

    /// <summary>
    /// Gets the filter of the table being scanned.
    /// </summary>
    /// <returns>the filter</returns>
    public TableFilter GetTableFilter()
        => tableFilter ?? throw new TODOException("FilteredDataChangeStatement.getTableFilter: no filter is set");

    /// <summary>
    /// Sets the condition that selects the rows to change.
    /// </summary>
    /// <remarks>
    /// Null clears it: an optimized-away condition (constant true) is no condition, and a
    /// statement that kept it would evaluate true per row for nothing.
    /// </remarks>
    /// <param name="condition">the condition, or null for all rows</param>
    public void SetCondition(Expression? condition) => this.condition = condition;

    /// <summary>
    /// Gets the condition that selects the rows to change.
    /// </summary>
    /// <returns>the condition, or null for all rows</returns>
    public Expression? GetCondition() => condition;

    /// <summary>
    /// Appends the WHERE and FETCH clauses of this statement, as the reference's
    /// appendFilterCondition does.
    /// </summary>
    protected StringBuilder AppendFilterCondition(StringBuilder builder, int sqlFlags)
    {
        if (condition is not null)
        {
            _ = builder.Append("\nWHERE ");
            _ = condition.GetUnenclosedSql(builder, sqlFlags);
        }

        if (fetchExpr is not null)
        {
            _ = builder.Append("\nFETCH FIRST ");
            string count = fetchExpr.GetSql(sqlFlags, Expression.WithoutParentheses);
            _ = count == "1"
                ? builder.Append("ROW ONLY")
                : builder.Append(count).Append(" ROWS ONLY");
        }

        return builder;
    }

    /// <summary>
    /// Sets the FETCH clause, which is what keeps a data change statement read only.
    /// </summary>
    /// <param name="fetch">the fetch expression</param>
    public void SetFetch(Expression fetch)
        => fetchExpr = fetch;
}
