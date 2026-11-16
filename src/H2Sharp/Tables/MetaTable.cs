/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.table.MetaTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The base of a database metadata pseudo table: the rows are computed when the table is read,
 * and the table is not creatable, droppable or alterable.
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Tables;

/// <summary>
/// The base class of the metadata pseudo tables.
/// </summary>
public abstract class MetaTable : Table
{
    /// <summary>
    /// The approximate number of rows of a meta table.
    /// </summary>
    public const long RowCountApproximation = 1000;

    /// <summary>The table type.</summary>
    protected readonly int Type;

    /// <summary>The indexed column.</summary>
    protected int indexColumn;

    /// <summary>The index for this table, when it has one besides the scan.</summary>
    protected MetaIndex? metaIndex;

    private readonly Database database;

    private readonly Schema schema;

    private readonly int id;

    private readonly List<Index> indexes = new(2);

    private MetaIndex scanIndex = null!;

    private string name = string.Empty;

    private Column[] columns = [];

    /// <summary>
    /// Creates a metadata table. The name, columns and index are set by the derived
    /// constructor, as the reference's setMetaTableName/setColumns/setIndexes do.
    /// </summary>
    /// <param name="schema">the schema</param>
    /// <param name="id">the object id</param>
    /// <param name="type">the meta table type</param>
    protected MetaTable(Schema schema, int id, int type)
    {
        this.schema = schema;
        this.id = id;
        Type = type;
        database = schema.GetDatabase();
    }

    /// <summary>
    /// Creates the scan index. A metadata table that has an additional index installs it in
    /// <see cref="metaIndex"/> before calling this method, as the reference does.
    /// </summary>
    protected void SetIndexes()
    {
        scanIndex = new MetaIndex(this, IndexColumn.Wrap(columns), true);
        indexes.Clear();
        indexes.Add(scanIndex);
        if (metaIndex is not null)
        {
            indexes.Add(metaIndex);
        }
    }

    /// <summary>Sets the meta table name.</summary>
    /// <param name="upperName">the uppercase name</param>
    protected void SetMetaTableName(string upperName) => name = upperName;

    /// <summary>
    /// Creates a character string column with the database's identifier type.
    /// </summary>
    /// <param name="name">the uppercase column name</param>
    /// <returns>the column</returns>
    protected Column Column(string name)
        => Column(name, database.IsCaseInsensitiveIdentifiers()
            ? TypeInfo.TypeVarcharIgnorecase : TypeInfo.TypeVarchar);

    /// <summary>Creates a column with the specified name and type.</summary>
    /// <param name="name">the uppercase column name</param>
    /// <param name="type">the data type</param>
    /// <returns>the column</returns>
    protected Column Column(string name, TypeInfo type) => new(name, type);

    /// <summary>Sets the columns.</summary>
    /// <param name="columns">the columns</param>
    protected void SetColumns(Column[] columns)
    {
        this.columns = columns;
        for (int i = 0; i < columns.Length; i++)
        {
            columns[i].SetTable(this, i);
        }
    }

    /// <summary>
    /// If needed, converts an identifier to lower case. The reference reads
    /// <c>settings.databaseToLower</c>; the port has no such setting, so this is the identity.
    /// </summary>
    /// <param name="s">the identifier</param>
    /// <returns>the identifier</returns>
    protected static string? Identifier(string? s) => s;

    /// <summary>
    /// Checks index conditions against a value.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="value">the value</param>
    /// <param name="indexFrom">the lower bound, or null</param>
    /// <param name="indexTo">the upper bound, or null</param>
    /// <returns>whether the row should be included</returns>
    protected bool CheckIndex(SessionLocal session, string? value, Value? indexFrom, Value? indexTo)
    {
        if (value is null || indexFrom is null && indexTo is null)
        {
            return true;
        }

        Value v = database.IsCaseInsensitiveIdentifiers()
            ? ValueVarcharIgnoreCase.Get(value) : ValueVarchar.Get(value);
        if (indexFrom is not null && session.Compare(v, indexFrom) < 0)
        {
            return false;
        }

        return indexTo is null || session.Compare(v, indexTo) <= 0;
    }

    /// <summary>
    /// Gets all tables of this database, including local temporary tables.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="indexFrom">the first value, or null</param>
    /// <param name="indexTo">the last value, or null</param>
    /// <returns>the tables</returns>
    protected List<Table> GetAllTables(SessionLocal session, Value? indexFrom, Value? indexTo)
    {
        List<Table> result = [];
        if (indexFrom is not null && indexFrom == indexTo)
        {
            string? tableName = indexFrom.GetString();
            if (tableName is not null)
            {
                foreach (Schema s in database.GetSchemas())
                {
                    Table? table = s.FindTableOrView(session, tableName);
                    if (table is not null)
                    {
                        result.Add(table);
                    }
                }

                Table? local = session.FindLocalTempTable(tableName);
                if (local is not null)
                {
                    result.Add(local);
                }
            }
        }
        else
        {
            foreach (Schema s in database.GetSchemas())
            {
                foreach (Table table in s.GetAllTablesAndViews(session))
                {
                    if (CheckIndex(session, table.GetTableName(), indexFrom, indexTo))
                    {
                        result.Add(table);
                    }
                }
            }

            foreach (Table table in session.GetLocalTempTables())
            {
                result.Add(table);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all constraints of this database, including constraints of local temporary tables.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the constraints</returns>
    protected List<Constraint> GetAllConstraints(SessionLocal session)
    {
        List<Constraint> result = [];
        foreach (Schema s in database.GetSchemas())
        {
            result.AddRange(s.GetAllConstraints());
        }

        result.AddRange(session.GetLocalTempTableConstraints().Values);
        return result;
    }

    /// <summary>
    /// Generates the rows of this table, applying the first/last row filters.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="first">the first row, or null</param>
    /// <param name="last">the last row, or null</param>
    /// <returns>the rows</returns>
    public abstract List<Row> GenerateRows(SessionLocal session, SearchRow? first, SearchRow? last);

    /// <summary>Adds a row to a list.</summary>
    /// <param name="session">the session</param>
    /// <param name="rows">the row list</param>
    /// <param name="stringsOrValues">the values, strings or null</param>
    protected void Add(SessionLocal session, List<Row> rows, params object?[] stringsOrValues)
    {
        Value[] values = new Value[stringsOrValues.Length];
        for (int i = 0; i < stringsOrValues.Length; i++)
        {
            object? value = stringsOrValues[i];
            Value v = value switch
            {
                null => ValueNull.Instance,
                string s => ValueVarchar.Get(s),
                _ => (Value)value,
            };

            values[i] = columns[i].Convert(session, v);
        }

        rows.Add(Row.Get(values, 1, rows.Count));
    }

    /// <inheritdoc/>
    public override int GetId() => id;

    /// <inheritdoc/>
    public override string GetTableName() => name;

    /// <inheritdoc/>
    public override int GetTableType() => SYSTEM_TABLE;

    /// <inheritdoc/>
    public override int GetColumnCount() => columns.Length;

    /// <inheritdoc/>
    public override Column GetColumn(int index) => columns[index];

    /// <inheritdoc/>
    public override Column[] GetColumns() => columns;

    /// <inheritdoc/>
    public override Index GetScanIndex(SessionLocal session) => scanIndex;

    /// <inheritdoc/>
    public override Index[] GetIndexes() => [.. indexes];

    /// <inheritdoc/>
    public override Index? GetIndexFor(SessionLocal session, SearchRow row) => scanIndex;

    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session) => RowCountApproximation;

    /// <inheritdoc/>
    public override bool CanGetRowCount(SessionLocal session) => false;

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session)
        => throw DbException.GetInternalError(ToString() ?? name);

    /// <inheritdoc/>
    public override string? GetCreateSql() => null;

    /// <inheritdoc/>
    public override string GetSql(int sqlFlags) => name;

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags) => builder.Append(name);

    /// <inheritdoc/>
    public override void CheckRename() => throw DbException.GetUnsupportedException("META");

    /// <inheritdoc/>
    public override void CheckSupportAlter() => throw DbException.GetUnsupportedException("META");

    /// <inheritdoc/>
    public override long Truncate(SessionLocal session) => throw DbException.GetUnsupportedException("META");

    /// <inheritdoc/>
    public override long AddRow(SessionLocal session, Row row)
        => throw DbException.GetUnsupportedException("META");

    /// <inheritdoc/>
    public override void RemoveRow(SessionLocal session, long key)
        => throw DbException.GetUnsupportedException("META");

    /// <inheritdoc/>
    public override void RemoveChildrenAndResources(SessionLocal session)
        => throw DbException.GetUnsupportedException("META");

    /// <inheritdoc/>
    public override void Close()
    {
    }

    /// <inheritdoc/>
    public override bool IsClosed() => false;

    /// <inheritdoc/>
    public override Schema? GetSchema() => schema;

    /// <inheritdoc/>
    public override bool IsDeterministic() => true;

    /// <inheritdoc/>
    public override bool CanReference() => false;
}
