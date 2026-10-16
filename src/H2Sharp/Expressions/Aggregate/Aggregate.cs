/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.aggregate.Aggregate.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sliced to COUNT: COUNT(*) and COUNT(expression) walk the whole road — reading,
 * mapping, the quick aggregate shortcut and the grouped accumulation. Every other
 * aggregate type refuses where the reference would branch to it (the data factory,
 * the value reader and the type switch), so adding the next one is new branches,
 * not a new shape. WITHIN GROUP / ORDER BY lists are not kept: no reader builds
 * them yet, and a field nothing writes is a promise nothing checks.
 */
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
            case AggregateType.Sum:
            case AggregateType.Min:
            case AggregateType.Max:
                if (!distinct)
                {
                    return aggregateType == AggregateType.Count
                        ? new AggregateDataCount(false)
                        : new AggregateDataDefault(aggregateType, type!);
                }

                throw new TODOException("Aggregate.createAggregateData: DISTINCT aggregates need collecting");
            default:
                throw new TODOException(
                    "Aggregate.createAggregateData: only COUNT, SUM, MIN and MAX are ported (type=" + aggregateType + ")");
        }
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
            default:
                throw Message.DbException.GetInternalError("type=" + aggregateType);
        }
    }

    /// <inheritdoc/>
    protected override Value GetAggregatedValue(SessionLocal session, object? aggregateData)
    {
        if (distinct)
        {
            throw new TODOException("Aggregate.getAggregatedValue: DISTINCT aggregates need collecting");
        }

        return ((AggregateData)aggregateData!).GetValue(session);
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
