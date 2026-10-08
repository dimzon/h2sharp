/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.Value.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The core surface: what every value can answer for itself.
 *
 * The reference class is 2871 lines and has no natural core, because almost every method on
 * it either converts to a concrete value type or dispatches to one. What is here is the part
 * that needs neither: the type number, the type, the memory estimate, the lengths, the truth
 * tests, and the accessors a subclass must answer. The accessors that need a subclass to
 * exist throw with a reason rather than returning a plausible zero.
 *
 * The reference class is cut into files here by what each part is about:
 *
 *   Value.cs           the type number constants, generated, do not edit
 *   Value.Core.cs      this file: the surface every value answers
 *   Value.TypeOrder.cs how type numbers order and combine, already ported
 *   Value.Arithmetic.cs add, subtract, multiply, divide, modulus, negate, signum
 *   Value.Compare.cs   comparing two values, needs the concrete types
 *   Value.Conversion.cs the convertTo family, needs the concrete types
 *   Value.Lobs.cs      readers, byte arrays and streams
 *
 * Value.Compare, Value.Conversion and Value.Lobs are not written yet. They are named here so
 * the shape of the port is on record rather than implied, and because each of them is blocked
 * on the same thing: a concrete value type to convert to or dispatch on.
 */
using System;
using System.IO;
using System.Numerics;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// A value of some type. Every value knows its type number, and every type knows how to
/// describe itself as SQL.
/// </summary>
public abstract partial class Value
{
    /// <summary>
    /// The empty array of values, so that a call returning "no values" allocates nothing.
    /// </summary>
    public static readonly Value[] EmptyValues = [];

    /// <summary>
    /// Get the data type of this value.
    /// </summary>
    /// <remarks>
    /// Named GetTypeInfo rather than GetType because GetType() already exists on every
    /// object in .NET, and hiding it would be a trap: a mistyped call would silently resolve
    /// to the reflection member and return the wrong Type. The same reasoning renamed
    /// ExtTypeInfoEnum.getType.
    /// </remarks>
    /// <returns>the data type</returns>
    public abstract TypeInfo GetTypeInfo();

    /// <summary>
    /// Get the value type.
    /// </summary>
    /// <returns>the value type number</returns>
    public abstract int GetValueType();

    /// <summary>
    /// Get the amount of memory this type uses.
    /// </summary>
    /// <remarks>
    /// The reference returns a fixed 24 with a note that it was measured on Java 11 without
    /// compressed object pointers, for every type up to <c>ValueBigint</c> and
    /// <c>ValueDouble</c>. The number is an estimate the engine uses to size its caches, not
    /// a measurement, so it is kept as the reference states it rather than recomputed per
    /// runtime, which would make it incomparable with the reference's own estimate.
    /// </remarks>
    /// <returns>the memory estimate</returns>
    public virtual int GetMemory() => 24;

    /// <summary>
    /// Get the value as a string.
    /// </summary>
    /// <returns>the string</returns>
    public abstract string? GetString();

    /// <summary>
    /// Get the value as a reader.
    /// </summary>
    /// <returns>the reader</returns>
    public virtual TextReader? GetReader() => new StringReader(GetString() ?? string.Empty);

    /// <summary>
    /// Get the value as a byte array.
    /// </summary>
    /// <returns>the byte array</returns>
    public virtual byte[]? GetBytes()
        => throw GetUnsupportedException("getBytes");

    /// <summary>
    /// Get the value as a byte array, without copying.
    /// </summary>
    /// <returns>the byte array</returns>
    public virtual byte[]? GetBytesNoCopy() => GetBytes();

    /// <summary>
    /// Get the value as a boolean.
    /// </summary>
    /// <returns>the boolean value</returns>
    public virtual bool GetBoolean()
        => throw GetUnsupportedException("getBoolean");

    /// <summary>
    /// Get the value as a byte.
    /// </summary>
    /// <remarks>
    /// Signed, because Java's byte is signed and the two languages do not agree on the range of
    /// their byte: Java's is -128..127, .NET's is 0..255. Using .NET's byte would make the value
    /// for -1 come back as 255, which is the same number and a different value.
    /// <para>
    /// The reference answers this by converting to TINYINT and asking that value, so a SMALLINT
    /// does have a byte and it is the low one. The general conversion machinery is not ported, so
    /// this is the narrow version of it: the value's own number with the range of a byte checked.
    /// For the numeric family that is the same answer. For a type whose number is not the value,
    /// such as a byte array, the general conversion has to arrive before this is exact.
    /// </para>
    /// </remarks>
    /// <returns>the byte value</returns>
    /// <exception cref="H2SharpException">if the value does not fit a byte</exception>
    public virtual sbyte GetByte()
    {
        int number = GetInt();
        if (number is < sbyte.MinValue or > sbyte.MaxValue)
        {
            GetNumericValueOutOfRange(number);
            return 0;
        }

        return (sbyte)number;
    }

    /// <summary>
    /// Get the value as a 16 bit integer.
    /// </summary>
    /// <returns>the short value</returns>
    public virtual short GetShort() => throw GetUnsupportedException("getShort");

    /// <summary>
    /// Get the value as an integer.
    /// </summary>
    /// <returns>the integer value</returns>
    public virtual int GetInt() => throw GetUnsupportedException("getInt");

    /// <summary>
    /// Get the value as a long.
    /// </summary>
    /// <returns>the long value</returns>
    public virtual long GetLong() => throw GetUnsupportedException("getLong");

    /// <summary>
    /// Get the value as a double.
    /// </summary>
    /// <returns>the double value</returns>
    public virtual double GetDouble() => throw GetUnsupportedException("getDouble");

    /// <summary>
    /// Get the value as a float.
    /// </summary>
    /// <returns>the float value</returns>
    public virtual float GetFloat() => throw GetUnsupportedException("getFloat");

    /// <summary>
    /// Get the value as an arbitrary precision integer.
    /// </summary>
    /// <returns>the integer value</returns>
    public virtual BigInteger GetBigInteger()
        => throw GetUnsupportedException("getBigInteger");

    /// <summary>
    /// Get the value as an arbitrary precision decimal.
    /// </summary>
    /// <remarks>
    /// This used to hand back a .NET <see cref="decimal"/> with a note that NUMERIC would have to
    /// do better, and that note has been paid off: <see cref="H2Sharp.Util.BigDecimal"/> is the
    /// port of <c>java.math.BigDecimal</c> and it is what the reference's own name refers to, so
    /// the declaration is the same. .NET's decimal stays out of it: it holds at most 28 significant
    /// digits and rounds on division, and NUMERIC(38, 10) is a real column type.
    /// </remarks>
    /// <returns>the decimal value</returns>
    public virtual H2Sharp.Util.BigDecimal GetBigDecimal()
        => throw GetUnsupportedException("getBigDecimal");

    /// <summary>
    /// Compare two values of the same type, without the conversion and NULL handling that
    /// <see cref="Value.Compare.CompareTo(Value?, CastDataProvider?, CompareMode)"/> does.
    /// </summary>
    /// <remarks>
    /// Named CompareTypeSafe rather than CompareTypeSafe with a shorter name because "type safe"
    /// is the reference's own word for it: the caller has already established that both values
    /// have the same type, so this must not do any work to find that out or to convert
    /// anything.
    /// </remarks>
    /// <param name="o">the other value, of the same type as this one</param>
    /// <param name="mode">the compare mode</param>
    /// <param name="provider">the cast information provider</param>
    /// <returns>-1 if this value is smaller, 1 if it is larger, 0 if they are equal</returns>
    public virtual int CompareTypeSafe(
        Value o,
        CompareMode mode,
        H2Sharp.Engine.CastDataProvider? provider)
        => throw GetUnsupportedException("compareTypeSafe");

    /// <summary>
    /// Get the sign of the value, where negative is -1, zero is 0, and positive is 1.
    /// </summary>
    /// <returns>the signum</returns>
    public virtual int GetSignum() => throw GetUnsupportedException("getSignum");

    /// <summary>
    /// Check if this value contains NULL, which is true for NULL itself and for a compound
    /// value with a NULL inside.
    /// </summary>
    /// <returns>whether this value contains NULL</returns>
    public virtual bool ContainsNull() => false;

    /// <summary>
    /// Scans this and specified values until a first NULL occurrence and returns
    /// a value where NULL appears earlier, or <see langword="null"/> if these two values
    /// have first NULL on the same position.
    /// </summary>
    /// <param name="v">
    /// a value of the same data type as this value, must be neither
    /// equal to nor smaller than nor greater than this value
    /// </param>
    /// <returns>this value, the specified value, or <see langword="null"/></returns>
    public Value? GetValueWithFirstNull(Value? v)
        => ReferenceEquals(this, ValueNull.Instance)
            ? ReferenceEquals(v, ValueNull.Instance) ? null : ValueNull.Instance
            : ReferenceEquals(v, ValueNull.Instance) ? ValueNull.Instance
            : GetValueWithFirstNullImpl(v!);

    /// <summary>
    /// The part of <see cref="GetValueWithFirstNull"/> that only a compound value has to
    /// answer, since a scalar is never "the other one" when neither is NULL.
    /// </summary>
    /// <param name="v">the other value, neither of them NULL</param>
    /// <returns>this value</returns>
    protected virtual Value GetValueWithFirstNullImpl(Value v) => this;

    /// <summary>
    /// Returns length of this value in characters.
    /// </summary>
    /// <returns>length of this value in characters</returns>
    /// <exception cref="NullReferenceException">if this value is <c>NULL</c></exception>
    public virtual long CharLength() => (GetString() ?? throw new NullReferenceException()).Length;

    /// <summary>
    /// Returns length of this value in bytes.
    /// </summary>
    /// <returns>length of this value in bytes</returns>
    /// <exception cref="NullReferenceException">if this value is <c>NULL</c></exception>
    public virtual long OctetLength()
        => (GetBytesNoCopy() ?? throw new NullReferenceException()).Length;

    /// <summary>
    /// Returns whether this value <c>IS TRUE</c>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if it is. For <c>BOOLEAN</c> values returns
    /// <see langword="true"/> for <c>TRUE</c> and <see langword="false"/> for <c>FALSE</c>
    /// and <c>UNKNOWN</c> (<c>NULL</c>).
    /// </returns>
    /// <seealso cref="GetBoolean"/>
    /// <seealso cref="IsFalse"/>
    public bool IsTrue() => !ReferenceEquals(this, ValueNull.Instance) && GetBoolean();

    /// <summary>
    /// Returns whether this value <c>IS FALSE</c>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if it is. For <c>BOOLEAN</c> values returns
    /// <see langword="true"/> for <c>FALSE</c> and <see langword="true"/> for <c>TRUE</c>
    /// and <c>UNKNOWN</c> (<c>NULL</c>).
    /// </returns>
    /// <seealso cref="GetBoolean"/>
    /// <seealso cref="IsTrue"/>
    public bool IsFalse() => !ReferenceEquals(this, ValueNull.Instance) && !GetBoolean();

    /// <summary>
    /// Throws the conversion error the reference reports when a value cannot become the
    /// requested type.
    /// </summary>
    /// <remarks>
    /// Returns nothing rather than an exception, because the reference's method throws from
    /// inside rather than returning one, and every call site writes it as a throw.
    /// </remarks>
    /// <param name="targetType">the type the value could not become</param>
    /// <exception cref="H2SharpException">always</exception>
    protected void GetDataConversionError(int targetType)
        => throw DbException.Get(
            ErrorCode.DataConversionError1,
            Value.GetTypeName(GetValueType()) + " to " + Value.GetTypeName(targetType));

    /// <summary>
    /// Throws the out of range error for a number that does not fit the type asked for.
    /// </summary>
    /// <remarks>
    /// This is a different error from the conversion one and the reference uses it here: converting
    /// a SMALLINT of 32767 to a TINYINT says that 32767 is out of range, not that a SMALLINT cannot
    /// become a TINYINT, because it can.
    /// </remarks>
    /// <param name="value">the number that did not fit</param>
    /// <exception cref="H2SharpException">always</exception>
    protected void GetNumericValueOutOfRange(long value)
        => throw DbException.Get(
            ErrorCode.NumericValueOutOfRange1,
            value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Throws the conversion error for a parameterised target type.
    /// </summary>
    /// <param name="targetType">the type the value could not become</param>
    /// <exception cref="H2SharpException">always</exception>
    protected void GetDataConversionError(TypeInfo targetType)
        => throw DbException.Get(
            ErrorCode.DataConversionError1,
            Value.GetTypeName(GetValueType()) + " to " + targetType.GetTraceSql());

    /// <summary>
    /// Append the SQL representation of this value to a string builder.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the specified builder</returns>
    public virtual StringBuilder GetSql(StringBuilder builder, int sqlFlags) => builder;

    /// <summary>
    /// The SQL representation of this value with the trace formatting flags.
    /// </summary>
    /// <returns>the SQL text</returns>
    public string GetTraceSql() => GetSql(new StringBuilder(), HasSql.TraceSqlFlags).ToString();

    /// <inheritdoc/>
    public override string ToString() => GetTraceSql();

    /// <summary>
    /// Create an exception meaning the specified operation is not supported for
    /// this data type.
    /// </summary>
    /// <param name="op">the operation</param>
    /// <returns>the exception</returns>
    protected H2SharpException GetUnsupportedExceptionForOperation(string op)
        => DbException.GetUnsupportedException(Value.GetTypeName(GetValueType()) + ' ' + op);

    /// <summary>
    /// The failure for an accessor that needs a concrete value type that is not ported yet.
    /// </summary>
    /// <remarks>
    /// Separate from the reference's <c>getUnsupportedExceptionForOperation</c> because it is
    /// not about the data type refusing the operation: it is about this port not having the
    /// subclass yet. Returning a zero instead would be a lie the caller cannot see.
    /// </remarks>
    /// <param name="what">the accessor</param>
    /// <returns>the exception</returns>
    private Exception GetUnsupportedException(string what)
        => DbException.GetInternalError(
            $"Value.{what} needs the concrete value type for {Value.GetTypeName(GetValueType())}, "
            + "which is not ported yet. See MIGRATION_STATUS.md for the order.");

    /// <summary>
    /// Check that a range lies inside a data size.
    /// </summary>
    /// <remarks>
    /// A range is given as a zero based offset and a length, as in the reference, and the
    /// message reports one based positions because that is what a user would see.
    /// </remarks>
    /// <param name="zeroBasedOffset">the zero based offset</param>
    /// <param name="length">the length</param>
    /// <param name="dataSize">the size of the data</param>
    /// <exception cref="H2SharpException">if the range is negative or does not fit</exception>
    internal static void RangeCheck(long zeroBasedOffset, long length, long dataSize)
    {
        if ((zeroBasedOffset | length) < 0 || length > dataSize - zeroBasedOffset)
        {
            if (zeroBasedOffset < 0 || zeroBasedOffset > dataSize)
            {
                throw DbException.GetInvalidValueException(
                    "offset", zeroBasedOffset + 1);
            }

            throw DbException.GetInvalidValueException("length", length);
        }
    }
}
