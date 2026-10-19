/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.MathFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The TRUNC() of a TIMESTAMP refuses with a TODOException: the reference returns a
 * DateTimeFunction(DATE_TRUNC, DAY, ...), and DateTimeFunction is not ported yet. TRUNC() of a
 * DATE, which only casts to TIMESTAMP, is ported.
 */
using System;
using System.Globalization;
using System.Text;

using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A math function.
/// </summary>
public sealed class MathFunction : Function1_2
{
    /// <summary>
    /// ABS().
    /// </summary>
    public const int Abs = 0;

    /// <summary>
    /// MOD().
    /// </summary>
    public const int Mod = Abs + 1;

    /// <summary>
    /// FLOOR().
    /// </summary>
    public const int Floor = Mod + 1;

    /// <summary>
    /// CEIL() or CEILING().
    /// </summary>
    public const int Ceil = Floor + 1;

    /// <summary>
    /// ROUND() (non-standard)
    /// </summary>
    public const int Round = Ceil + 1;

    /// <summary>
    /// ROUNDMAGIC() (non-standard)
    /// </summary>
    public const int Roundmagic = Round + 1;

    /// <summary>
    /// SIGN() (non-standard)
    /// </summary>
    public const int Sign = Roundmagic + 1;

    /// <summary>
    /// TRUNC() (non-standard)
    /// </summary>
    public const int Trunc = Sign + 1;

    private static readonly string[] Names = { //
        "ABS", "MOD", "FLOOR", "CEIL", "ROUND", "ROUNDMAGIC", "SIGN", "TRUNC" //
    };

    private readonly int function;

    private TypeInfo? commonType;

    /// <summary>
    /// Creates a new math function.
    /// </summary>
    /// <param name="arg1">the first argument</param>
    /// <param name="arg2">the second argument, or null</param>
    /// <param name="function">one of the constants of this class</param>
    public MathFunction(Expression arg1, Expression? arg2, int function)
        : base(arg1, arg2)
    {
        this.function = function;
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value v1, Value? v2)
    {
        switch (function)
        {
            case Abs:
                if (v1.GetSignum() < 0)
                {
                    v1 = v1.Negate();
                }

                break;
            case Mod:
                v1 = v1.ConvertToType(commonType!).Modulus(v2!.ConvertToType(commonType!)).ConvertToType(type!);
                break;
            case Floor:
                v1 = RoundValue(v1, v2, RoundingMode.Floor);
                break;
            case Ceil:
                v1 = RoundValue(v1, v2, RoundingMode.Ceiling);
                break;
            case Round:
                v1 = RoundValue(v1, v2, RoundingMode.HalfUp);
                break;
            case Roundmagic:
                v1 = ValueDouble.Get(RoundMagic(v1.GetDouble()));
                break;
            case Sign:
                v1 = ValueInteger.Get(v1.GetSignum());
                break;
            case Trunc:
                v1 = RoundValue(v1, v2, RoundingMode.Down);
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        return v1;
    }

    /// <summary>
    /// Rounds a value to the given scale. Named <c>RoundValue</c> and not <c>Round</c> because
    /// this class already carries a constant called <c>Round</c>, the function code; C# will not
    /// let one class hold a constant and a method under one name, and the constant keeps the
    /// reference's name.
    /// </summary>
    /// <param name="v1">the value to round</param>
    /// <param name="v2">the scale, or null</param>
    /// <param name="roundingMode">how to round</param>
    /// <returns>the rounded value</returns>
    private Value RoundValue(Value v1, Value? v2, RoundingMode roundingMode)
    {
        int scale = v2 != null ? CheckScale(v2) : 0;
        int t = type!.GetValueType();
        switch (t)
        {
            case Value.Tinyint:
            case Value.Smallint:
            case Value.Integer:
            case Value.Bigint:
                if (scale < 0)
                {
                    long original = v1.GetLong();
                    long scaled = scale < -18
                        ? 0L
                        : Value.ConvertToLongChecked(BigDecimal.FromLong(original).SetScale(scale, roundingMode), null);
                    if (original != scaled)
                    {
                        v1 = ValueBigint.Get(scaled).ConvertToType(type!);
                    }
                }

                break;
            case Value.Numeric:
                {
                    int targetScale = type!.GetScale();
                    BigDecimal bd = v1.GetBigDecimal();
                    if (scale < targetScale)
                    {
                        bd = bd.SetScale(scale, roundingMode);
                    }

                    v1 = ValueNumeric.Get(bd.SetScale(targetScale, roundingMode));
                    break;
                }

            case Value.Real:
            case Value.Double:
                {
                    // The reference rounds in double precision when scale is 0 and the mode has an
                    // exact double answer, and through a decimal for every other mode; its labelled
                    // break out of the block is a flag here.
                    bool throughDecimal = scale != 0;
                    double d = 0;
                    if (!throughDecimal)
                    {
                        switch (roundingMode)
                        {
                            case RoundingMode.Down:
                                d = v1.GetDouble();
                                d = d < 0 ? Math.Ceiling(d) : Math.Floor(d);
                                break;
                            case RoundingMode.Ceiling:
                                d = Math.Ceiling(v1.GetDouble());
                                break;
                            case RoundingMode.Floor:
                                d = Math.Floor(v1.GetDouble());
                                break;
                            default:
                                throughDecimal = true;
                                break;
                        }
                    }

                    if (throughDecimal)
                    {
                        BigDecimal bd = v1.GetBigDecimal().SetScale(scale, roundingMode);
                        v1 = t == Value.Real
                            ? ValueReal.Get((float)bd.ToDouble())
                            : ValueDouble.Get(bd.ToDouble());
                    }
                    else
                    {
                        v1 = t == Value.Real ? ValueReal.Get((float)d) : ValueDouble.Get(d);
                    }

                    break;
                }

            case Value.Decfloat:
                v1 = ValueDecfloat.Get(v1.GetBigDecimal().SetScale(scale, roundingMode));
                break;
        }

        return v1;
    }

    private static double RoundMagic(double d)
    {
        if (d < 0.000_000_000_000_1 && d > -0.000_000_000_000_1)
        {
            return 0.0;
        }

        if (d > 1_000_000_000_000d || d < -1_000_000_000_000d)
        {
            return d;
        }

        StringBuilder s = new();
        s.Append(JavaNumberFormat.DoubleToString(d));
        if (s.ToString().IndexOf('E') >= 0)
        {
            return d;
        }

        int len = s.Length;
        if (len < 16)
        {
            return d;
        }

        if (s.ToString().IndexOf('.') > len - 3)
        {
            return d;
        }

        s.Remove(len - 2, 2);
        len -= 2;
        char c1 = s[len - 2];
        char c2 = s[len - 3];
        char c3 = s[len - 4];
        if (c1 == '0' && c2 == '0' && c3 == '0')
        {
            s[len - 1] = '0';
        }
        else if (c1 == '9' && c2 == '9' && c3 == '9')
        {
            s[len - 1] = '9';
            s.Append('9');
            s.Append('9');
            s.Append('9');
        }

        return double.Parse(s.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        if (right != null)
        {
            right = right.Optimize(session);
        }

        switch (function)
        {
            case Abs:
                type = left.GetTypeInfo();
                if (type.GetValueType() == Value.Null)
                {
                    type = TypeInfo.TypeNumericFloatingPoint;
                }

                break;
            case Floor:
            case Ceil:
                {
                    Expression? e = OptimizeRound(0, true, false, true);
                    if (e != null)
                    {
                        return e;
                    }

                    break;
                }

            case Mod:
                TypeInfo divisorType = right!.GetTypeInfo();
                commonType = TypeInfo.GetHigherType(left.GetTypeInfo(), divisorType);
                int valueType = commonType.GetValueType();
                if (valueType == Value.Null)
                {
                    commonType = TypeInfo.TypeBigint;
                }
                else if (!DataType.IsNumericType(valueType))
                {
                    throw MvStore.Db.Store.GetInvalidExpressionTypeException(
                        "MOD argument",
                        DataType.IsNumericType(left.GetTypeInfo().GetValueType()) ? right : left);
                }

                type = DataType.IsNumericType(divisorType.GetValueType()) ? divisorType : commonType;
                break;
            case Round:
                {
                    Expression? e = OptimizeRoundWithScale(session, true);
                    if (e != null)
                    {
                        return e;
                    }

                    break;
                }

            case Roundmagic:
                type = TypeInfo.TypeDouble;
                break;
            case Sign:
                type = TypeInfo.TypeInteger;
                break;
            case Trunc:
                switch (left.GetTypeInfo().GetValueType())
                {
                    case Value.Varchar:
                        left = new CastSpecification(left, TypeInfo.GetTypeInfo(Value.Timestamp, -1L, 0, null))
                            .Optimize(session);
                        goto case Value.Timestamp;
                    case Value.Timestamp:
                    case Value.TimestampTz:
                        if (right != null)
                        {
                            throw DbException.Get(ErrorCode.InvalidParameterCount2, "TRUNC", "1");
                        }

                        throw new TODOException(
                            "MathFunction.optimize: TRUNC of a datetime needs DateTimeFunction (DATE_TRUNC), which is not ported yet");
                    case Value.Date:
                        if (right != null)
                        {
                            throw DbException.Get(ErrorCode.InvalidParameterCount2, "TRUNC", "1");
                        }

                        return new CastSpecification(left, TypeInfo.GetTypeInfo(Value.Timestamp, -1L, 0, null))
                            .Optimize(session);
                    default:
                        {
                            Expression? e = OptimizeRoundWithScale(session, false);
                            if (e != null)
                            {
                                return e;
                            }
                        }

                        break;
                }

                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        if (left.IsConstant() && (right == null || right.IsConstant()))
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type!);
        }

        return this;
    }

    private Expression? OptimizeRoundWithScale(SessionLocal session, bool possibleRoundUp)
    {
        int scale;
        bool scaleIsKnown = false, scaleIsNull = false;
        if (right != null)
        {
            if (right.IsConstant())
            {
                Value scaleValue = right.GetValue(session);
                scaleIsKnown = true;
                if (scaleValue != ValueNull.Instance)
                {
                    scale = CheckScale(scaleValue);
                }
                else
                {
                    scale = -1;
                    scaleIsNull = true;
                }
            }
            else
            {
                scale = -1;
            }
        }
        else
        {
            scale = 0;
            scaleIsKnown = true;
        }

        return OptimizeRound(scale, scaleIsKnown, scaleIsNull, possibleRoundUp);
    }

    private static int CheckScale(Value v)
    {
        int scale = v.GetInt();
        if (scale < -ValueNumeric.MaximumScale || scale > ValueNumeric.MaximumScale)
        {
            throw DbException.GetInvalidValueException("scale", scale);
        }

        return scale;
    }

    /// <summary>
    /// Optimizes rounding and truncation functions.
    /// </summary>
    /// <param name="scale">the scale, if known</param>
    /// <param name="scaleIsKnown">whether scale is known</param>
    /// <param name="scaleIsNull">whether scale is NULL</param>
    /// <param name="possibleRoundUp">
    /// true if result of rounding can have larger precision than precision of argument, false
    /// otherwise
    /// </param>
    /// <returns>the optimized expression or null if this function should be used</returns>
    private Expression? OptimizeRound(int scale, bool scaleIsKnown, bool scaleIsNull, bool possibleRoundUp)
    {
        TypeInfo leftType = left.GetTypeInfo();
        switch (leftType.GetValueType())
        {
            case Value.Null:
                type = TypeInfo.TypeNumericScale0;
                break;
            case Value.Tinyint:
            case Value.Smallint:
            case Value.Integer:
            case Value.Bigint:
                if (scaleIsKnown && scale >= 0)
                {
                    return left;
                }

                type = leftType;
                break;
            case Value.Real:
            case Value.Double:
            case Value.Decfloat:
                type = leftType;
                break;
            case Value.Numeric:
                {
                    long precision;
                    int originalScale = leftType.GetScale();
                    if (scaleIsKnown)
                    {
                        if (originalScale <= scale)
                        {
                            return left;
                        }

                        if (scale < 0)
                        {
                            scale = 0;
                        }
                        else if (scale > ValueNumeric.MaximumScale)
                        {
                            scale = ValueNumeric.MaximumScale;
                        }

                        precision = leftType.GetPrecision() - originalScale + scale;
                        if (possibleRoundUp)
                        {
                            precision++;
                        }
                    }
                    else
                    {
                        precision = leftType.GetPrecision();
                        if (possibleRoundUp)
                        {
                            precision++;
                        }

                        scale = originalScale;
                    }

                    type = TypeInfo.GetTypeInfo(Value.Numeric, precision, scale, null);
                    break;
                }

            default:
                throw MvStore.Db.Store.GetInvalidExpressionTypeException(GetName() + " argument", left);
        }

        if (scaleIsNull)
        {
            return TypedValueExpression.Get(ValueNull.Instance, type!);
        }

        return null;
    }

    /// <inheritdoc/>
    public override string GetName()
    {
        return Names[function];
    }
}
