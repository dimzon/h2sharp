/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.MathFunction2.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A math function with two arguments and DOUBLE PRECISION result.
/// </summary>
public sealed class MathFunction2 : Function2
{
    /// <summary>
    /// ATAN2() (non-standard).
    /// </summary>
    public const int Atan2 = 0;

    /// <summary>
    /// LOG().
    /// </summary>
    public const int Log = Atan2 + 1;

    /// <summary>
    /// POWER().
    /// </summary>
    public const int Power = Log + 1;

    private static readonly string[] Names = { //
        "ATAN2", "LOG", "POWER" //
    };

    private readonly int function;

    /// <summary>
    /// Creates a new math function.
    /// </summary>
    /// <param name="arg1">the first argument</param>
    /// <param name="arg2">the second argument</param>
    /// <param name="function">one of <see cref="Atan2"/>, <see cref="Log"/>, <see cref="Power"/></param>
    public MathFunction2(Expression arg1, Expression arg2, int function)
        : base(arg1, arg2)
    {
        this.function = function;
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value v1, Value v2)
    {
        double d1 = v1.GetDouble(), d2 = v2.GetDouble();
        switch (function)
        {
            case Atan2:
                d1 = Math.Atan2(d1, d2);
                break;
            case Log:
                if (session.GetMode().SwapLogFunctionParameters)
                {
                    double t = d2;
                    d2 = d1;
                    d1 = t;
                }

                if (d2 <= 0)
                {
                    throw DbException.GetInvalidValueException("LOG() argument", d2);
                }

                if (d1 <= 0 || d1 == 1)
                {
                    throw DbException.GetInvalidValueException("LOG() base", d1);
                }

                if (d1 == Math.E)
                {
                    d1 = Math.Log(d2);
                }
                else if (d1 == 10d)
                {
                    d1 = Math.Log10(d2);
                }
                else
                {
                    d1 = Math.Log(d2) / Math.Log(d1);
                }

                break;
            case Power:
                d1 = Math.Pow(d1, d2);
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        return ValueDouble.Get(d1);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        right = right.Optimize(session);
        type = TypeInfo.TypeDouble;
        if (left.IsConstant() && right.IsConstant())
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
