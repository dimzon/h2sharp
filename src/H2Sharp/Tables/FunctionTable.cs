/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.table.FunctionTable. */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions.Functions.Table;
using H2Sharp.Indexes;
using H2Sharp.Result;
using H2Sharp.Schemas;

namespace H2Sharp.Tables;

/// <summary>A virtual table backed by a table-valued function.</summary>
public class FunctionTable : VirtualConstructedTable
{
    private readonly TableFunction function;
    // Existing Table hierarchy has no schema / columns state; these retain VirtualTable's
    // inherited fields locally, as in the other VirtualConstructedTable ports.
    private readonly Schema schema;
    private readonly Column[] columns;
    /// <summary>Creates a table from the function's template.</summary>
    public FunctionTable(Schema schema, SessionLocal session, TableFunction function)
    {
        this.schema = schema;
        this.function = function;
        function.Optimize(session);
        ResultInterface result = function.GetValueTemplate(session);
        int count = result.GetVisibleColumnCount();
        columns = new Column[count];
        for (int i = 0; i < count; i++)
        {
            columns[i] = new Column(result.GetColumnName(i), result.GetColumnType(i));
            columns[i].SetTable(this, i);
        }
    }
    /// <inheritdoc/>
    public override bool CanGetRowCount(SessionLocal session) => false;
    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session) => long.MaxValue;
    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session) => long.MaxValue;
    /// <inheritdoc/>
    public override ResultInterface GetResult(SessionLocal session) => function.GetValue(session);
    /// <inheritdoc/>
    public override string GetSql(int sqlFlags) => GetSql(new StringBuilder(), sqlFlags).ToString();
    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags) => function.GetSql(builder, sqlFlags);
    /// <summary>Whether this table's function is deterministic.</summary>
    public override bool IsDeterministic() => function.IsDeterministic();
    /// <inheritdoc/>
    public override Column[] GetColumns() => columns;
    /// <inheritdoc/>
    public override Column GetColumn(int index) => columns[index];
    /// <inheritdoc/>
    public override int GetColumnCount() => columns.Length;
    /// <inheritdoc/>
    public override string GetTableName() => function.GetName();
    /// <inheritdoc/>
    public override int GetId() => 0; // VirtualTable constructor receives id=0 in Java.
    /// <inheritdoc/>
    public override Schema GetSchema() => schema;
    /// <inheritdoc/>
    public override int GetTableType() => TABLE;
    /// <inheritdoc/>
    public override string? GetCreateSql() => null;
    /// <inheritdoc/>
    public override Index? GetIndexFor(SessionLocal session, SearchRow row) => GetScanIndex(session);
    /// <inheritdoc/>
    public override Index[] GetIndexes() => [];
    /// <inheritdoc/>
    public override void Truncate(SessionLocal session) { }
    /// <inheritdoc/>
    public override void Close() { }
    /// <inheritdoc/>
    public override bool IsClosed() => false;
}
