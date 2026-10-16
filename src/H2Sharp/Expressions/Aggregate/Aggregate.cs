/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.aggregate.Aggregate.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sliced to COUNT, SUM, MIN, MAX, AVG, the STDDEV / VAR family and ANY / EVERY:
 * the flat, quick and grouped paths carry any of them — reading, mapping and
 * accumulation. Every other aggregate type refuses where the reference would
 * branch to it (the data factory, the value reader and the type switch), so
 * adding the next one is new branches, not a new shape. WITHIN GROUP / ORDER BY lists are not kept: no reader builds
 * them yet, and a field nothing writes is a promise nothing checks.
 */
using System;
using System.Text;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// Implements the integrated aggregate functions, such as COUNT, MAX, SUM.
/// </summary>
public class Aggregate : AbstractAggregate
{
    private readonly AggregateType aggregateType;

    /// <summary>
    /// Create a new aggregate object.
    /// </summary>
    /// <param name="aggregateType">the aggregate type</param>
    /// <param name="args">the aggregated expressions</param>
    /// <param name="select">the select statement</param>
    /// <param name="distinct">if distinct is used</param>
    public Aggregate(AggregateType aggregateType, Expression[] args, Select select, bool distinct)
        : base(select, args, distinct)
    {
        if (distinct && aggregateType == AggregateType.CountAll)
        {
            throw Message.DbException.GetInternalError();
        }

        this.aggregateType = aggregateType;
    }

    /// <summary>
    /// Returns the type of this aggregate.
    /// </summary>
    /// <returns>the type of this aggregate</returns>
    public AggregateType GetAggregateType() => aggregateType;

    /// <inheritdoc/>
    protected override void UpdateAggregate(SessionLocal session, object aggregateData)
    {
        AggregateData data = (AggregateData)aggregateData;
        Value? v = args.Length == 0 ? null : args[0].GetValue(session);
        // COUNT takes the argument as is; the type-specific arms arrive with their types.
        // The reference wraps values for ORDER BY lists here (updateCollecting), but no
        // reader builds those lists yet, so the wrap would be identity.
        data.Add(session, v);
    }

    /// <inheritdoc/>
    protected override object CreateAggregateData()
    {
        switch (aggregateType)
        {
            case AggregateType.CountAll:
                return new AggregateDataCount(true);
            case AggregateType.Count:
                if (!distinct)
                {
                    return new AggregateDataCount(false);
                }

                break;
            case AggregateType.Sum:
            case AggregateType.Avg:
                if (!distinct)
                {
                    return aggregateType switch
                    {
                        AggregateType.Avg => new AggregateDataAvg(type!),
                        _ => new AggregateDataDefault(aggregateType, type!),
                    };
                }

                break;
            case AggregateType.Min:
            case AggregateType.Max:
            case AggregateType.Any:
            case AggregateType.Every:
                // DISTINCT changes nothing for an extremum or a conjunction,
                // duplicates collapse on their own.
                return new AggregateDataDefault(aggregateType, type!);
            case AggregateType.StddevPop:
            case AggregateType.StddevSamp:
            case AggregateType.VarPop:
            case AggregateType.VarSamp:
                if (!distinct)
                {
                    return new AggregateDataStdVar(aggregateType);
                }

                break;
            default:
                throw new TODOException(
                    "Aggregate.createAggregateData: only COUNT, SUM, MIN, MAX, AVG, STDDEV and VAR are ported (type="
                    + aggregateType + ")");
        }

        return new AggregateDataCollecting(
            distinct, false, AggregateDataCollecting.NullCollectionMode.Ignored);
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        return select.IsQuickAggregateQuery() ? GetValueQuick(session) : base.GetValue(session);
    }

    private Value GetValueQuick(SessionLocal session)
    {
        switch (aggregateType)
        {
            case AggregateType.Count:
            case AggregateType.CountAll:
                Table? table = select.GetTopFilter()?.GetTable();
                if (table is null)
                {
                    throw Message.DbException.GetInternalError("type=" + aggregateType);
                }

                return ValueBigint.Get(table.GetRowCount(session));
            case AggregateType.Min:
            case AggregateType.Max:
                {
                    bool first = aggregateType == AggregateType.Min;
                    H2Sharp.Indexes.Index index = GetMinMaxColumnIndex()
                        ?? throw Message.DbException.GetInternalError("type=" + aggregateType);
                    int sortType = index.GetColumns()[0].SortType;
                    if ((sortType & H2Sharp.Result.SortOrder.Descending) != 0)
                    {
                        first = !first;
                    }

                    H2Sharp.Indexes.Cursor cursor = index.FindFirstOrLast(session, first);
                    H2Sharp.Result.SearchRow? row = cursor.GetSearchRow();
                    Value v;
                    if (row is null)
                    {
                        v = ValueNull.Instance;
                    }
                    else
                    {
                        // A missing stored value is NULL the way the reference only knows ValueNull.
                        v = row.GetValue(((ExpressionColumn)args[0]).GetColumn()!.GetColumnId())
                            ?? ValueNull.Instance;
                    }

                    return v;
                }

            default:
                throw Message.DbException.GetInternalError("type=" + aggregateType);
        }
    }

    private H2Sharp.Indexes.Index? GetMinMaxColumnIndex()
    {
        Expression arg = args[0];
        if (arg is ExpressionColumn col)
        {
            Column? column = col.GetColumn();
            TableFilter? filter = col.GetTableFilter();
            if (column is not null && filter is not null)
            {
                return filter.GetTable()?.GetIndexForColumn(column, true, false);
            }
        }

        return null;
    }

    /// <inheritdoc/>
    protected override Value GetAggregatedValue(SessionLocal session, object? aggregateData)
    {
        // Ported from Aggregate.getAggregatedValue: an empty group hands over no data,
        // the aggregate starts fresh and reports its empty-set value (NULL, or 0).
        AggregateData data = (AggregateData?)aggregateData ?? (AggregateData)CreateAggregateData();
        switch (aggregateType)
        {
            case AggregateType.Count:
                if (distinct)
                {
                    return ValueBigint.Get(((AggregateDataCollecting)data).GetCount());
                }

                break;
            case AggregateType.Sum:
                if (distinct)
                {
                    AggregateDataCollecting collected = (AggregateDataCollecting)data;
                    if (collected.GetCount() == 0)
                    {
                        return ValueNull.Instance;
                    }

                    return Collect(session, collected, new AggregateDataDefault(aggregateType, type!));
                }

                break;
            case AggregateType.Avg:
                if (distinct)
                {
                    AggregateDataCollecting collected = (AggregateDataCollecting)data;
                    if (collected.GetCount() == 0)
                    {
                        return ValueNull.Instance;
                    }

                    return Collect(session, collected, new AggregateDataAvg(type!));
                }

                break;
            case AggregateType.StddevPop:
            case AggregateType.StddevSamp:
            case AggregateType.VarPop:
            case AggregateType.VarSamp:
                if (distinct)
                {
                    AggregateDataCollecting collected = (AggregateDataCollecting)data;
                    if (collected.GetCount() == 0)
                    {
                        return ValueNull.Instance;
                    }

                    return Collect(session, collected, new AggregateDataStdVar(aggregateType));
                }

                break;
            default:
                break;
        }

        return data.GetValue(session);
    }

    private static Value Collect(SessionLocal session, AggregateDataCollecting c, AggregateData d)
    {
        foreach (Value v in c)
        {
            d.Add(session, v);
        }

        return d.GetValue(session);
    }

    /// <inheritdoc/>
    protected override void UpdateFromExpressions(SessionLocal session, object aggregateData, Value?[] array)
    {
        AggregateData data = (AggregateData)aggregateData;
        Value? v = args.Length == 0 ? null : array[0];
        UpdateData(session, data, v);
    }

    private void UpdateData(SessionLocal session, AggregateData data, Value? v)
    {
        // COUNT takes the argument as is; the type-specific arms arrive with their types.
        data.Add(session, v);
    }

    /// <inheritdoc/>
    protected override int GetNumExpressions() => args.Length;

    /// <inheritdoc/>
    protected override void RememberExpressions(SessionLocal session, Value?[] array)
    {
        for (int i = 0; i < args.Length; i++)
        {
            array[i] = args[i].GetValue(session);
        }
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        base.Optimize(session);
        if (args.Length == 1)
        {
            type = args[0].GetTypeInfo();
        }

        switch (aggregateType)
        {
            case AggregateType.Count:
                if (args[0].IsConstant())
                {
                    if (args[0].IsNullConstant())
                    {
                        return ValueExpression.Get(ValueBigint.Get(0L));
                    }

                    if (!distinct)
                    {
                        Aggregate rewritten = new(AggregateType.CountAll, [], select, false);
                        if (filterCondition is not null)
                        {
                            rewritten.SetFilterCondition(filterCondition);
                        }

                        return rewritten.Optimize(session);
                    }
                }

                type = TypeInfo.TypeBigint;
                break;
            case AggregateType.CountAll:
                type = TypeInfo.TypeBigint;
                break;
            case AggregateType.Sum:
                type = GetSumType(type!) ?? throw Message.DbException.Get(
                    Api.ErrorCode.SumOrAvgOnWrongDatatype1, GetTraceSql());
                break;
            case AggregateType.Min:
            case AggregateType.Max:
                break;
            case AggregateType.Any:
            case AggregateType.Every:
                type = TypeInfo.TypeBoolean;
                break;
            case AggregateType.Avg:
                type = GetAvgType(type!) ?? throw Message.DbException.Get(
                    Api.ErrorCode.SumOrAvgOnWrongDatatype1, GetTraceSql());
                break;
            case AggregateType.StddevPop:
            case AggregateType.StddevSamp:
            case AggregateType.VarPop:
            case AggregateType.VarSamp:
                type = TypeInfo.TypeDouble;
                break;
            default:
                throw Message.DbException.GetInternalError("type=" + aggregateType);
        }

        return this;
    }

    /// <summary>
    /// The additional result precision in decimal digits for a SUM aggregate function.
    /// </summary>
    private const int AdditionalSumPrecision = 10;

    /// <summary>
    /// The result type of a SUM over the argument type, or null when SUM makes no sense.
    /// </summary>
    /// <param name="type">the argument type</param>
    /// <returns>the result type, or null</returns>
    private static TypeInfo? GetSumType(TypeInfo type)
    {
        switch (type.GetValueType())
        {
            case Value.Boolean:
            case Value.Tinyint:
            case Value.Smallint:
            case Value.Integer:
                return TypeInfo.TypeBigint;
            case Value.Bigint:
                return TypeInfo.GetTypeInfo(
                    Value.Numeric, ValueBigint.DecimalPrecision + AdditionalSumPrecision, -1, null);
            case Value.Numeric:
                return TypeInfo.GetTypeInfo(
                    Value.Numeric, type.GetPrecision() + AdditionalSumPrecision, type.GetDeclaredScale(), null);
            case Value.Real:
                return TypeInfo.TypeDouble;
            case Value.Double:
                return TypeInfo.GetTypeInfo(
                    Value.Decfloat, ValueDouble.DecimalPrecision + AdditionalSumPrecision, -1, null);
            case Value.Decfloat:
                return TypeInfo.GetTypeInfo(
                    Value.Decfloat, type.GetPrecision() + AdditionalSumPrecision, -1, null);
            default:
                if (type.GetValueType() is >= Value.IntervalYear and <= Value.IntervalMinuteToSecond)
                {
                    throw new TODOException("Aggregate.getSumType: SUM over intervals is not ported");
                }

                return null;
        }
    }

    /// <summary>
    /// The additional result scale in decimal digits for an AVG aggregate function.
    /// </summary>
    private const int AdditionalAvgScale = 10;

    /// <summary>
    /// The result type of an AVG over the argument type, or null when AVG makes no sense.
    /// </summary>
    /// <param name="type">the argument type</param>
    /// <returns>the result type, or null</returns>
    private static TypeInfo? GetAvgType(TypeInfo type)
    {
        switch (type.GetValueType())
        {
            case Value.Tinyint:
            case Value.Smallint:
            case Value.Integer:
            case Value.Real:
                return TypeInfo.TypeDouble;
            case Value.Bigint:
                return TypeInfo.GetTypeInfo(
                    Value.Numeric, ValueBigint.DecimalPrecision + AdditionalAvgScale, AdditionalAvgScale, null);
            case Value.Numeric:
                {
                    int additionalScale = Math.Min(
                        ValueNumeric.MaximumScale - type.GetScale(),
                        Math.Min(Engine.Constants.MaxNumericPrecision - (int)type.GetPrecision(), AdditionalAvgScale));
                    return TypeInfo.GetTypeInfo(
                        Value.Numeric, type.GetPrecision() + additionalScale, type.GetScale() + additionalScale, null);
                }

            case Value.Double:
                return TypeInfo.GetTypeInfo(
                    Value.Decfloat, ValueDouble.DecimalPrecision + AdditionalAvgScale, -1, null);
            case Value.Decfloat:
                return TypeInfo.GetTypeInfo(
                    Value.Decfloat, type.GetPrecision() + AdditionalAvgScale, -1, null);
            case Value.IntervalYear:
            case Value.IntervalYearToMonth:
                return TypeInfo.GetTypeInfo(Value.IntervalYearToMonth, type.GetDeclaredPrecision(), 0, null);
            case Value.IntervalMonth:
                return TypeInfo.GetTypeInfo(Value.IntervalMonth, type.GetDeclaredPrecision(), 0, null);
            case Value.IntervalDay:
            case Value.IntervalDayToHour:
            case Value.IntervalDayToMinute:
            case Value.IntervalDayToSecond:
                return TypeInfo.GetTypeInfo(
                    Value.IntervalDayToSecond, type.GetDeclaredPrecision(), ValueInterval.MaximumScale, null);
            case Value.IntervalHour:
            case Value.IntervalHourToMinute:
            case Value.IntervalHourToSecond:
                return TypeInfo.GetTypeInfo(
                    Value.IntervalHourToSecond, type.GetDeclaredPrecision(), ValueInterval.MaximumScale, null);
            case Value.IntervalMinute:
            case Value.IntervalMinuteToSecond:
                return TypeInfo.GetTypeInfo(
                    Value.IntervalMinuteToSecond, type.GetDeclaredPrecision(), ValueInterval.MaximumScale, null);
            case Value.IntervalSecond:
                return TypeInfo.GetTypeInfo(
                    Value.IntervalSecond, type.GetDeclaredPrecision(), ValueInterval.MaximumScale, null);
            default:
                return null;
        }
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        if (!base.IsEverything(visitor))
        {
            return false;
        }

        if (filterCondition is not null && !filterCondition.IsEverything(visitor))
        {
            return false;
        }

        if (visitor.GetVisitorType() == ExpressionVisitor.OptimizableAggregate)
        {
            switch (aggregateType)
            {
                case AggregateType.Count:
                    if (distinct || args[0].GetNullable() != Column.NotNullable)
                    {
                        return false;
                    }

                    goto case AggregateType.CountAll;
                case AggregateType.CountAll:
                    Table? quickTable = visitor.GetTable();
                    return quickTable is not null && quickTable.CanGetRowCount(select.GetSession());
                case AggregateType.Min:
                case AggregateType.Max:
                    return GetMinMaxColumnIndex() is not null;
                default:
                    return false;
            }
        }

        foreach (Expression arg in args)
        {
            if (!arg.IsEverything(visitor))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        int cost = 1;
        foreach (Expression arg in args)
        {
            cost += arg.GetCost();
        }

        return cost;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        if (aggregateType == AggregateType.CountAll)
        {
            return AppendTailConditions(builder.Append("COUNT(*)"), sqlFlags, false);
        }

        string name = aggregateType switch
        {
            AggregateType.Count => "COUNT",
            AggregateType.Sum => "SUM",
            AggregateType.Min => "MIN",
            AggregateType.Max => "MAX",
            AggregateType.Avg => "AVG",
            AggregateType.StddevPop => "STDDEV_POP",
            AggregateType.StddevSamp => "STDDEV_SAMP",
            AggregateType.VarPop => "VAR_POP",
            AggregateType.VarSamp => "VAR_SAMP",
            AggregateType.Any => "ANY",
            AggregateType.Every => "EVERY",
            _ => throw Message.DbException.GetInternalError("type=" + aggregateType),
        };

        _ = builder.Append(distinct ? name + "(DISTINCT " : name + "(");
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            args[i].GetUnenclosedSql(builder, sqlFlags);
        }

        return AppendTailConditions(builder.Append(')'), sqlFlags, false);
    }
}
