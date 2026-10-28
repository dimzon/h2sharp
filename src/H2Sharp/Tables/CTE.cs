/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.CTE.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A common table expression: the table a WITH clause names.
 */
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Result;
using H2Sharp.Schemas;

namespace H2Sharp.Tables;

/// <summary>
/// A common table expression: the table a WITH clause names.
/// </summary>
/// <remarks>
/// The rows are the rows of the query the clause names, and the index over the table runs
/// that query.
/// </remarks>
public sealed class CTE : QueryExpressionTable
{
    private Index index = null!;

    private bool recursive;

    private ResultInterface? recursiveResult;

    /// <summary>
    /// Creates a common table expression.
    /// </summary>
    /// <param name="schema">the schema the table belongs to</param>
    /// <param name="name">the name of the table</param>
    /// <param name="columnTemplates">the names the clause gave the columns, or null</param>
    /// <param name="query">the query the rows come from</param>
    /// <param name="session">the session</param>
    public CTE(Schema? schema, string name, Column[]? columnTemplates, Query query, SessionLocal session,
        bool recursive = false)
        : base(schema, name)
        => Initialize(session, columnTemplates, query, recursive);

    internal void Initialize(
        SessionLocal session, Column[]? columnTemplates, Query query, bool recursive)
    {
        SetTemporary(true);
        tables = [.. query.GetTables()];
        columns = InitColumns(session, columnTemplates, query, false, true);
        viewQuery = query;
        this.recursive = recursive;
        index = recursive ? new RecursiveIndex(this, query) : new RegularQueryExpressionIndex(this, query);
    }

    internal void SetColumnsForResolution(Column[] resolutionColumns) => columns = resolutionColumns;

    /// <summary>Whether this expression is recursive.</summary>
    public bool IsRecursive() => recursive;

    /// <summary>The result of the previous iteration of a recursive expression.</summary>
    public ResultInterface? GetRecursiveResult() => recursiveResult;

    /// <summary>Sets the result of the previous iteration of a recursive expression.</summary>
    public void SetRecursiveResult(ResultInterface? value)
    {
        recursiveResult?.Close();
        recursiveResult = value;
    }

    /// <inheritdoc/>
    public override bool IsDeterministic() => !recursive && base.IsDeterministic();

    /// <inheritdoc/>
    public override bool IsQueryComparable() => false;

    /// <inheritdoc/>
    public override Query? GetTopQuery() => null;

    /// <inheritdoc/>
    public override Index GetScanIndex(SessionLocal session) => index;

    /// <inheritdoc/>
    public override Index? GetIndexFor(SessionLocal session, SearchRow row) => GetScanIndex(session);
}
