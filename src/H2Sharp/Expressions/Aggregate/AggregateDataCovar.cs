/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.aggregate.AggregateDataCovar.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// Data stored while calculating a COVAR_POP, COVAR_SAMP, or REGR_SXY aggregate.
/// </summary>
internal sealed class AggregateDataCovar(AggregateType aggregateType) : AggregateDataBinarySet
{
    private readonly AggregateType _aggregateType = aggregateType;

    private long _count;

    private double _sumY;

    private double _sumX;

    private double _sumYX;

    internal override void Add(SessionLocal session, Value? yValue, Value? xValue)
    {
        double y = yValue!.GetDouble();
        double x = xValue!.GetDouble();
        _sumY += y;
        _sumX += x;
        _sumYX += y * x;
        _count++;
    }

    internal override Value GetValue(SessionLocal session)
    {
        double v;
        switch (_aggregateType)
        {
            case AggregateType.CovarPop:
                if (_count < 1)
                {
                    return ValueNull.Instance;
                }

                v = (_sumYX - _sumX * _sumY / _count) / _count;
                break;
            case AggregateType.CovarSamp:
                if (_count < 2)
                {
                    return ValueNull.Instance;
                }

                v = (_sumYX - _sumX * _sumY / _count) / (_count - 1);
                break;
            case AggregateType.RegrSxy:
                if (_count < 1)
                {
                    return ValueNull.Instance;
                }

                v = _sumYX - _sumX * _sumY / _count;
                break;
            default:
                throw Message.DbException.GetInternalError("type=" + _aggregateType);
        }

        return ValueDouble.Get(v);
    }
}
