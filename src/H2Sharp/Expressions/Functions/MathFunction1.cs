/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.MathFunction1.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A math function with one argument and DOUBLE PRECISION result.
/// </summary>
public sealed class MathFunction1 : Function1
{
    // Trigonometric functions

    /// <summary>
    /// SIN().
    /// </summary>
    public const int Sin = 0;

    /// <summary>
    /// COS().
    /// </summary>
    public const int Cos = Sin + 1;

    /// <summary>
    /// TAN().
    /// </summary>
    public const int Tan = Cos + 1;

    /// <summary>
    /// COT() (non-standard).
    /// </summary>
    public const int Cot = Tan + 1;

    /// <summary>
    /// SINH().
    /// </summary>
    public const int Sinh = Cot + 1;

    /// <summary>
    /// COSH().
    /// </summary>
    public const int Cosh = Sinh + 1;

    /// <summary>
    /// TANH().
    /// </summary>
    public const int Tanh = Cosh + 1;

    /// <summary>
    /// ASIN().
    /// </summary>
    public const int Asin = Tanh + 1;

    /// <summary>
    /// ACOS().
    /// </summary>
    public const int Acos = Asin + 1;

    /// <summary>
    /// ATAN().
    /// </summary>
    public const int Atan = Acos + 1;

    // Logarithm functions

    /// <summary>
    /// LOG10().
    /// </summary>
    public const int Log10 = Atan + 1;

    /// <summary>
    /// LN().
    /// </summary>
    public const int Ln = Log10 + 1;

    // Exponential function

    /// <summary>
    /// EXP().
    /// </summary>
    public const int Exp = Ln + 1;

    // Square root

    /// <summary>
    /// SQRT().
    /// </summary>
    public const int Sqrt = Exp + 1;

    // Other non-standard

    /// <summary>
    /// DEGREES() (non-standard).
    /// </summary>
    public const int Degrees = Sqrt + 1;

    /// <summary>
    /// RADIANS() (non-standard).
    /// </summary>
    public const int Radians = Degrees + 1;

    private static readonly string[] Names = { //
        "SIN", "COS", "TAN", "COT", "SINH", "COSH", "TANH", "ASIN", "ACOS", "ATAN", //
        "LOG10", "LN", "EXP", "SQRT", "DEGREES", "RADIANS" //
    };

    private readonly int function;

    /// <summary>
    /// Creates a new math function.
    /// </summary>
    /// <param name="arg">the argument</param>
    /// <param name="function">one of the constants of this class</param>
    public MathFunction1(Expression arg, int function)
        : base(arg)
    {
        this.function = function;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v = arg.GetValue(session);
        if (v == ValueNull.Instance)
        {
            return ValueNull.Instance;
        }

        double d = v.GetDouble();
        switch (function)
        {
            case Sin:
                d = Math.Sin(d);
                break;
            case Cos:
                d = Math.Cos(d);
                break;
            case Tan:
                d = Math.Tan(d);
                break;
            case Cot:
                d = Math.Tan(d);
                if (d == 0.0)
                {
                    throw DbException.Get(ErrorCode.DivisionByZero1, GetTraceSql());
                }

                d = 1d / d;
                break;
            case Sinh:
                d = Math.Sinh(d);
                break;
            case Cosh:
                d = Math.Cosh(d);
                break;
            case Tanh:
                d = Math.Tanh(d);
                break;
            case Asin:
                if (d < -1d || d > 1d)
                {
                    throw DbException.GetInvalidValueException("ASIN() argument", d);
                }

                d = Math.Asin(d);
                break;
            case Acos:
                if (d < -1d || d > 1d)
                {
                    throw DbException.GetInvalidValueException("ACOS() argument", d);
                }

                d = Math.Acos(d);
                break;
            case Atan:
                d = Math.Atan(d);
                break;
            case Log10:
                if (d <= 0)
                {
                    throw DbException.GetInvalidValueException("LOG10() argument", d);
                }

                d = Math.Log10(d);
                break;
            case Ln:
                if (d <= 0)
                {
                    throw DbException.GetInvalidValueException("LN() argument", d);
                }

                d = Math.Log(d);
                break;
            case Exp:
                d = Math.Exp(d);
                break;
            case Sqrt:
                d = Math.Sqrt(d);
                break;
            case Degrees:
                // The order of the operations is the reference's, which is Java's own
                // Math.toDegrees: multiply first, then divide.
                d = d * 180.0 / Math.PI;
                break;
            case Radians:
                // Java's Math.toRadians divides first, then multiplies.
                d = d / 180.0 * Math.PI;
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        return ValueDouble.Get(d);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        arg = arg.Optimize(session);
        type = TypeInfo.TypeDouble;
        if (arg.IsConstant())
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    /// <inheritdoc/>
    public override string GetName()
    {
        return Names[function];
    }
}
