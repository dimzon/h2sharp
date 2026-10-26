/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.RangeTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The table SYSTEM_RANGE, which generates a range of numbers.
 */
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Values;

namespace H2Sharp.Tables;

/// <summary>
/// The table SYSTEM_RANGE: a virtual table that generates incrementing numbers with a given
/// start and end point.
/// </summary>
public sealed class RangeTable : Table
{
    /// <summary>
    /// The name of the range table.
    /// </summary>
    public const string Name = "SYSTEM_RANGE";

    /// <summary>
    /// The PostgreSQL alias for the range table.
    /// </summary>
    public const string Alias = "GENERATE_SERIES";

    private Expression min;

    private Expression max;

    private Expression? step;

    private bool optimized;

    private readonly RangeIndex index;

    private readonly Schema schema;

    private readonly Column[] columns;

    /// <summary>
    /// Creates a range with the given start and end.
    /// </summary>
    /// <param name="schema">the schema</param>
    /// <param name="min">the start expression</param>
    /// <param name="max">the end expression</param>
    public RangeTable(Schema schema, Expression min, Expression max)
    {
        this.schema = schema;
        this.min = min;
        this.max = max;
        columns = [new Column("X", TypeInfo.TypeBigint)];
        for (int i = 0; i < columns.Length; i++)
        {
            columns[i].SetTable(this, i);
        }

        index = new RangeIndex(this, IndexColumn.Wrap(columns));
    }

    /// <summary>
    /// Creates a range with the given start, end and increment.
    /// </summary>
    /// <param name="schema">the schema</param>
    /// <param name="min">the start expression</param>
    /// <param name="max">the end expression</param>
    /// <param name="step">the increment expression</param>
    public RangeTable(Schema schema, Expression min, Expression max, Expression step)
        : this(schema, min, max)
    {
        this.step = step;
    }

    /// <inheritdoc/>
    public override string GetTableName() => Name;

    /// <inheritdoc/>
    public override int GetTableType() => SYSTEM_TABLE;

    /// <inheritdoc/>
    public override int GetColumnCount() => 1;

    /// <inheritdoc/>
    public override Column GetColumn(int index) => columns[index];

    /// <inheritdoc/>
    public override Column[] GetColumns() => columns;

    /// <inheritdoc/>
    public override Index? GetScanIndex(SessionLocal session) => index;

    /// <inheritdoc/>
    public override Index[] GetIndexes() => [index];

    /// <inheritdoc/>
    public override Index? GetIndexFor(SessionLocal session, SearchRow row) => GetScanIndex(session);

    /// <inheritdoc/>
    public override bool CanGetRowCount(SessionLocal session) => true;

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session)
    {
        long step = GetStep(session);
        if (step == 0L)
        {
            throw DbException.Get(ErrorCode.StepSizeMustNotBeZero);
        }

        long delta = GetMax(session) - GetMin(session);
        if (step > 0)
        {
            if (delta < 0)
            {
                return 0;
            }
        }
        else if (delta > 0)
        {
            return 0;
        }

        return delta / step + 1;
    }

    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session) => 100;

    /// <inheritdoc/>
    public override string? GetCreateSql() => null;

    /// <inheritdoc/>
    public override string GetSql(int sqlFlags) => Name;

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append(Name).Append('(');
        min.GetUnenclosedSql(builder, sqlFlags).Append(", ");
        max.GetUnenclosedSql(builder, sqlFlags);
        if (step is not null)
        {
            step.GetUnenclosedSql(builder.Append(", "), sqlFlags);
        }

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

    /// <summary>
    /// The start value of the range.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the value</returns>
    public long GetMin(SessionLocal session)
    {
        Optimize(session);
        return min.GetValue(session).GetLong();
    }

    /// <summary>
    /// The end value of the range.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the value</returns>
    public long GetMax(SessionLocal session)
    {
        Optimize(session);
        return max.GetValue(session).GetLong();
    }

    /// <summary>
    /// The increment of the range, which is 1 when the statement did not name one.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the value</returns>
    public long GetStep(SessionLocal session)
    {
        Optimize(session);
        return step is null ? 1 : step.GetValue(session).GetLong();
    }

    /// <summary>
    /// Computes the start, the end and the increment once.
    /// </summary>
    /// <param name="session">the session</param>
    private void Optimize(SessionLocal session)
    {
        if (!optimized)
        {
            min = min.Optimize(session);
            max = max.Optimize(session);
            if (step is not null)
            {
                step = step.Optimize(session);
            }

            optimized = true;
        }
    }
}
