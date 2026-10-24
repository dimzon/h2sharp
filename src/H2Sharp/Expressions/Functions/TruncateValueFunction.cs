/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.TruncateValueFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Numerics;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A TRUNCATE_VALUE function.
/// </summary>
public sealed class TruncateValueFunction : FunctionN
{
    /// <summary>
    /// Creates a TRUNCATE_VALUE function.
    /// </summary>
    /// <param name="arg1">the value</param>
    /// <param name="arg2">the precision</param>
    /// <param name="arg3">whether to force the conversion</param>
    public TruncateValueFunction(Expression arg1, Expression arg2, Expression arg3)
        : base([arg1, arg2, arg3])
    {
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value? v1, Value? v2, Value? v3)
    {
        long precision = v2!.GetLong();
        bool force = v3!.GetBoolean();
        if (precision <= 0)
        {
            throw DbException.Get(
                Api.ErrorCode.InvalidValuePrecision,
                precision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "1",
                int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        int requested = precision > int.MaxValue ? int.MaxValue : (int)precision;
        TypeInfo type = v1!.GetTypeInfo();
        int valueType = type.GetValueType();
        if (SupportsPrecision(valueType))
        {
            if (precision < type.GetPrecision())
            {
                switch (valueType)
                {
                    case Value.Numeric:
                    {
                        BigDecimal rounded = RoundToPrecision(v1.GetBigDecimal(), requested);
                        if (rounded.Scale < 0)
                        {
                            rounded = rounded.SetScale(0, RoundingMode.Unnecessary);
                        }

                        return ValueNumeric.Get(rounded);
                    }

                    case Value.Decfloat:
                        return ValueDecfloat.Get(RoundToPrecision(v1.GetBigDecimal(), requested));
                    default:
                        return v1.CastToValue(
                            TypeInfo.GetTypeInfo(valueType, precision, type.GetScale(), type.GetExtTypeInfo()),
                            session.CastDataProvider);
                }
            }
        }
        else if (force)
        {
            BigDecimal value;
            switch (valueType)
            {
                case Value.Tinyint:
                case Value.Smallint:
                case Value.Integer:
                    value = new BigDecimal(new BigInteger(v1.GetInt()), 0);
                    break;
                case Value.Bigint:
                    value = new BigDecimal(new BigInteger(v1.GetLong()), 0);
                    break;
                case Value.Real:
                case Value.Double:
                    value = v1.GetBigDecimal();
                    break;
                default:
                    return v1;
            }

            value = RoundToPrecision(value, requested);
            if (valueType == Value.Decfloat)
            {
                return ValueDecfloat.Get(value);
            }

            if (value.Scale < 0)
            {
                value = value.SetScale(0, RoundingMode.Unnecessary);
            }

            return ValueNumeric.Get(value).ConvertToType(TypeInfo.GetTypeInfo(valueType));
        }

        return v1;
    }

    /// <summary>
    /// Whether the type carries a precision the function can truncate to. The reference reads
    /// this off its type table; NUMERIC, DECFLOAT and ARRAY are the ones that do.
    /// </summary>
    private static bool SupportsPrecision(int valueType)
        => valueType is Value.Numeric or Value.Decfloat or Value.Array;

    /// <summary>
    /// Rounds to so many significant digits, HALF_UP, as MathContext does.
    /// </summary>
    private static BigDecimal RoundToPrecision(BigDecimal value, int precision)
    {
        if (value.Signum() == 0)
        {
            return value;
        }

        // The adjusted exponent is digits-before-the-point minus one; the scale that keeps
        // exactly precision significant digits follows from it.
        int scale = precision - 1 - (value.Precision() - value.Scale - 1);
        return scale >= value.Scale ? value : value.SetScale(scale, RoundingMode.HalfUp);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = OptimizeArguments(session, allConst: true);
        type = args[0].GetTypeInfo();
        if (allConst)
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    /// <inheritdoc/>
    public override string GetName() => "TRUNCATE_VALUE";
}
