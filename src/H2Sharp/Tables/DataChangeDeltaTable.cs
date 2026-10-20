/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.DataChangeDeltaTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The rows a statement changed, collected for the caller that asked for them.
 *
 * Three versions of a changed row exist, and which one a caller gets is what the nested enum is
 * for: the row as it was, the new row with its defaults evaluated but before any trigger ran,
 * and the row as it ended up. A trigger that rewrites a row would make OLD and FINAL differ, and
 * a generated-keys caller that was handed NEW would get a row the triggers never saw.
 *
 * The table is what makes the form usable: "SELECT Id FROM FINAL TABLE (INSERT INTO …)" asks
 * the insert for the rows it wrote and reads them as the rows of a table.
 */
using System.Text;
using H2Sharp.Command.Dml;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Indexes;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Values;

namespace H2Sharp.Tables;

/// <summary>
/// The rows a data change statement changed, as a table.
/// </summary>
public sealed class DataChangeDeltaTable : VirtualConstructedTable
{
    /// <summary>
    /// Which version of a changed row the collector holds.
    /// </summary>
    public enum ResultOption
    {
        /// <summary>OLD row.</summary>
        OLD,

        /// <summary>NEW row with evaluated default expressions, but before triggers.</summary>
        NEW,

        /// <summary>FINAL rows after triggers.</summary>
        FINAL,
    }

    private readonly Schema? schema;

    private readonly DataChangeStatement statement;

    private readonly ResultOption resultOption;

    private readonly Expression[] expressions;

    private readonly Column[] columns;

    private readonly Index index;

    /// <summary>
    /// Creates the table of the rows a statement changed.
    /// </summary>
    /// <param name="schema">the schema the table belongs to</param>
    /// <param name="statement">the statement that changes the rows</param>
    /// <param name="resultOption">which version of a changed row is collected</param>
    public DataChangeDeltaTable(Schema? schema, DataChangeStatement statement, ResultOption resultOption)
    {
        this.schema = schema;
        this.statement = statement;
        this.resultOption = resultOption;

        Table? table = statement.GetTable();
        Column[] tableColumns = table?.GetColumns() ?? [];
        int columnCount = tableColumns.Length;
        columns = new Column[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            columns[i] = tableColumns[i].GetClone();
            columns[i].SetTable(this, i);
        }

        expressions = new Expression[columnCount];
        string tableName = GetTableName();
        for (int i = 0; i < columnCount; i++)
        {
            expressions[i] = new ExpressionColumn(columns[i].GetName(), tableName, 0);
        }

        index = new VirtualConstructedTableIndex(this);
    }

    /// <summary>
    /// Collects the final row for INSERT operations.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="table">the table</param>
    /// <param name="deltaChangeCollector">where the changed rows go</param>
    /// <param name="deltaChangeCollectionMode">what to collect, or null for none</param>
    /// <param name="newRow">the inserted row</param>
    public static void CollectForInsert(
        SessionLocal session,
        Table table,
        ResultTarget? deltaChangeCollector,
        ResultOption? deltaChangeCollectionMode,
        Row newRow)
    {
        _ = session;
        _ = table;
        if (deltaChangeCollectionMode == ResultOption.FINAL)
        {
            deltaChangeCollector?.AddRow(newRow.GetValueList());
        }
    }

    /// <inheritdoc/>
    public override ResultInterface GetResult(SessionLocal session)
    {
        statement.Prepare();
        int columnCount = expressions.Length;
        LocalResult result = new(session, expressions, columnCount, columnCount);
        statement.Run(result, resultOption);
        return result;
    }

    /// <inheritdoc/>
    public override string GetTableName() => statement.GetStatementName();

    /// <inheritdoc/>
    public override int GetTableType() => TABLE;

    /// <inheritdoc/>
    public override int GetColumnCount() => columns.Length;

    /// <inheritdoc/>
    public override Column GetColumn(int index) => columns[index];

    /// <inheritdoc/>
    public override Column[] GetColumns() => columns;

    /// <inheritdoc/>
    public override Index[] GetIndexes() => [index];

    /// <inheritdoc/>
    public override Index? GetIndexFor(SessionLocal session, SearchRow row) => GetScanIndex(session);

    /// <inheritdoc/>
    public override bool CanGetRowCount(SessionLocal session) => false;

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session) => long.MaxValue;

    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session) => long.MaxValue;

    /// <inheritdoc/>
    public override string? GetCreateSql() => null;

    /// <inheritdoc/>
    public override string GetSql(int sqlFlags)
    {
        StringBuilder builder = new();
        return GetSql(builder, sqlFlags).ToString();
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append(resultOption.ToString()).Append(" TABLE (");
        _ = builder.Append(statement.GetSql());
        return builder.Append(')');
    }

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
