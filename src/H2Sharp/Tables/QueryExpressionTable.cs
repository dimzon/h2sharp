/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.QueryExpressionTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A derived table or a common table expression: a table whose rows are the rows of a query.
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Command;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Values;

namespace H2Sharp.Tables;

/// <summary>
/// A derived table or a common table expression.
/// </summary>
/// <remarks>
/// The reference's table has no rows of its own: the rows are the rows of the query it holds,
/// and the index over it runs that query. The columns are the columns of the query, or the
/// names the statement gave them.
/// </remarks>
public abstract class QueryExpressionTable : Table
{
    private const long RowCountApproximation = 100;

    /// <summary>
    /// The query the rows come from.
    /// </summary>
    protected Query viewQuery = null!;

    /// <summary>
    /// The tables the query reads.
    /// </summary>
    protected List<Table> tables = [];

    /// <summary>
    /// The name of the table.
    /// </summary>
    protected readonly string name;

    /// <summary>
    /// The schema the table belongs to.
    /// </summary>
    private readonly Schema? schema;

    /// <summary>
    /// The columns of the table.
    /// </summary>
    protected Column[] columns = [];

    /// <summary>
    /// Creates a table over a query.
    /// </summary>
    /// <param name="schema">the schema the table belongs to</param>
    /// <param name="name">the name of the table</param>
    protected QueryExpressionTable(Schema? schema, string name)
    {
        this.schema = schema;
        this.name = name;
    }

    /// <summary>
    /// The query the rows come from.
    /// </summary>
    /// <returns>the query</returns>
    public Query GetQuery() => viewQuery;

    /// <summary>
    /// The query the table is a part of, or null when the table stands on its own.
    /// </summary>
    /// <returns>the top query, or null</returns>
    public abstract Query? GetTopQuery();

    /// <summary>
    /// Creates the columns of the table out of the columns of the query.
    /// </summary>
    /// <remarks>
    /// The names the statement gave the columns win; without them the name of the expression
    /// is the name, and the type of the expression is the type.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="columnTemplates">the names the statement gave, or null</param>
    /// <param name="query">the query</param>
    /// <param name="isDerivedTable">whether the table is a derived table rather than a common
    /// table expression</param>
    /// <param name="cte">whether the table is a common table expression</param>
    /// <returns>the columns</returns>
    protected Column[] InitColumns(
        SessionLocal session,
        Column[]? columnTemplates,
        Query query,
        bool isDerivedTable,
        bool cte)
    {
        Expression[] expressions = query.GetExpressions();
        int count = query.GetColumnCount();
        List<Column> list = new(count);
        for (int i = 0; i < count; i++)
        {
            Expression expr = expressions[i];
            string? columnName = null;
            TypeInfo type = TypeInfo.TypeUnknown;
            if (columnTemplates is not null && columnTemplates.Length > i)
            {
                columnName = columnTemplates[i].GetName();
                type = columnTemplates[i].GetTypeInfo();
            }

            if (columnName is null)
            {
                columnName = isDerivedTable
                    ? expr.GetAlias(session, i)
                    : expr.GetColumnName(session, i);
            }

            if (type.GetValueType() == Value.Unknown)
            {
                type = expr.GetTypeInfo();
            }

            list.Add(new Column(columnName, type, this, i));
        }

        return [.. list];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The reference's table carries an object id, and a table that is not in the schema has
    /// none: the id of a derived table is zero.
    /// </remarks>
    public override int GetId() => 0;

    /// <inheritdoc/>
    public override string GetTableName() => name;

    /// <inheritdoc/>
    /// <remarks>
    /// The reference answers null here: a derived table is not a kind of table the catalog
    /// knows about. This hierarchy counts kinds with numbers and has no number for "no kind",
    /// so the derived table answers the kind of a regular table — the same answer the data
    /// change delta table gives.
    /// </remarks>
    public override int GetTableType() => TABLE;

    /// <inheritdoc/>
    public override int GetColumnCount() => columns.Length;

    /// <inheritdoc/>
    public override Column GetColumn(int index) => columns[index];

    /// <inheritdoc/>
    public override Column[] GetColumns() => columns;

    /// <inheritdoc/>
    public override bool IsView() => true;

    /// <inheritdoc/>
    public override Index[] GetIndexes() => [];

    /// <inheritdoc/>
    public override bool IsQueryComparable()
    {
        foreach (Table table in tables)
        {
            if (!table.IsQueryComparable())
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool CanGetRowCount(SessionLocal session) => false;

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session)
        => throw DbException.GetInternalError(GetType().Name);

    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session) => RowCountApproximation;

    /// <inheritdoc/>
    public override string? GetCreateSql() => null;

    /// <inheritdoc/>
    public override void Truncate(SessionLocal session)
        => throw DbException.GetUnsupportedException(GetType().Name + ".truncate");

    /// <inheritdoc/>
    public override void Close()
    {
    }

    /// <inheritdoc/>
    public override bool IsClosed() => false;

    /// <inheritdoc/>
    public override Schema? GetSchema() => schema;

    /// <inheritdoc/>
    public override string GetSql(int sqlFlags) => GetSql(new StringBuilder(), sqlFlags).ToString();

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => builder.Append(name);
}
