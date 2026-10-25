/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.GCDFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Numerics;

using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// GCD and LCM functions.
/// </summary>
public sealed class GCDFunction : FunctionN
{
    /// <summary>
    /// GCD() (non-standard).
    /// </summary>
    public const int Gcd = 0;

    /// <summary>
    /// LCM() (non-standard).
    /// </summary>
    public const int Lcm = Gcd + 1;

    private static readonly string[] Names = { //
        "GCD", "LCM" //
    };

    /// <summary>
    /// The maximum bit length of a NUMERIC result, as the reference computes it.
    /// </summary>
    public static readonly int MaxBitLength =
        (int)Math.Ceiling(Constants.MaxNumericPrecision / Math.Log10(2));

    private readonly int function;

    /// <summary>
    /// Creates a new function.
    /// </summary>
    /// <param name="function">one of <see cref="Gcd"/>, <see cref="Lcm"/></param>
    public GCDFunction(int function)
        : base(new Expression[2])
    {
        this.function = function;
        type = TypeInfo.TypeNumericScale0;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v1 = args[0].GetValue(session);
        if (v1 == ValueNull.Instance)
        {
            return ValueNull.Instance;
        }

        Value v2 = args[1].GetValue(session);
        if (v2 == ValueNull.Instance)
        {
            return ValueNull.Instance;
        }

        BigInteger a = v1.GetBigInteger(), b = v2.GetBigInteger();
        return function switch
        {
            Gcd => GcdValue(session, a, b),
            Lcm => LcmValue(session, a, b),
            _ => throw DbException.GetInternalError("function=" + function),
        };
    }

    // Java's gcd/lcm become GcdValue/LcmValue: PascalCase Gcd/Lcm would collide with
    // the port's GCD/LCM constants. The algorithm and call order are unchanged.
    private Value GcdValue(SessionLocal session, BigInteger a, BigInteger b)
    {
        a = BigInteger.GreatestCommonDivisor(a, b);
        int count = args.Length;
        if (count > 2)
        {
            bool one = a.IsOne;
            for (int i = 2; i < count; i++)
            {
                Value v = args[i].GetValue(session);
                if (v == ValueNull.Instance)
                {
                    return ValueNull.Instance;
                }

                if (!one)
                {
                    b = v.GetBigInteger();
                    if (b.Sign != 0)
                    {
                        a = BigInteger.GreatestCommonDivisor(a, b);
                        one = a.IsOne;
                    }
                }
            }
        }

        return ValueNumeric.Get(a);
    }

    private Value LcmValue(SessionLocal session, BigInteger a, BigInteger b)
    {
        bool zero = a.Sign == 0 || b.Sign == 0;
        a = zero ? BigInteger.Zero : BigInteger.Abs(a * b) / BigInteger.GreatestCommonDivisor(a, b);
        int count = args.Length;
        if (count > 2)
        {
            bool overflow = !zero && a.GetBitLength() > MaxBitLength;
            for (int i = 2; i < count; i++)
            {
                Value v = args[i].GetValue(session);
                if (v == ValueNull.Instance)
                {
                    return ValueNull.Instance;
                }

                if (!zero)
                {
                    b = v.GetBigInteger();
                    if (b.Sign == 0)
                    {
                        a = BigInteger.Zero;
                        zero = true;
                        overflow = false;
                    }
                    else if (!overflow)
                    {
                        a = BigInteger.Abs(a * b) / BigInteger.GreatestCommonDivisor(a, b);
                        overflow = a.GetBitLength() > MaxBitLength;
                    }
                }
            }

            if (overflow)
            {
                throw DbException.GetValueTooLongException(
                    "NUMERIC", "unknown least common multiple", -1);
            }
        }

        return ValueNumeric.Get(a);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = true, wasNull = false;
        for (int i = 0, l = args.Length; i < l; i++)
        {
            Expression e = args[i].Optimize(session);
            wasNull |= CheckType(e, GetName());
            args[i] = e;
            if (allConst && !e.IsConstant())
            {
                allConst = false;
            }
        }

        if (allConst)
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), TypeInfo.TypeNumericScale0);
        }

        if (wasNull)
        {
            return TypedValueExpression.Get(ValueNull.Instance, TypeInfo.TypeNumericScale0);
        }

        InlineSubexpressions(t => t is GCDFunction f && f.function == function);
        return this;
    }

    /// <summary>
    /// Checks type of GCD, LCM, GCD_AGG, or LCM_AGG argument.
    /// </summary>
    /// <param name="e">the argument</param>
    /// <param name="name">the name of the function</param>
    /// <returns>true when the argument has the NULL data type, false otherwise</returns>
    public static bool CheckType(Expression e, string name)
    {
        TypeInfo argType = e.GetTypeInfo();
        switch (argType.GetValueType())
        {
            case Value.Null:
                return true;
            case Value.Tinyint:
            case Value.Smallint:
            case Value.Integer:
            case Value.Bigint:
                break;
            case Value.Numeric:
                if (argType.GetScale() == 0)
                {
                    break;
                }

                goto default;
            default:
                throw DbException.GetInvalidValueException(name + " argument", e.GetTraceSql());
        }

        return false;
    }

    /// <inheritdoc/>
    public override string GetName() => Names[function];
}
