/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.index.QueryExpressionIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The index of a table whose rows are the rows of a query.
 */
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Util;

namespace H2Sharp.Indexes;

/// <summary>
/// This object represents a virtual index for a query expression.
/// </summary>
public abstract class QueryExpressionIndex : Index
{
    /// <summary>
    /// The query the rows come from.
    /// </summary>
    protected Query query = null!;

    /// <summary>
    /// Creates an index over a query.
    /// </summary>
    /// <param name="table">the table the index belongs to</param>
    protected QueryExpressionIndex(QueryExpressionTable table)
        : base(table, 0, "QUERY_EXPRESSION_INDEX", [], 0, IndexType.CreateNonUnique(false))
    {
        columns = [];
    }

    /// <summary>
    /// The query the rows come from.
    /// </summary>
    /// <returns>the query</returns>
    public Query GetQuery() => query;

    /// <inheritdoc/>
    public override string GetPlanSql()
        => query.GetPlanSql(HasSql.TraceSqlFlags | HasSql.AddPlanInformation);

    /// <inheritdoc/>
    public override void Close(SessionLocal session)
    {
    }

    /// <inheritdoc/>
    public override void Add(SessionLocal session, Row row)
        => throw DbException.GetUnsupportedException(GetType().Name + ".add");

    /// <inheritdoc/>
    public override void Remove(SessionLocal session, Row row)
        => throw DbException.GetUnsupportedException(GetType().Name + ".remove");

    /// <inheritdoc/>
    public override void Remove(SessionLocal session)
        => throw DbException.GetUnsupportedException(GetType().Name + ".remove");

    /// <inheritdoc/>
    public override void Truncate(SessionLocal session)
        => throw DbException.GetUnsupportedException(GetType().Name + ".truncate");

    /// <inheritdoc/>
    public override bool NeedRebuild() => false;

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session) => 0L;

    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session) => 0L;
}
