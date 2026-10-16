/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.aggregate.AggregateDataDefault.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sliced to the aggregates the engine reads: SUM, MIN and MAX. The other arms
 * refuse where the reference would branch to them, so the next aggregate is new
 * branches, not a new shape.
 */
using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// Data stored while calculating an aggregate.
/// </summary>
internal sealed class AggregateDataDefault : AggregateData
{
    private readonly AggregateType aggregateType;
    private readonly TypeInfo dataType;
    private Value? value;

    /// <summary>
    /// Aggregation state for the computed result type.
    /// </summary>
    /// <param name="aggregateType">the type of the aggregate operation</param>
    /// <param name="dataType">the data type of the computed result</param>
    internal AggregateDataDefault(AggregateType aggregateType, TypeInfo dataType)
    {
        this.aggregateType = aggregateType;
        this.dataType = dataType;
    }

    /// <inheritdoc/>
    internal override void Add(SessionLocal session, Value? v)
    {
        if (v is ValueNull)
        {
            return;
        }

        switch (aggregateType)
        {
            case AggregateType.Sum:
                if (value is null)
                {
                    value = v!.ConvertToType(dataType);
                }
                else
                {
                    v = v!.ConvertToType(value.GetTypeInfo());
                    value = value.Add(v);
                }

                break;
            case AggregateType.Min:
                if (value is null || session.Compare(v!, value) < 0)
                {
                    value = v;
                }

                break;
            case AggregateType.Max:
                if (value is null || session.Compare(v!, value) > 0)
                {
                    value = v;
                }

                break;
            default:
                throw Message.DbException.GetInternalError("type=" + aggregateType);
        }
    }

    /// <inheritdoc/>
    internal override Value GetValue(SessionLocal session)
    {
        Value? v = value;
        if (v is null)
        {
            return ValueNull.Instance;
        }

        return v.ConvertToType(dataType);
    }
}
