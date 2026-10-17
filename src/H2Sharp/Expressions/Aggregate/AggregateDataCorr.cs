/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.aggregate.AggregateDataCorr.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// Data stored while calculating a CORR, REGR_SLOPE, REGR_INTERCEPT, or REGR_R2 aggregate.
/// </summary>
internal sealed class AggregateDataCorr(AggregateType aggregateType) : AggregateDataBinarySet
{
    private readonly AggregateType _aggregateType = aggregateType;

    private long _count;

    private double _sumY;

    private double _sumX;

    private double _sumYX;

    private double _m2y;

    private double _meanY;

    private double _m2x;

    private double _meanX;

    internal override void Add(SessionLocal session, Value? yValue, Value? xValue)
    {
        double y = yValue!.GetDouble();
        double x = xValue!.GetDouble();
        _sumY += y;
        _sumX += x;
        _sumYX += y * x;
        if (++_count == 1)
        {
            _meanY = y;
            _meanX = x;
            _m2x = _m2y = 0;
        }
        else
        {
            double delta = y - _meanY;
            _meanY += delta / _count;
            _m2y += delta * (y - _meanY);
            delta = x - _meanX;
            _meanX += delta / _count;
            _m2x += delta * (x - _meanX);
        }
    }

    internal override Value GetValue(SessionLocal session)
    {
        if (_count < 1)
        {
            return ValueNull.Instance;
        }

        double v;
        switch (_aggregateType)
        {
            case AggregateType.Corr:
                if (_m2y == 0 || _m2x == 0)
                {
                    return ValueNull.Instance;
                }

                v = (_sumYX - _sumX * _sumY / _count) / Math.Sqrt(_m2y * _m2x);
                break;
            case AggregateType.RegrSlope:
                if (_m2x == 0)
                {
                    return ValueNull.Instance;
                }

                v = (_sumYX - _sumX * _sumY / _count) / _m2x;
                break;
            case AggregateType.RegrIntercept:
                if (_m2x == 0)
                {
                    return ValueNull.Instance;
                }

                v = _meanY - (_sumYX - _sumX * _sumY / _count) / _m2x * _meanX;
                break;
            case AggregateType.RegrR2:
                if (_m2x == 0)
                {
                    return ValueNull.Instance;
                }

                if (_m2y == 0)
                {
                    return ValueDouble.One;
                }

                v = _sumYX - _sumX * _sumY / _count;
                v = v * v / (_m2y * _m2x);
                break;
            default:
                throw Message.DbException.GetInternalError("type=" + _aggregateType);
        }

        return ValueDouble.Get(v);
    }
}
