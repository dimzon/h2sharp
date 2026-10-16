/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.table.DualTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Result;
using H2Sharp.Schemas;

namespace H2Sharp.Tables;

/// <summary>
/// The DUAL table for selects without a FROM clause.
/// </summary>
public sealed class DualTable : Table
{
    /// <summary>
    /// The name of the DUAL table.
    /// </summary>
    public const string Name = "DUAL";

    private readonly Database database;

    private readonly DualIndex scanIndex;

    private readonly Index[] indexes;

    /// <summary>
    /// Creates a DUAL table.
    /// </summary>
    /// <param name="database">the database</param>
    public DualTable(Database database)
    {
        this.database = database;
        scanIndex = new DualIndex(this);
        indexes = [scanIndex];
    }

    /// <inheritdoc/>
    public override string GetTableName() => Name;

    /// <inheritdoc/>
    public override int GetTableType() => SYSTEM_TABLE;

    /// <inheritdoc/>
    public override int GetColumnCount() => 0;

    /// <inheritdoc/>
    public override Column GetColumn(int index)
        => throw new TODOException("DualTable.getColumn: DUAL has no columns");

    /// <inheritdoc/>
    public override Column[] GetColumns() => [];

    /// <inheritdoc/>
    public override Index? GetScanIndex(SessionLocal session) => scanIndex;

    /// <inheritdoc/>
    /// <remarks>
    /// The one array, shared: this table never gains an index, so no copy guards anything.
    /// </remarks>
    public override Index[] GetIndexes() => indexes;

    /// <inheritdoc/>
    public override Index? GetPrimaryKey() => null;

    /// <inheritdoc/>
    public override Index? GetIndexFor(SessionLocal session, SearchRow row) => GetScanIndex(session);

    /// <inheritdoc/>
    public override long GetRowCountApproximation() => 1L;

    /// <inheritdoc/>
    public override bool CanGetRowCount(SessionLocal session) => true;

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session) => 1L;

    /// <inheritdoc/>
    public override string? GetCreateSql() => null;

    /// <inheritdoc/>
    public override string GetSql(int sqlFlags) => Name;

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags) => builder.Append(Name);

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
    public override Schema? GetSchema() => database.GetMainSchema();
}
