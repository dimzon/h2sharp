/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.ShadowTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;

namespace H2Sharp.Tables;

/// <summary>A temporary shadow table for recursive queries.</summary>
public sealed class ShadowTable : VirtualConstructedTable
{
    private readonly Column[] columns;

    private readonly Schema schema;

    private readonly string name;

    /// <summary>Creates the shadow table.</summary>
    public ShadowTable(Schema schema, string name, Column[] columns)
    {
        this.schema = schema;
        this.name = name;
        this.columns = columns;
        SetTemporary(true);
    }

    /// <inheritdoc/>
    public override ResultInterface GetResult(SessionLocal session)
        => throw DbException.GetInternalError("shadow table");

    /// <inheritdoc/>
    public override bool CanGetRowCount(SessionLocal session) => false;

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session) => long.MaxValue;

    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session) => long.MaxValue;

    /// <inheritdoc/>
    public override string GetTableName() => name;

    /// <inheritdoc/>
    public override int GetTableType() => TABLE;

    /// <inheritdoc/>
    public override int GetColumnCount() => columns.Length;

    /// <inheritdoc/>
    public override Column GetColumn(int index) => columns[index];

    /// <inheritdoc/>
    public override Column[] GetColumns() => columns;

    /// <inheritdoc/>
    public override Index[] GetIndexes() => [];

    /// <inheritdoc/>
    public override Index? GetIndexFor(SessionLocal session, SearchRow row) => GetScanIndex(session);

    /// <inheritdoc/>
    public override string? GetCreateSql() => null;

    /// <inheritdoc/>
    public override string GetSql(int sqlFlags) => name;

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => builder.Append(name);

    /// <inheritdoc/>
    public override void Truncate(SessionLocal session)
    {
    }

    /// <inheritdoc/>
    public override void RemoveChildrenAndResources(SessionLocal session)
    {
    }

    /// <inheritdoc/>
    public override void Close()
    {
    }

    /// <inheritdoc/>
    public override bool IsClosed() => false;

    /// <inheritdoc/>
    public override Schema? GetSchema() => schema;
}
