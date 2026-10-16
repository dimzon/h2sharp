/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.command.query.SelectUnion.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * One UNION of two queries.
 *
 * The branches arrive prepared — the parser builds each one the same way it builds a
 * lone select — so init, mapping and planning have nothing left to do here, and the
 * lifecycle members say so rather than redoing it. What runs here is the merge: the rows
 * of the left, then the rows of the right, deduplicated through the result's own distinct
 * machinery when the union is not ALL. A limit caps the merged rows, which is what makes
 * SingleRow stop after the first branch that answers.
 *
 * ORDER BY and LIMIT written on a branch stay on that branch. The reference hangs a
 * trailing ORDER BY on the union, and a query that relies on that reads its last branch
 * sorted here instead of the whole answer. That shape waits for the union to own its
 * order, and until then the deviation is this paragraph, not a silent agreement.
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Command.Query;

/// <summary>
/// A UNION of two queries.
/// </summary>
public sealed class SelectUnion : Query
{
    private readonly Query left;

    private readonly Query right;

    private readonly bool distinct;

    /// <summary>
    /// Creates a union of two prepared queries.
    /// </summary>
    /// <param name="left">the left branch</param>
    /// <param name="right">the right branch</param>
    /// <param name="distinct">whether duplicate rows collapse</param>
    public SelectUnion(Query left, Query right, bool distinct)
        : base(left.GetSession(), left.GetSql())
    {
        this.left = left;
        this.right = right;
        this.distinct = distinct;
        if (left.GetColumnCount() != right.GetColumnCount())
        {
            throw Message.DbException.Get(Api.ErrorCode.ColumnCountDoesNotMatch);
        }
    }

    /// <inheritdoc/>
    public override Expression[] GetExpressions() => left.GetExpressions();

    /// <inheritdoc/>
    public override int GetColumnCount() => left.GetColumnCount();

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session, List<TableFilter> filters, long limit)
        => left.GetRowCount(session, filters, limit) + right.GetRowCount(session, filters, limit);

    /// <inheritdoc/>
    public override ResultInterface Run(long limit)
    {
        SessionLocal session = GetSession();
        Expression[] expressions = GetExpressions();
        LocalResult result = new(session, expressions, GetColumnCount(), GetColumnCount());
        if (distinct)
        {
            result.SetDistinct();
        }

        foreach (Query branch in (Query[])[left, right])
        {
            using ResultInterface rows = branch.Run(-1);
            int visible = rows.GetVisibleColumnCount();
            while ((limit < 0 || result.GetRowCount() < limit) && rows.Next())
            {
                Value?[] full = rows.CurrentRow();
                Value?[] row = full.Length == visible ? full : full[..visible];
                result.AddRow(row);
            }

            if (limit >= 0 && result.GetRowCount() >= limit)
            {
                break;
            }
        }

        result.Done();
        return result;
    }

    /// <inheritdoc/>
    public override void Init()
    {
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, bool outer)
    {
    }

    /// <inheritdoc/>
    public override void PrepareExpressions()
    {
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Select;

    /// <inheritdoc/>
    public override bool IsTransactional() => true;

    /// <inheritdoc/>
    public override ResultInterface? QueryMeta() => null;

    /// <inheritdoc/>
    public override bool IsQuery() => true;
}
