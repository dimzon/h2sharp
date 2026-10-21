/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.DerivedTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A derived table: a table whose rows are the rows of a query.
 */
using System.Text;
using H2Sharp.Command;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Util;

namespace H2Sharp.Tables;

/// <summary>
/// A derived table: the table a query in the FROM clause stands for.
/// </summary>
/// <remarks>
/// The rows are the rows of the query, and the index over the table runs that query. The
/// query is the one the statement was parsed into, so the parameters of the derived table are
/// the parameters of the statement: the reference prepares the query again from its text and
/// copies the values into the new parameters, and the port has no need to, because the
/// parameters it keeps are the same objects the caller sets.
/// </remarks>
public sealed class DerivedTable : QueryExpressionTable
{
    private readonly Query? topQuery;

    private readonly Index index;

    /// <summary>
    /// Creates a derived table out of a query.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="name">the name of the table</param>
    /// <param name="columnTemplates">the names the statement gave the columns, or null</param>
    /// <param name="query">the query the rows come from</param>
    /// <param name="topQuery">the query the table is a part of, or null</param>
    public DerivedTable(
        SessionLocal session,
        string name,
        Column[]? columnTemplates,
        Query query,
        Query? topQuery)
        : base(session.GetDatabase().GetMainSchema(), name)
    {
        SetTemporary(true);
        this.topQuery = topQuery;
        query.PrepareExpressions();
        tables = [.. query.GetTables()];
        columns = InitColumns(session, columnTemplates, query, true, false);
        viewQuery = query;
        index = new RegularQueryExpressionIndex(this, query);
    }

    /// <inheritdoc/>
    public override Query? GetTopQuery() => topQuery;

    /// <inheritdoc/>
    public override Index GetScanIndex(SessionLocal session) => index;

    /// <inheritdoc/>
    public override Index? GetIndexFor(SessionLocal session, Result.SearchRow row) => GetScanIndex(session);

    /// <inheritdoc/>
    public override string GetSql(int sqlFlags) => GetSql(new StringBuilder(), sqlFlags).ToString();

    /// <inheritdoc/>
    /// <remarks>
    /// The reference writes the text of the query the table stands for, in parentheses; this
    /// port has no plan text yet, so the table writes its name.
    /// </remarks>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => builder.Append(name);
}
