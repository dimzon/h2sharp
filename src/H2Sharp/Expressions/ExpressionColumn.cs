/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.ExpressionColumn.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A reference to a column of a row that is being read.
 *
 * This is the expression that makes the query engine work, and the design is worth stating in
 * one sentence: a parsed query does not know where its columns are, and mapColumns is the pass
 * that tells it. Until that pass runs, an ExpressionColumn holds a table alias and a column name
 * as *text* and can only be mapped by name; afterwards it holds the resolved Column and reads
 * through the resolver. That two-stage life is why the reference insists on mapColumns being
 * called before the plan is built, and why getValue on an unmapped column is a failure rather
 * than a silent null — a column that quietly resolved to NULL would turn "wrong column name" into
 * "empty result", which is the worst way for that mistake to appear.
 *
 * The query level is the other subtlety. A column of an outer query is readable while an inner
 * one is being evaluated, but not the other way round; the counter is incremented as the
 * optimiser descends, and a column that ends up at level 1 while the resolver is at level 0 is a
 * correlated subquery, which is legal, while the reverse is not.
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// A column of a row that is being read.
/// </summary>
public sealed class ExpressionColumn : Expression
{
    private string? columnAlias;

    private string? tableAlias;

    private Column? column;

    private int queryLevel;

    /// <summary>
    /// What this was mapped through, for the callers that must know which table a shared name
    /// came from.
    /// </summary>
    private ColumnResolver? columnResolver;

    /// <summary>
    /// Creates a new expression column.
    /// </summary>
    /// <param name="column">the column</param>
    /// <param name="resolver">the resolver</param>
    /// <param name="queryLevel">the query level</param>
    public ExpressionColumn(Column column, ColumnResolver resolver, int queryLevel)
    {
        this.column = column;
        columnAlias = resolver.GetColumnName(column);
        tableAlias = resolver.GetTableAlias();
        this.queryLevel = queryLevel;
        columnResolver = resolver;
    }

    /// <summary>
    /// Creates a new expression column by table alias and column name.
    /// </summary>
    /// <param name="columnName">the column name</param>
    /// <param name="tableAlias">the table alias</param>
    /// <param name="queryLevel">the query level</param>
    public ExpressionColumn(string columnName, string? tableAlias, int queryLevel)
    {
        columnAlias = columnName;
        this.tableAlias = tableAlias;
        this.queryLevel = queryLevel;
    }

    /// <summary>
    /// Gets the column this expression reads.
    /// </summary>
    /// <remarks>
    /// Null until the expression has been mapped, and null after it has been mapped to an alias
    /// and a name rather than to a column — see <see cref="GetTableFilter"/> for why that
    /// difference is in the design and not an omission.
    /// </remarks>
    /// <returns>the column, or null</returns>
    public Column? GetColumn()
    {
        return column;
    }

    /// <summary>
    /// Gets the table filter this expression reads from.
    /// </summary>
    /// <remarks>
    /// The reference keeps the filter on the expression, because a query can name the same table
    /// twice and a column reference has to say which of them it means. Here that is the
    /// resolver it was mapped through, which answers for itself.
    /// </remarks>
    /// <returns>the table filter, or null</returns>
    public TableFilter? GetTableFilter() => columnResolver?.GetTableFilter();

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        // Bound columns read through their resolver, as the reference reads through its own:
        // the group-data detour above it waits for grouping. Only unmapped columns go through
        // the session, which does not resolve yet.
        if (columnResolver is not null && column is not null)
        {
            return columnResolver.GetValue(column)!;
        }

        if (tableAlias is null)
        {
            throw new TODOException("ExpressionColumn.GetValue: no resolver is set on this expression");
        }

        ColumnResolver? resolver = session.GetResolver(tableAlias);
        if (resolver is null || column is null)
        {
            throw new TODOException("ExpressionColumn.GetValue: the column was not mapped");
        }

        // Null passes through as it does in the reference: no row to read from fails where
        // the value is used, not here.
        return resolver.GetValue(column)!;
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        if (column is null)
        {
            throw new TODOException("ExpressionColumn.GetTypeInfo: the column was not mapped");
        }

        return column.GetTypeInfo();
    }

    /// <summary>
    /// Maps this column to a column of a resolver, or fails with the name it was looking for.
    /// </summary>
    /// <param name="resolver">the resolver</param>
    /// <param name="alias">the alias of the column, or null to use the column name</param>
    /// <param name="checkAll">true to check all columns, false to stop at the first match</param>
    /// <returns>whether a column was found</returns>
    public bool MapColumn(ColumnResolver resolver, string? alias, bool checkAll)
    {
        bool found = false;
        string? foundColumn = null;
        foreach (Column c in resolver.GetColumns())
        {
            string? cAlias = resolver.GetColumnName(c);
            if (tableAlias == null || tableAlias.Equals(resolver.GetTableAlias(), StringComparison.Ordinal))
            {
                if (alias == null && cAlias.Equals(columnAlias, StringComparison.Ordinal))
                {
                    column = c;
                    found = true;
                    break;
                }

                if (cAlias.Equals(columnAlias, StringComparison.Ordinal)
                    && (alias == null || alias.Equals(cAlias, StringComparison.Ordinal)))
                {
                    foundColumn = cAlias;
                    column = c;
                    found = true;
                    if (!checkAll)
                    {
                        break;
                    }
                }
            }
        }

        if (!found && foundColumn is not null)
        {
            columnAlias = foundColumn;
        }

        // First mapping wins, as the reference keeps the first resolver: a later one with a
        // different filter is an ambiguous column, which waits for its own error.
        if (found && columnResolver is null)
        {
            columnResolver = resolver;
        }

        return found;
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        for (; level < queryLevel; level++)
        {
            _ = resolver.IncrementQueryLevel(1);
        }

        if (state != MapInWindow && MapColumn(resolver, tableAlias, false))
        {
            if (state == MapInitial && level == 0)
            {
                // Not worth the trouble
                queryLevel = 0;
                level = 0;
            }

            return;
        }

        if (state == MapInitial || state == MapInAggregate)
        {
            queryLevel++;
            return;
        }

        throw new TODOException("ExpressionColumn.MapColumns: the column was not found");
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        return this;
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        if (tableAlias != null)
        {
            ParserUtil.QuoteIdentifier(builder, tableAlias, sqlFlags).Append('.');
        }

        return ParserUtil.QuoteIdentifier(builder, columnAlias ?? string.Empty, sqlFlags);
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return true;
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        return 1;
    }

    /// <inheritdoc/>
    public override string? GetTableAlias()
    {
        return tableAlias;
    }

    /// <summary>
    /// Gets the table alias this was written with, before any mapping.
    /// </summary>
    /// <remarks>
    /// The name as written, not as resolved: init matches ORDER BY names against the
    /// select list by text, and a resolved column would match everything.
    /// </remarks>
    /// <returns>the table alias, or null</returns>
    public string? GetOriginalTableAliasName()
    {
        return tableAlias;
    }

    /// <summary>
    /// Gets the column name this was written with, before any mapping.
    /// </summary>
    /// <returns>the column name</returns>
    public string? GetOriginalColumnName()
    {
        return columnAlias;
    }

    /// <inheritdoc/>
    public override string? GetSchemaName()
    {
        return column?.GetTable()?.GetSchemaName();
    }

    /// <inheritdoc/>
    public override string? GetTableName()
    {
        return column?.GetTable()?.GetTableName();
    }

    /// <inheritdoc/>
    public override int GetNullable()
    {
        return column is null ? Column.NullableUnknown : column.IsNullable();
    }

    /// <inheritdoc/>
    public override string GetAlias(SessionLocal session, int columnIndex)
    {
        return columnAlias ?? GetSql(HasSql.QuoteOnlyWhenRequired | HasSql.NoCasts, WithoutParentheses);
    }

    /// <inheritdoc/>
    public override string GetColumnName(SessionLocal session, int columnIndex)
    {
        return columnAlias ?? GetSql(HasSql.QuoteOnlyWhenRequired | HasSql.NoCasts, WithoutParentheses);
    }
}
