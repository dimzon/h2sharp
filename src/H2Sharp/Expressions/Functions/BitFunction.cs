/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.BitFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Numerics;
using System.Text;

using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Aggregate;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

using AggregateExpression = H2Sharp.Expressions.Aggregate.Aggregate;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A bitwise function.
/// </summary>
public sealed class BitFunction : Function1_2
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
    /// BITGET() (non-standard).
    /// </summary>
    public const int Bitget = Bitxnor + 1;

    /// <summary>
    /// BITCOUNT() (non-standard).
    /// </summary>
    public const int Bitcount = Bitget + 1;

    /// <summary>
    /// LSHIFT() (non-standard).
    /// </summary>
    public const int Lshift = Bitcount + 1;

    /// <summary>
    /// RSHIFT() (non-standard).
    /// </summary>
    public const int Rshift = Lshift + 1;

    /// <summary>
    /// ULSHIFT() (non-standard).
    /// </summary>
    public const int Ulshift = Rshift + 1;

    /// <summary>
    /// URSHIFT() (non-standard).
    /// </summary>
    public const int Urshift = Ulshift + 1;

    /// <summary>
    /// ROTATELEFT() (non-standard).
    /// </summary>
    public const int Rotateleft = Urshift + 1;

    /// <summary>
    /// ROTATERIGHT() (non-standard).
    /// </summary>
    public const int Rotateright = Rotateleft + 1;

    private static readonly string[] Names =
    [
        "BITAND", "BITOR", "BITXOR", "BITNOT", "BITNAND", "BITNOR", "BITXNOR", "BITGET", "BITCOUNT", "LSHIFT",
        "RSHIFT", "ULSHIFT", "URSHIFT", "ROTATELEFT", "ROTATERIGHT",
    ];

    private readonly int function;

    /// <summary>
    /// Creates a bitwise function with the given arguments.
    /// </summary>
    /// <param name="arg1">the first argument</param>
    /// <param name="arg2">the second argument, or null</param>
    /// <param name="function">the function code</param>
    public BitFunction(Expression arg1, Expression? arg2, int function)
        : base(arg1, arg2)
    {
        this.function = function;
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value v1, Value? v2)
    {
        switch (function)
        {
            case Bitget:
                return BitGet(v1, v2!);
            case Bitcount:
                return BitCount(v1);
            case Lshift:
                return Shift(v1, v2!.GetLong(), false);
            case Rshift:
                {
                    long offset = v2!.GetLong();
                    return Shift(v1, offset != long.MinValue ? -offset : long.MaxValue, false);
                }

            case Ulshift:
                return Shift(v1, v2!.GetLong(), true);
            case Urshift:
                return Shift(v1, -v2!.GetLong(), true);
            case Rotateleft:
                return Rotate(v1, v2!.GetLong(), false);
            case Rotateright:
                return Rotate(v1, v2!.GetLong(), true);
            default:
                return GetBitwise(function, type!, v1, v2);
        }
    }

    private static ValueBoolean BitGet(Value v1, Value v2)
    {
        long offset = v2.GetLong();
        bool b;
        if (offset >= 0L)
        {
            switch (v1.GetValueType())
            {
                case Value.Binary:
                case Value.Varbinary:
                    {
                        byte[] bytes = v1.GetBytesNoCopy()!;
                        int bit = (int)(offset & 0x7);
                        offset >>= 3;
                        b = offset < bytes.Length && (bytes[(int)offset] & (1 << bit)) != 0;
                        break;
                    }

                case Value.Tinyint:
                    b = offset < 8 && (v1.GetByte() & (1 << (int)offset)) != 0;
                    break;
                case Value.Smallint:
                    b = offset < 16 && (v1.GetShort() & (1 << (int)offset)) != 0;
                    break;
                case Value.Integer:
                    b = offset < 32 && (v1.GetInt() & (1 << (int)offset)) != 0;
                    break;
                case Value.Bigint:
                    b = (v1.GetLong() & (1L << (int)offset)) != 0;
                    break;
                default:
                    throw DbException.GetInvalidValueException("bit function parameter", v1.GetTraceSql());
            }
        }
        else
        {
            b = false;
        }

        return ValueBoolean.Get(b);
    }

    private static ValueBigint BitCount(Value v1)
    {
        long c;
        switch (v1.GetValueType())
        {
            case Value.Binary:
            case Value.Varbinary:
                {
                    byte[] bytes = v1.GetBytesNoCopy()!;
                    int l = bytes.Length;
                    c = 0L;
                    int i = 0;
                    for (int bound = l & unchecked((int)0xfffffff8); i < bound; i += 8)
                    {
                        c += BitOperations.PopCount((ulong)Bits.LongFromBigEndian(bytes, i));
                    }

                    for (; i < l; i++)
                    {
                        c += BitOperations.PopCount((uint)(bytes[i] & 0xff));
                    }

                    break;
                }

            case Value.Tinyint:
                c = BitOperations.PopCount((uint)(v1.GetByte() & 0xff));
                break;
            case Value.Smallint:
                c = BitOperations.PopCount((uint)(v1.GetShort() & 0xffff));
                break;
            case Value.Integer:
                c = BitOperations.PopCount((uint)v1.GetInt());
                break;
            case Value.Bigint:
                c = BitOperations.PopCount((ulong)v1.GetLong());
                break;
            default:
                throw DbException.GetInvalidValueException("bit function parameter", v1.GetTraceSql());
        }

        return ValueBigint.Get(c);
    }

    private static Value Shift(Value v1, long offset, bool unsigned)
    {
        if (offset == 0L)
        {
            return v1;
        }

        int vt = v1.GetValueType();
        switch (vt)
        {
            case Value.Binary:
            case Value.Varbinary:
                {
                    byte[] bytes = v1.GetBytesNoCopy()!;
                    int length = bytes.Length;
                    if (length == 0)
                    {
                        return v1;
                    }

                    byte[] newBytes = new byte[length];
                    if (offset > -8L * length && offset < 8L * length)
                    {
                        if (offset > 0)
                        {
                            int nBytes = (int)(offset >> 3);
                            int nBits = ((int)offset) & 0x7;
                            if (nBits == 0)
                            {
                                Array.Copy(bytes, nBytes, newBytes, 0, length - nBytes);
                            }
                            else
                            {
                                int nBits2 = 8 - nBits;
                                int dstIndex = 0, srcIndex = nBytes;
                                length--;
                                while (srcIndex < length)
                                {
                                    newBytes[dstIndex++] = (byte)(bytes[srcIndex++] << nBits
                                        | (bytes[srcIndex] & 0xff) >>> nBits2);
                                }

                                newBytes[dstIndex] = (byte)(bytes[srcIndex] << nBits);
                            }
                        }
                        else
                        {
                            offset = -offset;
                            int nBytes = (int)(offset >> 3);
                            int nBits = ((int)offset) & 0x7;
                            if (nBits == 0)
                            {
                                Array.Copy(bytes, 0, newBytes, nBytes, length - nBytes);
                            }
                            else
                            {
                                int nBits2 = 8 - nBits;
                                int dstIndex = nBytes, srcIndex = 0;
                                newBytes[dstIndex++] = (byte)((bytes[srcIndex] & 0xff) >>> nBits);
                                while (dstIndex < length)
                                {
                                    newBytes[dstIndex++] = (byte)(bytes[srcIndex++] << nBits2
                                        | (bytes[srcIndex] & 0xff) >>> nBits);
                                }
                            }
                        }
                    }

                    return vt == Value.Binary ? ValueBinary.GetNoCopy(newBytes) : ValueVarbinary.GetNoCopy(newBytes);
                }

            case Value.Tinyint:
                {
                    sbyte v;
                    if (offset < 8)
                    {
                        v = v1.GetByte();
                        if (offset > -8)
                        {
                            if (offset > 0)
                            {
                                v <<= (int)offset;
                            }
                            else if (unsigned)
                            {
                                v = (sbyte)((v & 0xFF) >>> (int)-offset);
                            }
                            else
                            {
                                v >>= (int)-offset;
                            }
                        }
                        else if (unsigned)
                        {
                            v = 0;
                        }
                        else
                        {
                            v >>= 7;
                        }
                    }
                    else
                    {
                        v = 0;
                    }

                    return ValueTinyint.Get(v);
                }

            case Value.Smallint:
                {
                    short v;
                    if (offset < 16)
                    {
                        v = v1.GetShort();
                        if (offset > -16)
                        {
                            if (offset > 0)
                            {
                                v <<= (int)offset;
                            }
                            else if (unsigned)
                            {
                                v = (short)((v & 0xFFFF) >>> (int)-offset);
                            }
                            else
                            {
                                v >>= (int)-offset;
                            }
                        }
                        else if (unsigned)
                        {
                            v = 0;
                        }
                        else
                        {
                            v >>= 15;
                        }
                    }
                    else
                    {
                        v = 0;
                    }

                    return ValueSmallint.Get(v);
                }

            case Value.Integer:
                {
                    int v;
                    if (offset < 32)
                    {
                        v = v1.GetInt();
                        if (offset > -32)
                        {
                            if (offset > 0)
                            {
                                v <<= (int)offset;
                            }
                            else if (unsigned)
                            {
                                v >>>= (int)-offset;
                            }
                            else
                            {
                                v >>= (int)-offset;
                            }
                        }
                        else if (unsigned)
                        {
                            v = 0;
                        }
                        else
                        {
                            v >>= 31;
                        }
                    }
                    else
                    {
                        v = 0;
                    }

                    return ValueInteger.Get(v);
                }

            case Value.Bigint:
                {
                    long v;
                    if (offset < 64)
                    {
                        v = v1.GetLong();
                        if (offset > -64)
                        {
                            if (offset > 0)
                            {
                                v <<= (int)offset;
                            }
                            else if (unsigned)
                            {
                                v >>>= (int)-offset;
                            }
                            else
                            {
                                v >>= (int)-offset;
                            }
                        }
                        else if (unsigned)
                        {
                            v = 0;
                        }
                        else
                        {
                            v >>= 63;
                        }
                    }
                    else
                    {
                        v = 0;
                    }

                    return ValueBigint.Get(v);
                }

            default:
                throw DbException.GetInvalidValueException("bit function parameter", v1.GetTraceSql());
        }
    }

    private static Value Rotate(Value v1, long offset, bool right)
    {
        int vt = v1.GetValueType();
        switch (vt)
        {
            case Value.Binary:
            case Value.Varbinary:
                {
                    byte[] bytes = v1.GetBytesNoCopy()!;
                    int length = bytes.Length;
                    if (length == 0)
                    {
                        return v1;
                    }

                    long bitLength = length << 3;
                    offset %= bitLength;
                    if (right)
                    {
                        offset = -offset;
                    }

                    if (offset == 0L)
                    {
                        return v1;
                    }
                    else if (offset < 0)
                    {
                        offset += bitLength;
                    }

                    byte[] newBytes = new byte[length];
                    int nBytes = (int)(offset >> 3);
                    int nBits = ((int)offset) & 0x7;
                    if (nBits == 0)
                    {
                        Array.Copy(bytes, nBytes, newBytes, 0, length - nBytes);
                        Array.Copy(bytes, 0, newBytes, length - nBytes, nBytes);
                    }
                    else
                    {
                        int nBits2 = 8 - nBits;
                        for (int dstIndex = 0, srcIndex = nBytes; dstIndex < length;)
                        {
                            newBytes[dstIndex++] = (byte)(bytes[srcIndex] << nBits
                                | (bytes[srcIndex = (srcIndex + 1) % length] & 0xFF) >>> nBits2);
                        }
                    }

                    return vt == Value.Binary ? ValueBinary.GetNoCopy(newBytes) : ValueVarbinary.GetNoCopy(newBytes);
                }

            case Value.Tinyint:
                {
                    int o = (int)offset;
                    if (right)
                    {
                        o = -o;
                    }

                    if ((o &= 0x7) == 0)
                    {
                        return v1;
                    }

                    int v = v1.GetByte() & 0xFF;
                    return ValueTinyint.Get((sbyte)((v << o) | (v >>> (8 - o))));
                }

            case Value.Smallint:
                {
                    int o = (int)offset;
                    if (right)
                    {
                        o = -o;
                    }

                    if ((o &= 0xF) == 0)
                    {
                        return v1;
                    }

                    int v = v1.GetShort() & 0xFFFF;
                    return ValueSmallint.Get((short)((v << o) | (v >>> (16 - o))));
                }

            case Value.Integer:
                {
                    int o = (int)offset;
                    if (right)
                    {
                        o = -o;
                    }

                    if ((o &= 0x1F) == 0)
                    {
                        return v1;
                    }

                    return ValueInteger.Get((int)BitOperations.RotateLeft((uint)v1.GetInt(), o));
                }

            case Value.Bigint:
                {
                    int o = (int)offset;
                    if (right)
                    {
                        o = -o;
                    }

                    if ((o &= 0x3F) == 0)
                    {
                        return v1;
                    }

                    return ValueBigint.Get((long)BitOperations.RotateLeft((ulong)v1.GetLong(), o));
                }

            default:
                throw DbException.GetInvalidValueException("bit function parameter", v1.GetTraceSql());
        }
    }

    /// <summary>
    /// Computes the value of a bitwise function.
    /// </summary>
    /// <param name="function">one of <see cref="Bitand"/>, <see cref="Bitor"/>, <see cref="Bitxor"/>, <see cref="Bitnot"/>, <see cref="Bitnand"/>, <see cref="Bitnor"/>, <see cref="Bitxnor"/></param>
    /// <param name="type">the type of result</param>
    /// <param name="v1">the value of first argument</param>
    /// <param name="v2">the value of second argument, or null</param>
    /// <returns>the resulting value</returns>
    public static Value GetBitwise(int function, TypeInfo type, Value v1, Value? v2)
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

            int limit = (int)type.GetPrecision();
            if (min > limit)
            {
                max = min = limit;
            }
            else if (max > limit)
            {
                max = limit;
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
            case Bitnot:
                return OptimizeNot(session);
            case Bitget:
                type = TypeInfo.TypeBoolean;
                break;
            case Bitcount:
                type = TypeInfo.TypeBigint;
                break;
            case Lshift:
            case Rshift:
            case Ulshift:
            case Urshift:
            case Rotateleft:
            case Rotateright:
                type = CheckArgType(left);
                break;
            default:
                type = GetCommonType(left, right!);
                break;
        }

        if (left.IsConstant() && (right == null || right.IsConstant()))
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    private Expression OptimizeNot(SessionLocal session)
    {
        type = CheckArgType(left);
        if (left.IsConstant())
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }
        else if (left is BitFunction bitFunction)
        {
            int f = bitFunction.function;
            switch (f)
            {
                case Bitand:
                case Bitor:
                case Bitxor:
                    f += Bitnand - Bitand;
                    break;
                case Bitnot:
                    return bitFunction.left;
                case Bitnand:
                case Bitnor:
                case Bitxnor:
                    f -= Bitnand - Bitand;
                    break;
                default:
                    return this;
            }

            return new BitFunction(bitFunction.left, bitFunction.right, f).Optimize(session);
        }
        else if (left is AggregateExpression aggregate)
        {
            AggregateType t;
            switch (aggregate.GetAggregateType())
            {
                case AggregateType.BitAndAgg:
                    t = AggregateType.BitNandAgg;
                    break;
                case AggregateType.BitOrAgg:
                    t = AggregateType.BitNorAgg;
                    break;
                case AggregateType.BitXorAgg:
                    t = AggregateType.BitXnorAgg;
                    break;
                case AggregateType.BitNandAgg:
                    t = AggregateType.BitAndAgg;
                    break;
                case AggregateType.BitNorAgg:
                    t = AggregateType.BitOrAgg;
                    break;
                case AggregateType.BitXnorAgg:
                    t = AggregateType.BitXorAgg;
                    break;
                default:
                    return this;
            }

            AggregateExpression rewritten = new(t, [aggregate.GetSubexpression(0)!], aggregate.GetSelect(),
                aggregate.IsDistinct());
            rewritten.SetFilterCondition(aggregate.GetFilterCondition());
            rewritten.SetOverCondition(aggregate.GetOverCondition());
            return rewritten.Optimize(session);
        }

        return this;
    }

    private static TypeInfo GetCommonType(Expression arg1, Expression arg2)
    {
        TypeInfo t1 = CheckArgType(arg1), t2 = CheckArgType(arg2);
        int vt1 = t1.GetValueType(), vt2 = t2.GetValueType();
        bool bs = DataType.IsBinaryStringType(vt1);
        if (bs != DataType.IsBinaryStringType(vt2))
        {
            throw DbException.GetInvalidValueException("bit function parameters",
                t2.GetSql(t1.GetSql(new StringBuilder(), HasSql.TraceSqlFlags).Append(" vs "),
                    HasSql.TraceSqlFlags).ToString());
        }

        if (bs)
        {
            long precision;
            if (vt1 == Value.Binary)
            {
                precision = t1.GetDeclaredPrecision();
                if (vt2 == Value.Binary)
                {
                    precision = Math.Max(precision, t2.GetDeclaredPrecision());
                }
            }
            else
            {
                if (vt2 == Value.Binary)
                {
                    vt1 = Value.Binary;
                    precision = t2.GetDeclaredPrecision();
                }
                else
                {
                    long precision1 = t1.GetDeclaredPrecision(), precision2 = t2.GetDeclaredPrecision();
                    precision = precision1 <= 0L || precision2 <= 0L ? -1L : Math.Max(precision1, precision2);
                }
            }

            return TypeInfo.GetTypeInfo(vt1, precision, 0, null);
        }

        return TypeInfo.GetTypeInfo(Math.Max(vt1, vt2));
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

        throw MvStore.Db.Store.GetInvalidExpressionTypeException("bit function argument", arg);
    }

    /// <inheritdoc/>
    public override string GetName() => Names[function];
}
