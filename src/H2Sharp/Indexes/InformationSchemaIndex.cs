/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.index.MetaIndex, the index of a metadata table.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The scan index of a metadata table: the rows are computed when the table is read, so the
 * index holds nothing and hands out the rows of the view.
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Indexes;

/// <summary>
/// The index of a metadata table: a scan over the rows the view computes.
/// </summary>
public sealed class InformationSchemaIndex : Index
{
    private readonly InformationSchemaTable metaTable;

    /// <summary>
    /// Creates the index of a metadata table.
    /// </summary>
    /// <param name="table">the table</param>
    public InformationSchemaIndex(InformationSchemaTable table)
        : base(table, 0, table.GetTableName() + "_INDEX", [], 0, null)
    {
        metaTable = table;
    }

    /// <inheritdoc/>
    public override Cursor? GetCursor(SessionLocal session, SearchRow? first, SearchRow? last, bool reverse)
        => new InformationSchemaCursor(metaTable.GenerateRows(session));

    /// <inheritdoc/>
    public override double GetCost(
        SessionLocal session,
        int[]? masks,
        TableFilter[]? filters,
        int filter,
        SortOrder? sortOrder,
        Command.Query.AllColumnsForPlan? allColumnsSet,
        bool isSelectCommand)
        => 1d;

    /// <inheritdoc/>
    public override string? GetCreateSql() => null;

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session) => metaTable.GenerateRows(session).Count;

    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session) => InformationSchemaTable.RowCountApproximation;

    /// <inheritdoc/>
    public override bool CanGetFirstOrLast() => false;

    /// <inheritdoc/>
    public override string GetPlanSql() => "information schema scan";

    /// <inheritdoc/>
    public override void Close(SessionLocal session)
    {
    }

    /// <inheritdoc/>
    public override bool NeedRebuild() => false;
}

/// <summary>
/// A cursor over the rows of a metadata table.
/// </summary>
internal sealed class InformationSchemaCursor : Cursor
{
    private readonly List<Value[]> rows;

    private int index = -1;

    /// <summary>
    /// Creates the cursor.
    /// </summary>
    /// <param name="rows">the rows</param>
    internal InformationSchemaCursor(List<Value[]> rows)
    {
        this.rows = rows;
    }

    /// <inheritdoc/>
    public Row? Get() => index < 0 ? null : Row.Get(rows[index], index + 1);

    /// <inheritdoc/>
    public SearchRow? GetSearchRow() => Get();

    /// <inheritdoc/>
    public bool Next()
    {
        if (index + 1 < rows.Count)
        {
            index++;
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool Previous()
    {
        if (index > 0)
        {
            index--;
            return true;
        }

        return false;
    }
}
