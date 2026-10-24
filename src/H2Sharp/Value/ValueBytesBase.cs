/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueBytesBase.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Base implementation of byte array based data types.
/// </summary>
public abstract class ValueBytesBase : Value
{
    /// <summary>
    /// The value.
    /// </summary>
    protected byte[] value;

    private TypeInfo? type;

    private int hash;

    /// <summary>
    /// Creates a byte array value.
    /// </summary>
    /// <param name="v">the bytes</param>
    /// <exception cref="H2SharpException">if the array is longer than a value may be</exception>
    protected ValueBytesBase(byte[] v)
    {
        int length = v.Length;
        if (length > Constants.MaxStringLength)
        {
            throw DbException.GetValueTooLongException(
                GetTypeName(GetValueType()), StringUtils.ConvertBytesToHex(v, 41), length);
        }

        value = v;
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        TypeInfo? cached = type;
        if (cached is null)
        {
            long precision = value.Length;
            type = cached = new TypeInfo(GetValueType(), precision, 0, null);
        }

        return cached;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The reference compares the arrays as unsigned, which is what <c>Arrays.compareUnsigned</c>
    /// says, and what a byte array means to a reader: <c>X'80'</c> sorts after <c>X'7F'</c>. The
    /// platform's own comparison of two <see cref="byte"/> arrays is signed and answers the
    /// opposite way round, so the ordering is spelled out here instead of being delegated.
    /// </remarks>
    public sealed override int CompareTypeSafe(Value v, CompareMode mode, CastDataProvider? provider)
        => Math.Sign(CompareUnsigned(value, ((ValueBytesBase)v).value));

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => Dialect.AppendBytes(builder, this, sqlFlags);

    /// <inheritdoc/>
    /// <remarks>
    /// The reference mixes in <c>getClass().hashCode()</c>, which in Java is an identity hash:
    /// a different number on every run, in the reference itself. So a byte array's hash is not a
    /// reproducible value on either side, and it is deliberately not pinned by the differential
    /// test. Equality, which is what a hash has to agree with, is compared.
    /// </remarks>
    public sealed override int GetHashCode()
    {
        int h = hash;
        if (h == 0)
        {
            unchecked
            {
                // The class part stands in for the reference's identity hash, which is the only
                // honest thing to do with it: any number here would differ from the reference's,
                // and none would be stable either.
                h = GetType().GetHashCode() ^ Utils.GetByteArrayHash(value);
                if (h == 0)
                {
                    h = 1_234_570_417;
                }
            }

            hash = h;
        }

        return h;
    }

    /// <inheritdoc/>
    public sealed override byte[]? GetBytes() => Utils.CloneByteArray(value);

    /// <inheritdoc/>
    public sealed override byte[]? GetBytesNoCopy() => value;

    /// <inheritdoc/>
    /// <remarks>
    /// Not sealed, because the reference does not seal it either: a GEOMETRY counts twenty bytes per
    /// byte of geometry rather than one, so its own value is twenty times this.
    /// </remarks>
    public override int GetMemory() => value.Length + 24;

    /// <inheritdoc/>
    /// <remarks>
    /// The reference asks whether the two objects are the same class and not merely related by
    /// inheritance, so a BINARY never equals a VARBINARY with the same bytes. Overridden, and not
    /// inherited, for the same reason.
    /// </remarks>
    public sealed override bool Equals(object? other)
        => other is not null
            && GetType() == other.GetType()
            && ((ValueBytesBase)other).value.AsSpan().SequenceEqual(value);

    /// <summary>
    /// Compares two byte arrays the way Java's <c>Arrays.compareUnsigned</c> does.
    /// </summary>
    /// <remarks>
    /// An array is shorter than any longer array that starts with it, so the length decides only
    /// when every shared byte is equal. Bytes are unsigned on both sides of the comparison.
    /// </remarks>
    /// <param name="a">the first array</param>
    /// <param name="b">the second array</param>
    /// <returns>
    /// a negative number if the first array is smaller, 0 if the arrays are equal, a positive
    /// number otherwise
    /// </returns>
    private static int CompareUnsigned(byte[] a, byte[] b)
        => a.AsSpan().SequenceCompareTo(b);
}
