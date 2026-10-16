using System;

using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

///
/// Data stored while calculating a STDDEV_POP, STDDEV_SAMP, VAR_SAMP, VAR_POP,
/// REGR_SXX, or REGR_SYY aggregate.
///
internal sealed class AggregateDataStdVar(AggregateType aggregateType) : AggregateData
{
    private readonly AggregateType _aggregateType = aggregateType;

    private long _count;

    private double _m2;

    private double _mean;

    internal override void Add(SessionLocal session, Value? v)
    {
        if (v is ValueNull)
        {
            return;
        }

        // Using Welford's method, see also
        // https://en.wikipedia.org/wiki/Algorithms_for_calculating_variance
        // https://www.johndcook.com/standard_deviation.html
        double x = v!.GetDouble();
        if (++_count == 1)
        {
            _mean = x;
            _m2 = 0;
        }
        else
        {
            double delta = x - _mean;
            _mean += delta / _count;
            _m2 += delta * (x - _mean);
        }
    }

    internal override Value GetValue(SessionLocal session)
    {
        double v;
        switch (_aggregateType)
        {
            case AggregateType.StddevSamp:
            case AggregateType.VarSamp:
                if (_count < 2)
                {
                    return ValueNull.Instance;
                }

                v = _m2 / (_count - 1);
                if (_aggregateType == AggregateType.StddevSamp)
                {
                    v = Math.Sqrt(v);
                }

                break;
            case AggregateType.StddevPop:
            case AggregateType.VarPop:
                if (_count < 1)
                {
                    return ValueNull.Instance;
                }

                v = _m2 / _count;
                if (_aggregateType == AggregateType.StddevPop)
                {
                    v = Math.Sqrt(v);
                }

                break;
            case AggregateType.RegrSxx:
            case AggregateType.RegrSyy:
                if (_count < 1)
                {
                    return ValueNull.Instance;
                }

                v = _m2;
                break;
            default:
                throw Message.DbException.GetInternalError("type=" + _aggregateType);
        }

        return ValueDouble.Get(v);
    }
}
