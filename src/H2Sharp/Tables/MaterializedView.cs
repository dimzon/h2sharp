/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.MaterializedView.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Command.Query;
using H2Query = H2Sharp.Command.Query.Query;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Util;

namespace H2Sharp.Tables;

/// <summary>A materialized view backed by its stored table.</summary>
public sealed class MaterializedView : Table
{
    private readonly Schema schema;

    private readonly int id;

    private string name;

    private Table table;

    private Column[] columns;

    private H2Query query;

    private string querySql;

    /// <summary>Creates a materialized view.</summary>
    public MaterializedView(Schema schema, int id, string name, Table table, H2Query query, string querySql)
    {
        this.schema = schema;
        this.id = id;
        this.name = name;
        this.query = query;
        this.querySql = querySql;
        this.table = table;
        columns = CloneColumns(table);
    }

    /// <summary>Replaces the underlying storage and definition.</summary>
    public void Replace(Table newTable, Query newQuery, string newQuerySql)
    {
        table = newTable;
        query = newQuery;
        querySql = newQuerySql;
        columns = CloneColumns(newTable);
    }

    private static Column[] CloneColumns(Table source)
    {
        Column[] result = new Column[source.GetColumns().Length];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = source.GetColumns()[i].GetClone();
            result[i].SetTable(null, i);
        }

        return result;
    }

    /// <summary>The table that stores the rows.</summary>
    public Table GetUnderlyingTable() => table;

    /// <summary>The query that defines the view.</summary>
    public H2Query GetSelect() => query;

    /// <inheritdoc/>
    public override string GetTableName() => name;

    /// <inheritdoc/>
    public override int GetId() => id;

    /// <inheritdoc/>
    public override Schema? GetSchema() => schema;

    /// <inheritdoc/>
    public override int GetTableType() => MATERIALIZED_VIEW;

    /// <inheritdoc/>
    public override bool IsView() => true;

    /// <inheritdoc/>
    public override int GetColumnCount() => columns.Length;

    /// <inheritdoc/>
    public override Column GetColumn(int index) => columns[index];

    /// <inheritdoc/>
    public override Column[] GetColumns() => columns;

    /// <inheritdoc/>
    public override Index? GetScanIndex(SessionLocal session) => table.GetScanIndex(session);

    /// <inheritdoc/>
    public override Index? GetScanIndex(SessionLocal session, int[]? masks, TableFilter[]? filters,
        int filter, SortOrder? sortOrder, AllColumnsForPlan? allColumnsSet)
        => table.GetScanIndex(session, masks, filters, filter, sortOrder, allColumnsSet);

    /// <inheritdoc/>
    public override PlanItem GetBestPlanItem(SessionLocal session, int[]? masks, TableFilter[]? filters,
        int filter, SortOrder? sortOrder, AllColumnsForPlan? allColumnsSet, bool isSelectCommand)
        => table.GetBestPlanItem(session, masks, filters, filter, sortOrder, allColumnsSet, isSelectCommand);

    /// <inheritdoc/>
    public override Index? GetIndexFor(SessionLocal session, SearchRow row) => table.GetIndexFor(session, row);

    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session)
        => table.GetRowCountApproximation(session);

    /// <inheritdoc/>
    public override bool CanGetRowCount(SessionLocal session) => table.CanGetRowCount(session);

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session) => table.GetRowCount(session);

    /// <inheritdoc/>
    public override Index[] GetIndexes() => table.GetIndexes();

    /// <inheritdoc/>
    public override Index? GetIndex(int index) => table.GetIndex(index);

    /// <inheritdoc/>
    public override int GetIndexCount() => table.GetIndexCount();

    /// <inheritdoc/>
    public override Index AddIndex(SessionLocal session, string? indexName, int indexId, IndexColumn[] cols,
        int uniqueColumnCount, IndexType indexType, bool create, string? indexComment)
        => table.AddIndex(session, indexName, indexId, cols, uniqueColumnCount, indexType, create, indexComment);

    /// <inheritdoc/>
    public override void RemoveIndex(Index index) => table.RemoveIndex(index);

    /// <inheritdoc/>
    public override IndexColumn[] GetPrimaryKeyColumns() => table.GetPrimaryKeyColumns();

    /// <inheritdoc/>
    public override bool HasPrimaryKey() => table.HasPrimaryKey();

    /// <inheritdoc/>
    public override Index? FindPrimaryKey() => table.FindPrimaryKey();

    /// <inheritdoc/>
    public override void CheckSupportAlter()
        => throw DbException.GetUnsupportedException("MATERIALIZED VIEW");

    /// <inheritdoc/>
    public override long AddRow(SessionLocal session, Row row)
        => throw DbException.GetUnsupportedException("MATERIALIZED VIEW.ADD_ROW");

    /// <inheritdoc/>
    public override void UpdateRow(SessionLocal session, Row oldRow, Row newRow)
        => throw DbException.GetUnsupportedException("MATERIALIZED VIEW.UPDATE_ROW");

    /// <inheritdoc/>
    public override void RemoveRow(SessionLocal session, long key)
        => throw DbException.GetUnsupportedException("MATERIALIZED VIEW.REMOVE_ROW");

    /// <inheritdoc/>
    public override void Truncate(SessionLocal session)
        => throw DbException.GetUnsupportedException("MATERIALIZED VIEW.TRUNCATE");

    /// <inheritdoc/>
    public override string? GetCreateSql()
        => GetCreateSql(orReplace: false, force: false);

    /// <summary>CREATE SQL for this view.</summary>
    public string GetCreateSql(bool orReplace, bool force)
    {
        StringBuilder builder = new("CREATE ");
        if (orReplace)
        {
            _ = builder.Append("OR REPLACE ");
        }

        if (force)
        {
            _ = builder.Append("FORCE ");
        }

        _ = builder.Append("MATERIALIZED VIEW ");
        _ = GetSql(builder, HasSql.DefaultSqlFlags);
        if (GetComment() is not null)
        {
            _ = builder.Append(" COMMENT ");
            _ = StringUtils.QuoteStringSql(builder, GetComment()!);
        }

        return builder.Append(" AS\n").Append(querySql).ToString();
    }

    /// <inheritdoc/>
    public override string GetSql(int sqlFlags) => GetSql(new StringBuilder(), sqlFlags).ToString();

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => ParserUtil.QuoteIdentifier(builder, name, sqlFlags);

    /// <inheritdoc/>
    public override void RemoveChildrenAndResources(SessionLocal session)
    {
        Database database = schema.GetDatabase();
        database.RemoveSchemaObject(session, table);
        database.RemoveMeta(session, id);
    }

    /// <inheritdoc/>
    public override void Close() => table.Close();

    /// <inheritdoc/>
    public override bool IsClosed() => table.IsClosed();
}
