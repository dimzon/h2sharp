/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.function.BitFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sliced to the bitwise operations the bit aggregates call: the operation codes
 * BITAND..BITXNOR, getBitwise and its two branches, and checkArgType. The rest of
 * the reference class — the Function subclass with left/right, optimize, getValue
 * and getSQL — is not ported: no reader builds a BITAND() expression, so that half
 * has no caller. checkArgType's refusal is the reference's, including the UNKNOWN
 * branch from Store.getInvalidExpressionTypeException, which is inlined here
 * because Store itself is not ported yet.
 */
using System;

using H2Sharp.Api;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// The bitwise functions BITAND, BITOR, BITXOR, BITNOT, BITNAND, BITNOR and BITXNOR.
/// </summary>
public static class BitFunction
{
    /// <summary>
    /// BITAND() (non-standard).
    /// </summary>
    public const int Bitand = 0;

    /// <summary>
    /// BITOR() (non-standard).
    /// </summary>
    public const int Bitor = Bitand + 1;

    /// <summary>
    /// BITXOR() (non-standard).
    /// </summary>
    public const int Bitxor = Bitor + 1;

    /// <summary>
    /// BITNOT() (non-standard).
    /// </summary>
    public const int Bitnot = Bitxor + 1;

    /// <summary>
    /// BITNAND() (non-standard).
    /// </summary>
    public const int Bitnand = Bitnot + 1;

    /// <summary>
    /// BITNOR() (non-standard).
    /// </summary>
    public const int Bitnor = Bitnand + 1;

    /// <summary>
    /// BITXNOR() (non-standard).
    /// </summary>
    public const int Bitxnor = Bitnor + 1;

    /// <summary>
    /// Computes the value of a bitwise function.
    /// </summary>
    /// <param name="function">one of <see cref="Bitand"/>, <see cref="Bitor"/>, <see cref="Bitxor"/>, <see cref="Bitnot"/>, <see cref="Bitnand"/>, <see cref="Bitnor"/>, <see cref="Bitxnor"/></param>
    /// <param name="type">the type of result</param>
    /// <param name="v1">the value of first argument</param>
    /// <param name="v2">the value of second argument, or null</param>
    /// <returns>the resulting value</returns>
    public static Value GetBitwise(int function, TypeInfo type, Value? v1, Value? v2)
        => type.GetValueType() < Value.Tinyint
            ? GetBinaryString(function, type, v1!, v2)
            : GetNumeric(function, type, v1!, v2);

    private static Value GetBinaryString(int function, TypeInfo type, Value v1, Value? v2)
    {
        byte[] bytes;
        if (function == Bitnot)
        {
            bytes = v1.GetBytes()!;
            for (int i = 0, l = bytes.Length; i < l; i++)
            {
                bytes[i] = (byte)~bytes[i];
            }
        }
        else
        {
            byte[] bytes1 = v1.GetBytesNoCopy()!;
            byte[] bytes2 = v2!.GetBytesNoCopy()!;
            int length1 = bytes1.Length;
            int length2 = bytes2.Length;
            int min;
            int max;
            if (length1 <= length2)
            {
                min = length1;
                max = length2;
            }
            else
            {
                min = length2;
                max = length1;
                (bytes1, bytes2) = (bytes2, bytes1);
            }

            long limit = type.GetPrecision();
            if (min > limit)
            {
                max = min = (int)limit;
            }
            else if (max > limit)
            {
                max = (int)limit;
            }

            bytes = new byte[max];
            int i = 0;
            switch (function)
            {
                case Bitand:
                    for (; i < min; i++)
                    {
                        bytes[i] = (byte)(bytes1[i] & bytes2[i]);
                    }

                    break;
                case Bitor:
                    for (; i < min; i++)
                    {
                        bytes[i] = (byte)(bytes1[i] | bytes2[i]);
                    }

                    Array.Copy(bytes2, i, bytes, i, max - i);
                    break;
                case Bitxor:
                    for (; i < min; i++)
                    {
                        bytes[i] = (byte)(bytes1[i] ^ bytes2[i]);
                    }

                    Array.Copy(bytes2, i, bytes, i, max - i);
                    break;
                case Bitnand:
                    for (; i < min; i++)
                    {
                        bytes[i] = (byte)~(bytes1[i] & bytes2[i]);
                    }

                    Array.Fill(bytes, unchecked((byte)-1), i, max - i);
                    break;
                case Bitnor:
                    for (; i < min; i++)
                    {
                        bytes[i] = (byte)~(bytes1[i] | bytes2[i]);
                    }

                    for (; i < max; i++)
                    {
                        bytes[i] = (byte)~bytes2[i];
                    }

                    break;
                case Bitxnor:
                    for (; i < min; i++)
                    {
                        bytes[i] = (byte)~(bytes1[i] ^ bytes2[i]);
                    }

                    for (; i < max; i++)
                    {
                        bytes[i] = (byte)~bytes2[i];
                    }

                    break;
                default:
                    throw DbException.GetInternalError("function=" + function);
            }
        }

        return type.GetValueType() == Value.Binary
            ? ValueBinary.GetNoCopy(bytes)
            : ValueVarbinary.GetNoCopy(bytes);
    }

    private static Value GetNumeric(int function, TypeInfo type, Value v1, Value? v2)
    {
        long l1 = v1.GetLong();
        switch (function)
        {
            case Bitand:
                l1 &= v2!.GetLong();
                break;
            case Bitor:
                l1 |= v2!.GetLong();
                break;
            case Bitxor:
                l1 ^= v2!.GetLong();
                break;
            case Bitnot:
                l1 = ~l1;
                break;
            case Bitnand:
                l1 = ~(l1 & v2!.GetLong());
                break;
            case Bitnor:
                l1 = ~(l1 | v2!.GetLong());
                break;
            case Bitxnor:
                l1 = ~(l1 ^ v2!.GetLong());
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        return type.GetValueType() switch
        {
            Value.Tinyint => ValueTinyint.Get((sbyte)l1),
            Value.Smallint => ValueSmallint.Get((short)l1),
            Value.Integer => ValueInteger.Get((int)l1),
            Value.Bigint => ValueBigint.Get(l1),
            _ => throw DbException.GetInternalError(),
        };
    }

    /// <summary>
    /// Checks the type of an argument of a bitwise function.
    /// </summary>
    /// <param name="arg">the argument</param>
    /// <returns>the type of the specified argument</returns>
    /// <exception cref="H2SharpException">if argument type is not supported by bitwise functions</exception>
    public static TypeInfo CheckArgType(Expression arg)
    {
        TypeInfo t = arg.GetTypeInfo();
        switch (t.GetValueType())
        {
            case Value.Null:
            case Value.Binary:
            case Value.Varbinary:
            case Value.Tinyint:
            case Value.Smallint:
            case Value.Integer:
            case Value.Bigint:
                return t;
        }

        if (t.GetValueType() == Value.Unknown)
        {
            throw DbException.Get(ErrorCode.UnknownDataType1, arg.GetTraceSql());
        }

        throw DbException.Get(ErrorCode.InvalidValue2, t.GetTraceSql(), "bit function argument");
    }
}
