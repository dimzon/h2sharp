using System;
using System.Collections.Generic;

using H2Sharp.Engine;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

///
/// Data stored while calculating an ANY_VALUE aggregate.
///
internal sealed class AggregateDataAnyValue : AggregateData
{
    private const int MaxValues = 256;

    private readonly List<Value> _values = [];

    private long _filter = -1L;

    internal override void Add(SessionLocal session, Value? v)
    {
        if (v is ValueNull)
        {
            return;
        }

        long filter = _filter;
        if (filter == long.MinValue || (session.GetRandom().NextLong() | filter) == filter)
        {
            _values.Add(v!);
            if (_values.Count == MaxValues)
            {
                Compact(session);
            }
        }
    }

    private void Compact(SessionLocal session)
    {
        _filter <<= 1;
        JavaRandom random = session.GetRandom();
        for (int s = 0, t = 0; t < MaxValues / 2; s += 2, t++)
        {
            int idx = s;
            if (random.NextBoolean())
            {
                idx++;
            }

            _values[t] = _values[idx];
        }

        _values.RemoveRange(MaxValues / 2, MaxValues / 2);
    }

    internal override Value GetValue(SessionLocal session)
    {
        int count = _values.Count;
        if (count == 0)
        {
            return ValueNull.Instance;
        }

        return _values[session.GetRandom().NextInt(count)];
    }
}
