using System.Numerics;

using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

///
/// Data stored while calculating an AVG aggregate.
///
internal sealed class AggregateDataAvg(TypeInfo dataType) : AggregateData
{
    private readonly TypeInfo _dataType = dataType;

    private long _count;

    private double _doubleValue;

    private BigDecimal? _decimalValue;

    private BigInteger? _integerValue;

    internal override void Add(SessionLocal session, Value? v)
    {
        if (v is ValueNull)
        {
            return;
        }

        _count++;
        switch (_dataType.GetValueType())
        {
            case Value.Double:
                _doubleValue += v!.GetDouble();
                break;
            case Value.Numeric:
            case Value.Decfloat:
                {
                    // Ported from AggregateDataAvg.add: NUMERIC and DECFLOAT results
                    // accumulate in BigDecimal.
                    BigDecimal bd = v!.GetBigDecimal();
                    _decimalValue = _decimalValue is null ? bd : _decimalValue.Add(bd);
                    break;
                }

            default:
                // Interval results accumulate in absolute BigInteger units. IntervalUtils
                // ports both directions, so the full branch comes along.
                BigInteger bi = IntervalUtils.IntervalToAbsolute((ValueInterval)v!);
                _integerValue = _integerValue is null ? bi : _integerValue.Value + bi;
                break;
        }
    }

    internal override Value GetValue(SessionLocal session)
    {
        if (_count == 0)
        {
            return ValueNull.Instance;
        }

        Value v;
        switch (_dataType.GetValueType())
        {
            case Value.Double:
                v = ValueDouble.Get(_doubleValue / _count);
                break;
            case Value.Numeric:
                v = ValueNumeric.Get(
                    _decimalValue!.Divide(
                        BigDecimal.FromLong(_count), _dataType.GetScale(), RoundingMode.HalfDown));
                break;
            case Value.Decfloat:
                v = ValueDecfloat.Divide(_decimalValue!, BigDecimal.FromLong(_count), _dataType);
                break;
            default:
                v = IntervalUtils.IntervalFromAbsolute(
                    IntervalQualifiers.ValueOf(_dataType.GetValueType() - Value.IntervalYear),
                    _integerValue!.Value / new BigInteger(_count));
                break;
        }

        return v.CastToValue(_dataType, session.CastDataProvider);
    }
}
