/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * A command builder over the provider.
 *
 * The base class does the generation: it asks the select command for its schema table,
 * determines the base table and its key columns, and assembles the INSERT, UPDATE and DELETE
 * text and parameters. What the provider supplies is the dialect: H2 quotes identifiers with
 * double quotes and binds values by position with "?", so the placeholder is "?" and the
 * parameter names are labels that never reach the statement text.
 *
 * The one part the base class cannot know by itself is which result columns are the key.
 * The engine's result metadata names the base table and column of every resolved column, but
 * it does not carry key membership, because JDBC metadata has no such column either. The
 * builder therefore looks the table up in the schema it came from and asks the table for its
 * primary key; a statement whose columns name no base table, or whose table has no primary
 * key, gets no key columns and the base class refuses the UPDATE or DELETE rather than
 * generating a WHERE clause that would match more than the row it came from.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Data;

/// <summary>
/// Builds single-table commands from a select command.
/// </summary>
public sealed class H2CommandBuilder : DbCommandBuilder
{
    /// <summary>
    /// The name of the schema-table column that carries the engine's declared type name.
    /// </summary>
    private const string DataTypeNameColumn = "DataTypeName";

    private H2DataAdapter? rowUpdatingAdapter;

    /// <summary>
    /// Creates a builder with no adapter.
    /// </summary>
    public H2CommandBuilder()
    {
        // H2 spells a quoted identifier with double quotes; the base defaults to no quoting
        // at all, which would turn a lowercase quoted name into an upper-cased unquoted one.
        QuotePrefix = "\"";
        QuoteSuffix = "\"";
    }

    /// <summary>
    /// Creates a builder for the given adapter.
    /// </summary>
    /// <param name="adapter">the adapter</param>
    public H2CommandBuilder(H2DataAdapter adapter)
        : this()
    {
        DataAdapter = adapter;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing to apply for most columns: the parameter keeps the caller's CLR value and the
    /// engine converts it to the column it goes into when the statement runs. The base class has
    /// already set SourceColumn, SourceVersion and SourceColumnNullMapping, which is what the
    /// adapter reads a row value through. A CLR TimeSpan is the exception: the engine sends it as
    /// an interval unless the parameter declares a time of day, and the schema row knows the
    /// column was declared TIME.
    /// </remarks>
    protected override void ApplyParameterInfo(
        DbParameter parameter, DataRow row, StatementType statementType, bool whereClause)
    {
        if (row.Table.Columns.Contains(DataTypeNameColumn)
            && string.Equals(
                row[DataTypeNameColumn] as string,
                Values.Value.GetTypeName(Values.Value.Time),
                StringComparison.Ordinal))
        {
            parameter.DbType = DbType.Time;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A name, not text: H2 binds values by position, so the generated statement carries "?"
    /// and this name only labels the parameter for the caller and the adapter.
    /// </remarks>
    protected override string GetParameterName(int parameterOrdinal)
        => "p" + parameterOrdinal.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    /// <remarks>
    /// The name is returned as-is, because a named parameter in this provider is spelled
    /// "$name" in the statement text and the name the rewriter looks up is bare.
    /// </remarks>
    protected override string GetParameterName(string parameterName) => parameterName;

    /// <inheritdoc/>
    /// <remarks>
    /// H2's anonymous parameter: each "?" takes the next value in collection order, which is
    /// the order the base class adds them in.
    /// </remarks>
    protected override string GetParameterPlaceholder(int parameterOrdinal) => "?";

    /// <inheritdoc/>
    /// <remarks>
    /// The base class calls this once to attach and once to detach; the adapter exposes
    /// typed row events, so the handler is subscribed there and forwarded as the base's
    /// argument type.
    /// </remarks>
    protected override void SetRowUpdatingHandler(DbDataAdapter adapter)
    {
        if (adapter is not H2DataAdapter h2Adapter)
        {
            return;
        }

        if (ReferenceEquals(rowUpdatingAdapter, h2Adapter))
        {
            h2Adapter.RowUpdating -= OnRowUpdating;
            rowUpdatingAdapter = null;
            return;
        }

        if (rowUpdatingAdapter is not null)
        {
            rowUpdatingAdapter.RowUpdating -= OnRowUpdating;
        }

        rowUpdatingAdapter = h2Adapter;
        h2Adapter.RowUpdating += OnRowUpdating;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The reader reports the base table, schema and column of every resolved column, but it
    /// has no connection to ask which of those columns form the key. That question is
    /// answered here from the schema the select reads, and only for real table columns.
    /// </remarks>
    protected override DataTable? GetSchemaTable(DbCommand sourceCommand)
    {
        DataTable? schemaTable = base.GetSchemaTable(sourceCommand);
        if (schemaTable is null
            || sourceCommand.Connection is not H2Connection connection
            || connection.Session is not { } session)
        {
            return schemaTable;
        }

        DataColumn? tableNameColumn = schemaTable.Columns[SchemaTableColumn.BaseTableName];
        DataColumn? baseColumnNameColumn = schemaTable.Columns[SchemaTableColumn.BaseColumnName];
        if (tableNameColumn is null || baseColumnNameColumn is null)
        {
            // Not a schema table this provider produced: it carries no base table to look up.
            return schemaTable;
        }

        DataColumn? schemaNameColumn = schemaTable.Columns[SchemaTableColumn.BaseSchemaName];
        DataColumn keyColumn = EnsureBooleanColumn(schemaTable, SchemaTableColumn.IsKey);
        DataColumn readOnlyColumn = EnsureBooleanColumn(schemaTable, SchemaTableOptionalColumn.IsReadOnly);
        DataColumn autoIncrementColumn =
            EnsureBooleanColumn(schemaTable, SchemaTableOptionalColumn.IsAutoIncrement);

        Dictionary<(string Schema, string Table), BaseTable?> tables = [];
        foreach (DataRow row in schemaTable.Rows)
        {
            string? tableName = row[tableNameColumn] as string;
            if (string.IsNullOrEmpty(tableName))
            {
                continue;
            }

            string? schemaName = schemaNameColumn is null ? null : row[schemaNameColumn] as string;
            (string, string) lookup = (schemaName ?? string.Empty, tableName);
            if (!tables.TryGetValue(lookup, out BaseTable? baseTable))
            {
                baseTable = ResolveBaseTable(session, schemaName, tableName);
                tables.Add(lookup, baseTable);
            }

            string? baseColumnName = row[baseColumnNameColumn] as string;
            Column? column = baseTable is null || string.IsNullOrEmpty(baseColumnName)
                ? null
                : baseTable.Table.FindColumn(baseColumnName);
            if (column is null)
            {
                continue;
            }

            row[keyColumn] = baseTable!.KeyColumns?.Contains(column.GetName()) == true;
            // A computed column is filled by the engine; an INSERT that named it would be
            // refused, and an UPDATE of it likewise.
            row[readOnlyColumn] = column.IsGenerated();
            row[autoIncrementColumn] = column.IsIdentity();
        }

        return schemaTable;
    }

    private void OnRowUpdating(object? sender, H2RowUpdatingEventArgs e) => RowUpdatingHandler(e);

    private static DataColumn EnsureBooleanColumn(DataTable table, string name)
        => table.Columns[name] ?? table.Columns.Add(name, typeof(bool));

    /// <summary>
    /// Resolves the table a result column was read from and its primary key columns.
    /// </summary>
    /// <param name="session">the session the select runs in</param>
    /// <param name="schemaName">the schema name reported by the result, or null</param>
    /// <param name="tableName">the table name reported by the result</param>
    /// <returns>the table and its key columns, or null when the name is not a schema table</returns>
    private static BaseTable? ResolveBaseTable(SessionLocal session, string? schemaName, string tableName)
    {
        Database database = session.GetDatabase();
        Schema? schema = schemaName is null
            ? database.FindSchema(session.GetCurrentSchemaName())
            : database.FindSchema(schemaName);
        Table? table = schema?.FindTableOrView(session, tableName);
        if (table is null)
        {
            return null;
        }

        HashSet<string>? keyColumns = null;
        if (table.FindPrimaryKey() is { } primaryKey)
        {
            keyColumns = new HashSet<string>(StringComparer.Ordinal);
            foreach (IndexColumn indexColumn in primaryKey.GetColumns())
            {
                _ = keyColumns.Add(indexColumn.GetColumn().GetName());
            }
        }

        return new BaseTable(table, keyColumns);
    }

    /// <summary>
    /// A base table and the names of the columns that identify one of its rows.
    /// </summary>
    private sealed class BaseTable
    {
        internal BaseTable(Table table, HashSet<string>? keyColumns)
        {
            Table = table;
            KeyColumns = keyColumns;
        }

        internal Table Table { get; }

        internal HashSet<string>? KeyColumns { get; }
    }
}
