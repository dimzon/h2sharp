/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.result.SimpleResult.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A result that is a list, and nothing else.
 *
 * It is both a result and a target: a statement walks it with next() and fills it with addRow(),
 * which is why it implements both interfaces. Every piece of it is a field — no buffering to a
 * temporary file, no lazy evaluation, no closing — and the methods that say so return the honest
 * answer rather than the one a cleverer result would give. That is the reference's own shape:
 * the work is done by LocalResult, and this one is the plain case it shares its interface with.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// Simple in-memory result.
/// </summary>
public class SimpleResult : ResultInterface, ResultTarget
{
    /// <summary>
    /// Column info for the simple result.
    /// </summary>
    internal sealed class Column
    {
        /// <summary>Column alias.</summary>
        internal readonly string Alias;

        /// <summary>Column name.</summary>
        internal readonly string ColumnName;

        /// <summary>Column type.</summary>
        internal readonly TypeInfo ColumnType;

        /// <summary>
        /// Creates a column.
        /// </summary>
        /// <param name="alias">the alias</param>
        /// <param name="columnName">the name</param>
        /// <param name="columnType">the type</param>
        internal Column(string alias, string columnName, TypeInfo columnType)
        {
            ArgumentNullException.ThrowIfNull(alias);
            ArgumentNullException.ThrowIfNull(columnName);
            ArgumentNullException.ThrowIfNull(columnType);
            Alias = alias;
            ColumnName = columnName;
            ColumnType = columnType;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            const int Prime = 31;
            int result = 1;
            result = (Prime * result) + Alias.GetHashCode();
            result = (Prime * result) + ColumnName.GetHashCode();
            return result;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj is not Column other)
            {
                return false;
            }

            return Alias.Equals(other.Alias) && ColumnName.Equals(other.ColumnName);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Alias.Equals(ColumnName) ? ColumnName : ColumnName + ' ' + Alias;
        }
    }

    private readonly List<Column> columns;

    private readonly List<Value?[]> rows;

    private readonly string schemaName;

    private readonly string tableName;

    private int rowId;

    /// <summary>
    /// Creates new instance of simple result.
    /// </summary>
    public SimpleResult()
        : this(string.Empty, string.Empty)
    {
    }

    /// <summary>
    /// Creates new instance of simple result.
    /// </summary>
    /// <param name="schemaName">the name of the schema</param>
    /// <param name="tableName">the name of the table</param>
    public SimpleResult(string schemaName, string tableName)
    {
        columns = Utils.NewSmallArrayList<Column>();
        rows = [];
        this.schemaName = schemaName;
        this.tableName = tableName;
        rowId = -1;
    }

    private SimpleResult(List<Column> columns, List<Value?[]> rows, string schemaName, string tableName)
    {
        this.columns = columns;
        this.rows = rows;
        this.schemaName = schemaName;
        this.tableName = tableName;
        rowId = -1;
    }

    /// <summary>
    /// Add column to the result.
    /// </summary>
    /// <param name="alias">Column's alias.</param>
    /// <param name="columnName">Column's name.</param>
    /// <param name="columnType">Column's value type.</param>
    /// <param name="columnPrecision">Column's precision.</param>
    /// <param name="columnScale">Column's scale.</param>
    public void AddColumn(string alias, string columnName, int columnType, long columnPrecision, int columnScale)
    {
        AddColumn(alias, columnName, TypeInfo.GetTypeInfo(columnType, columnPrecision, columnScale, null));
    }

    /// <summary>
    /// Add column to the result.
    /// </summary>
    /// <param name="columnName">Column's name.</param>
    /// <param name="columnType">Column's type.</param>
    public void AddColumn(string columnName, TypeInfo columnType)
    {
        AddColumn(new Column(columnName, columnName, columnType));
    }

    /// <summary>
    /// Add column to the result.
    /// </summary>
    /// <param name="alias">Column's alias.</param>
    /// <param name="columnName">Column's name.</param>
    /// <param name="columnType">Column's type.</param>
    public void AddColumn(string alias, string columnName, TypeInfo columnType)
    {
        AddColumn(new Column(alias, columnName, columnType));
    }

    /// <summary>
    /// Add column to the result.
    /// </summary>
    /// <param name="column">Column info.</param>
    internal void AddColumn(Column column)
    {
        System.Diagnostics.Debug.Assert(rows.Count == 0);
        columns.Add(column);
    }

    /// <inheritdoc/>
    public void AddRow(params Value?[] values)
    {
        System.Diagnostics.Debug.Assert(values.Length == columns.Count);
        rows.Add(values);
    }

    /// <inheritdoc/>
    public void Reset()
    {
        rowId = -1;
    }

    /// <inheritdoc/>
    public Value?[] CurrentRow()
    {
        return rows[rowId];
    }

    /// <inheritdoc/>
    public bool Next()
    {
        int count = rows.Count;
        if (rowId < count)
        {
            return ++rowId < count;
        }

        return false;
    }

    /// <inheritdoc/>
    public long GetRowId()
    {
        return rowId;
    }

    /// <inheritdoc/>
    public bool IsAfterLast()
    {
        return rowId >= rows.Count;
    }

    /// <inheritdoc/>
    public int GetVisibleColumnCount()
    {
        return columns.Count;
    }

    /// <inheritdoc/>
    public long GetRowCount()
    {
        return rows.Count;
    }

    /// <inheritdoc/>
    public bool HasNext()
    {
        return rowId < rows.Count - 1;
    }

    /// <inheritdoc/>
    public bool NeedToClose()
    {
        return false;
    }

    /// <inheritdoc/>
    public void Close()
    {
        // Do nothing for now
    }

    /// <inheritdoc/>
    public string GetAlias(int i)
    {
        return columns[i].Alias;
    }

    /// <inheritdoc/>
    public string GetSchemaName(int i)
    {
        return schemaName;
    }

    /// <inheritdoc/>
    public string GetTableName(int i)
    {
        return tableName;
    }

    /// <inheritdoc/>
    public string GetColumnName(int i)
    {
        return columns[i].ColumnName;
    }

    /// <inheritdoc/>
    public TypeInfo GetColumnType(int i)
    {
        return columns[i].ColumnType;
    }

    /// <inheritdoc/>
    public bool IsIdentity(int i)
    {
        return false;
    }

    /// <inheritdoc/>
    public int GetNullable(int i)
    {
        return H2Sharp.Result.ResultSetMetaData.ColumnNullableUnknown;
    }

    /// <inheritdoc/>
    public void SetFetchSize(int fetchSize)
    {
        // Ignored
    }

    /// <inheritdoc/>
    public int GetFetchSize()
    {
        return 1;
    }

    /// <inheritdoc/>
    public bool IsLazy()
    {
        return false;
    }

    /// <inheritdoc/>
    public bool IsClosed()
    {
        return false;
    }

    /// <summary>
    /// Creates a shallow copy of this result.
    /// </summary>
    /// <param name="targetSession">the session of the copy</param>
    /// <returns>the copy</returns>
    public SimpleResult CreateShallowCopy(Session targetSession)
    {
        return new SimpleResult(columns, rows, schemaName, tableName);
    }

    /// <inheritdoc/>
    ResultInterface? ResultInterface.CreateShallowCopy(Session targetSession)
    {
        return CreateShallowCopy(targetSession);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public void LimitsWereApplied()
    {
        // Nothing to do
    }

    /// <summary>
    /// Sort rows in the list.
    /// </summary>
    /// <param name="comparator">the comparator</param>
    public void SortRows(Comparison<Value?[]> comparator)
    {
        rows.Sort(comparator);
    }
}
