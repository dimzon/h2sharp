/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.index.RegularQueryExpressionIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A regular query expression index: the index of a derived table, which runs the query the
 * table stands for.
 */
using H2Sharp.Command;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tables;

namespace H2Sharp.Indexes;

/// <summary>
/// A regular query expression index.
/// </summary>
/// <remarks>
/// The index runs the query of the table and hands the rows to the cursor. The reference
/// prepares the query again from its text and pushes the conditions of the index into it;
/// this port keeps the query the statement was parsed into and lets the filter of the table
/// apply the conditions, which is where the other tables of the port apply them too.
/// </remarks>
public sealed class RegularQueryExpressionIndex : QueryExpressionIndex
{
    /// <summary>
    /// Creates the index of a derived table.
    /// </summary>
    /// <param name="table">the table</param>
    /// <param name="query">the query the rows come from</param>
    public RegularQueryExpressionIndex(QueryExpressionTable table, Query query)
        : base(table)
    {
        this.query = query;
    }

    /// <inheritdoc/>
    public override double GetCost(
        SessionLocal session,
        int[]? masks,
        TableFilter[]? filters,
        int filter,
        SortOrder? sortOrder,
        AllColumnsForPlan? allColumnsSet,
        bool isSelectCommand)
        => (double)query.GetCost();

    /// <inheritdoc/>
    public override Cursor? GetCursor(SessionLocal session, SearchRow? first, SearchRow? last, bool reverse)
        => new QueryExpressionCursor(this, query.Run(0), first, last);
}
