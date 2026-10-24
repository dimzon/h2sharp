/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * The H2 dialect as a facade: one centralized place where an H2 literal is written. The providers
 * that sit on this one — EF Core, linq2db — hold plain .NET values, and a literal inlined into a
 * statement has to be the literal the engine itself would print and read back, or a statement
 * that round-trips on the engine round-trips nowhere else. So the two halves of this class are:
 *
 * The public methods take the .NET value and append its literal directly: the digits, quotes and
 * prefixes come from the same helpers the engine renders with — DateTimeUtils, StringUtils,
 * IntervalUtils, JavaNumberFormat — and no engine value is built on the way, so a literal costs
 * what its text costs. The conversion rules are the ones H2ValueMapper converts parameters by;
 * a JSON document is the one exception, because the literal of an unnormalized document is a
 * different document, and the normalization is the engine's own work.
 *
 * The internal methods are the bodies of the reference's <c>Value.getSQL</c> methods, moved here
 * from the value classes so that the literal syntax has one home; the value classes' GetSql
 * overrides delegate here, which is what keeps the two from drifting apart. The default flags
 * are the reference's DEFAULT_SQL_FLAGS (none), which is why a literal whose type the parser
 * would not guess back arrives wrapped in a CAST, exactly where the reference puts one.
 *
 * Deviations from the reference, recorded here once:
 * - the class itself has no org.h2 counterpart (the reference keeps getSQL on the value classes);
 * - ValueLob.LobOctetLength, ValueLob.LobCharLength and ValueLob.FormatLobDataComment are
 *   widened from protected to internal, because the moved LOB bodies read them and a static
 *   method of another class cannot; the public OctetLength() was no substitute, because it
 *   reads the LOB, which the trace form deliberately avoids.
 */
using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Util;
using H2Sharp.Util.Geometry;
using H2Sharp.Util.Json;
using H2Sharp.Values;
using H2Sharp.Values.Lobs;

namespace H2Sharp.Data;

/// <summary>
/// The H2 dialect as a facade: SQL literals for plain .NET values, appended to a string builder.
/// </summary>
/// <remarks>
/// Every method appends the literal and returns the same builder, so calls chain:
/// <c>Dialect.AppendTimestamp(sql, value)</c> writes <c>TIMESTAMP '2024-01-02 10:00:00'</c>.
/// A <c>null</c> value writes a typed cast — <c>CAST(NULL AS TIMESTAMP)</c> out of the
/// timestamp method — because the type is known at the call site and a bare NULL is UNKNOWN,
/// which is the one thing a statement inlined into cannot carry; <see cref="AppendNull"/>
/// writes the bare NULL, the form the engine's own getSQL writes for ValueNull. Nothing is
/// allocated for the literal beyond the text itself: the value is formatted as it is read, and
/// no engine value is built on the way. The literal is the one the engine prints for the value
/// and parses back into it, which is the whole contract: what this class writes, a statement
/// can carry and the engine can read.
/// </remarks>
public static class Dialect
{
    // ------------------------------------------------------------------
    // The public layer: plain .NET values in, H2 literals out.
    // ------------------------------------------------------------------

    /// <summary>
    /// Appends the NULL literal.
    /// </summary>
    /// <remarks>
    /// The bare, untyped NULL — the form the engine's own getSQL writes for ValueNull. The
    /// nullable overloads of this class write a typed cast instead, because there the type is
    /// known; call this one when the caller wants NULL to stay UNKNOWN.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendNull(StringBuilder builder)
        => builder.Append("NULL");

    /// <summary>
    /// Appends the typed NULL: <c>CAST(NULL AS type)</c>, the form a known type deserves.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="type">the type name, as the parser reads it</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StringBuilder AppendNullCast(StringBuilder builder, string type)
        => builder.Append("CAST(NULL AS ").Append(type).Append(')');

    /// <summary>
    /// Appends the literal of a boolean: <c>TRUE</c> or <c>FALSE</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendBoolean(StringBuilder builder, bool? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "BOOLEAN");
        }

        return builder.Append(value.GetValueOrDefault() ? "TRUE" : "FALSE");
    }

    /// <summary>
    /// Appends the literal of a TINYINT, a signed byte.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendTinyint(StringBuilder builder, sbyte? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "TINYINT");
        }

        sbyte v = value.GetValueOrDefault();
        _ = StringUtils.AppendInvariant(builder.Append("CAST("), v);
        return builder.Append(" AS TINYINT)");
    }

    /// <summary>
    /// Appends the literal of a SMALLINT.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendSmallint(StringBuilder builder, short? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "SMALLINT");
        }

        short v = value.GetValueOrDefault();
        _ = StringUtils.AppendInvariant(builder.Append("CAST("), v);
        return builder.Append(" AS SMALLINT)");
    }

    /// <summary>
    /// Appends the literal of an INTEGER.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendInteger(StringBuilder builder, int? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "INTEGER");
        }

        return StringUtils.AppendInvariant(builder, value.GetValueOrDefault());
    }

    /// <summary>
    /// Appends the literal of a BIGINT.
    /// </summary>
    /// <remarks>
    /// A bigint that fits in an int prints with a CAST that says how wide it is, so that reading
    /// the literal back does not silently widen the type.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendBigint(StringBuilder builder, long? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "BIGINT");
        }

        long v = value.GetValueOrDefault();
        if (v == (int)v)
        {
            _ = builder.Append("CAST(");
            _ = StringUtils.AppendInvariant(builder, v);
            return builder.Append(" AS BIGINT)");
        }

        return StringUtils.AppendInvariant(builder, v);
    }

    /// <summary>
    /// Appends the literal of a NUMERIC: a plain number, never with an exponent.
    /// </summary>
    /// <remarks>
    /// A whole number that fits in a long prints with a CAST that says how wide it is, because a
    /// bare number would come back as whatever type the parser chose. Anything with a fractional
    /// part, or too wide for a long, prints as it is: the CAST would say nothing the number does
    /// not already say.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendNumeric(StringBuilder builder, decimal? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "NUMERIC");
        }

        decimal m = value.GetValueOrDefault();
        if (m.Scale == 0 && long.MinValue <= m && m <= long.MaxValue)
        {
            long v = (long)m;
            // The width the CAST names is the count of the digits, which is what the engine's
            // BigDecimal calls precision. The magnitude is taken as unsigned, so the negative
            // minimum is not negated into itself.
            ulong magnitude = v < 0 ? ~(ulong)v + 1UL : (ulong)v;
            int precision = 1;
            while (magnitude >= 10UL)
            {
                magnitude /= 10UL;
                precision++;
            }

            _ = builder.Append("CAST(");
            _ = StringUtils.AppendInvariant(builder, v);
            return builder.Append(" AS NUMERIC(").Append(precision).Append("))");
        }

        Span<char> digits = stackalloc char[31];
        if (!m.TryFormat(digits, out int written, default, CultureInfo.InvariantCulture))
        {
            return builder.Append(m.ToString(CultureInfo.InvariantCulture));
        }

        return builder.Append(digits[..written]);
    }

    /// <summary>
    /// Appends the literal of a REAL.
    /// </summary>
    /// <remarks>
    /// A float literal is a REAL or a DOUBLE depending on what follows it, so the literal says
    /// which one it is with a CAST. Infinity and NaN are not numbers and cannot be written as
    /// literals, so they are quoted as text and the conversion back does the work.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendReal(StringBuilder builder, float? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "REAL");
        }

        float v = value.GetValueOrDefault();
        if (float.IsPositiveInfinity(v))
        {
            return builder.Append("CAST('Infinity' AS REAL)");
        }

        if (float.IsNegativeInfinity(v))
        {
            return builder.Append("CAST('-Infinity' AS REAL)");
        }

        if (float.IsNaN(v))
        {
            return builder.Append("CAST('NaN' AS REAL)");
        }

        return builder.Append("CAST(").Append(JavaNumberFormat.FloatToString(v)).Append(" AS REAL)");
    }

    /// <summary>
    /// Appends the literal of a DOUBLE PRECISION.
    /// </summary>
    /// <remarks>
    /// Infinity and NaN are quoted as text, the way the engine writes them; the number itself is
    /// spelled the Java way, so that 0.0 prints as 0.0 and 1e7 as 1.0E7.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendDouble(StringBuilder builder, double? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "DOUBLE PRECISION");
        }

        double v = value.GetValueOrDefault();
        if (double.IsPositiveInfinity(v))
        {
            return builder.Append("CAST('Infinity' AS DOUBLE PRECISION)");
        }

        if (double.IsNegativeInfinity(v))
        {
            return builder.Append("CAST('-Infinity' AS DOUBLE PRECISION)");
        }

        if (double.IsNaN(v))
        {
            return builder.Append("CAST('NaN' AS DOUBLE PRECISION)");
        }

        return builder.Append("CAST(").Append(JavaNumberFormat.DoubleToString(v))
            .Append(" AS DOUBLE PRECISION)");
    }

    /// <summary>
    /// Appends the literal of a character string: the text quoted, every quote inside doubled.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the text, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendString(StringBuilder builder, string? value)
        => value is null
            ? AppendNullCast(builder, "VARCHAR")
            : StringUtils.QuoteStringSql(builder, value);

    /// <summary>
    /// Appends the literal of a CHAR, its length of one said out loud: <c>CAST('x' AS CHAR(1))</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the character, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendChar(StringBuilder builder, char? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "CHAR(1)");
        }

        return StringUtils.QuoteStringSql(_ = builder.Append("CAST("), value.GetValueOrDefault().ToString())
            .Append(" AS CHAR(1))");
    }

    /// <summary>
    /// Appends the literal of a VARBINARY: the bytes as hex, <c>X'CAFE'</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the bytes, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendBytes(StringBuilder builder, byte[]? value)
    {
        if (value is null)
        {
            return AppendNullCast(builder, "VARBINARY");
        }

        builder.Append("X'");
        _ = StringUtils.ConvertBytesToHex(builder, value);
        return builder.Append('\'');
    }

    /// <summary>
    /// Appends the literal of a UUID: <c>UUID '...'</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendUuid(StringBuilder builder, Guid? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "UUID");
        }

        // The two halves in the order the engine stores a UUID: sixteen bytes, big-endian, high
        // first, which is how ValueUuid.Get(Guid) reads one.
        Span<byte> bytes = stackalloc byte[16];
        if (!value.GetValueOrDefault().TryWriteBytes(bytes, bigEndian: true, out _))
        {
            throw new ArgumentException("The UUID cannot be written", nameof(value));
        }

        return AppendUuid(
            _ = builder.Append("UUID '"),
            BinaryPrimitives.ReadInt64BigEndian(bytes),
            BinaryPrimitives.ReadInt64BigEndian(bytes[8..])).Append('\'');
    }

    /// <summary>
    /// Appends the literal of a JSON document: <c>JSON '...'</c>.
    /// </summary>
    /// <remarks>
    /// A JSON document is what a <see cref="JsonDocument"/> means here, the way
    /// <c>H2ValueMapper</c> reads one; a plain <see cref="string"/> is a string, not a document.
    /// This is the one literal that builds an engine value on the way: the document has to be
    /// normalized before it is a literal, because the literal of an unnormalized document is a
    /// different document, and the normalization is the engine's own work.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the document, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendJson(StringBuilder builder, JsonDocument? value)
        => value is null
            ? AppendNullCast(builder, "JSON")
            : AppendJson(builder, ValueJson.FromJson(value.RootElement.GetRawText()), HasSql.DefaultSqlFlags);

    /// <summary>
    /// Appends the literal of a JSON document given as an element of one.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the element</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendJson(StringBuilder builder, JsonElement value)
        => AppendJson(builder, ValueJson.FromJson(value.GetRawText()), HasSql.DefaultSqlFlags);

    /// <summary>
    /// Appends the literal of a DATE: <c>DATE '2024-01-02'</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendDate(StringBuilder builder, DateOnly? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "DATE");
        }

        DateOnly d = value.GetValueOrDefault();
        _ = builder.Append("DATE '");
        _ = DateTimeUtils.AppendDate(builder, DateTimeUtils.DateValue(d.Year, d.Month, d.Day));
        return builder.Append('\'');
    }

    /// <summary>
    /// Appends the literal of a TIME: <c>TIME '10:00:00'</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the time of day, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendTime(StringBuilder builder, TimeOnly? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "TIME");
        }

        _ = builder.Append("TIME '");
        _ = DateTimeUtils.AppendTime(builder, value.GetValueOrDefault().ToTimeSpan().Ticks * 100L);
        return builder.Append('\'');
    }

    /// <summary>
    /// Appends the literal of a TIME built from a length of time: the length read as a time of
    /// day, the way <see cref="System.Data.DbType.Time"/> means it.
    /// </summary>
    /// <remarks>
    /// A <see cref="TimeSpan"/> is a length, and the engine has two types for one: the caller
    /// names which, here or by <see cref="AppendInterval"/>. A length of a day or more, or a
    /// negative one, is not a time of day and is refused rather than written.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the time of day, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendTime(StringBuilder builder, TimeSpan? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "TIME");
        }

        long nanos = value.GetValueOrDefault().Ticks * 100L;
        if (nanos < 0 || nanos >= DateTimeUtils.NanosPerDay)
        {
            throw new ArgumentOutOfRangeException(nameof(value), nanos, "TIME is a time of day");
        }

        _ = builder.Append("TIME '");
        _ = DateTimeUtils.AppendTime(builder, nanos);
        return builder.Append('\'');
    }

    /// <summary>
    /// Appends the literal of an INTERVAL DAY TO SECOND built from a length of time.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the length, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendInterval(StringBuilder builder, TimeSpan? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "INTERVAL DAY TO SECOND");
        }

        TimeSpan span = value.GetValueOrDefault();
        BigInteger total = new BigInteger(span.Ticks) * 100;
        bool negative = total.Sign < 0;
        BigInteger absolute = BigInteger.Abs(total);
        return IntervalUtils.AppendInterval(
            builder,
            IntervalQualifier.DayToSecond,
            negative,
            (long)(absolute / IntervalUtils.NanosPerDayBi),
            (long)(absolute % IntervalUtils.NanosPerDayBi));
    }

    /// <summary>
    /// Appends the literal of a TIMESTAMP: <c>TIMESTAMP '2024-01-02 10:00:00'</c>.
    /// </summary>
    /// <remarks>
    /// The <see cref="DateTime.Kind"/> is not asked: TIMESTAMP holds no zone, so the value is
    /// taken as written, the wall-clock reading — the rule <c>H2ValueMapper</c> converts
    /// parameters by.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendTimestamp(StringBuilder builder, DateTime? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "TIMESTAMP");
        }

        DateTime dt = value.GetValueOrDefault();
        _ = builder.Append("TIMESTAMP '");
        _ = DateTimeUtils.AppendDate(builder, DateTimeUtils.DateValue(dt.Year, dt.Month, dt.Day)).Append(' ');
        _ = DateTimeUtils.AppendTime(builder, dt.TimeOfDay.Ticks * 100L);
        return builder.Append('\'');
    }

    /// <summary>
    /// Appends the literal of a TIMESTAMP WITH TIME ZONE, the offset included.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value, or null for NULL</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder AppendTimestampWithTimeZone(StringBuilder builder, DateTimeOffset? value)
    {
        if (!value.HasValue)
        {
            return AppendNullCast(builder, "TIMESTAMP WITH TIME ZONE");
        }

        DateTimeOffset dto = value.GetValueOrDefault();
        _ = builder.Append("TIMESTAMP WITH TIME ZONE '");
        _ = DateTimeUtils.AppendDate(builder, DateTimeUtils.DateValue(dto.Year, dto.Month, dto.Day)).Append(' ');
        _ = DateTimeUtils.AppendTime(builder, dto.TimeOfDay.Ticks * 100L);
        _ = DateTimeUtils.AppendTimeZone(builder, (int)dto.Offset.TotalSeconds);
        return builder.Append('\'');
    }

    // ------------------------------------------------------------------
    // The engine layer: the bodies of the reference's getSQL methods,
    // moved here; the value classes' GetSql overrides delegate to them.
    // ------------------------------------------------------------------

    /// <summary>
    /// Appends the SQL representation of a NULL.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendNull(StringBuilder builder, ValueNull value, int sqlFlags)
        => builder.Append("NULL");

    /// <summary>
    /// Appends the SQL representation of a boolean.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendBoolean(StringBuilder builder, ValueBoolean value, int sqlFlags)
        => builder.Append(value.GetString());

    /// <summary>
    /// Appends the SQL representation of a TINYINT.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendTinyint(StringBuilder builder, ValueTinyint value, int sqlFlags)
    {
        if ((sqlFlags & HasSql.NoCasts) == 0)
        {
            return builder.Append("CAST(").Append(value.GetByte()).Append(" AS TINYINT)");
        }

        return builder.Append(value.GetByte());
    }

    /// <summary>
    /// Appends the SQL representation of a SMALLINT.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendSmallint(StringBuilder builder, ValueSmallint value, int sqlFlags)
    {
        if ((sqlFlags & HasSql.NoCasts) == 0)
        {
            return builder.Append("CAST(").Append(value.GetShort()).Append(" AS SMALLINT)");
        }

        return builder.Append(value.GetShort());
    }

    /// <summary>
    /// Appends the SQL representation of an INTEGER.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendInteger(StringBuilder builder, ValueInteger value, int sqlFlags)
        => builder.Append(value.GetInt().ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Appends the SQL representation of a BIGINT.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendBigint(StringBuilder builder, ValueBigint value, int sqlFlags)
    {
        long v = value.GetLong();
        // A bigint that fits in an int is printed as a cast, so that reading the SQL back does
        // not silently widen the type. The invariant append: the reference's builder.append(long)
        // is locale-independent, .NET's is not (issue #12 A3).
        if ((sqlFlags & HasSql.NoCasts) == 0 && v == (int)v)
        {
            _ = builder.Append("CAST(");
            _ = StringUtils.AppendInvariant(builder, v);
            return builder.Append(" AS BIGINT)");
        }

        return StringUtils.AppendInvariant(builder, v);
    }

    /// <summary>
    /// Appends the SQL representation of a NUMERIC.
    /// </summary>
    /// <remarks>
    /// A whole number that fits in a long prints with a CAST that says how wide it is, because a
    /// bare number would come back as whatever type the parser chose. Anything with a fractional
    /// part, or too wide for a long, prints as it is: the CAST would say nothing the number does
    /// not already say.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendNumeric(StringBuilder builder, ValueNumeric value, int sqlFlags)
    {
        // The reference builds the plain string first and asks it whether it has a point in it.
        // That question has an answer without building it: the plain form of a Number has a point
        // exactly when its scale is positive, since a scale of zero writes the digits and a
        // negative one writes the digits and the zeros the scale stands for. So the common path,
        // which is a whole number that fits a long, costs nothing but the builder.
        BigDecimal number = value.GetBigDecimal();
        if ((sqlFlags & HasSql.NoCasts) == 0
            && number.Scale <= 0
            && number.CompareTo(Value.MaxLongDecimal) <= 0
            && number.CompareTo(Value.MinLongDecimal) >= 0)
        {
            return builder.Append("CAST(").Append(number)
                .Append(" AS NUMERIC(")
                .Append(number.Precision().ToString(CultureInfo.InvariantCulture))
                .Append("))");
        }

        return builder.Append(number.ToPlainString());
    }

    /// <summary>
    /// Appends the SQL representation of a REAL.
    /// </summary>
    /// <remarks>
    /// A float literal is a REAL or a DOUBLE depending on what follows it, so the reference says
    /// which one this is with a CAST unless the caller says it already knows.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendReal(StringBuilder builder, ValueReal value, int sqlFlags)
    {
        if ((sqlFlags & HasSql.NoCasts) == 0)
        {
            return AppendNumber(builder.Append("CAST("), value.GetFloat()).Append(" AS REAL)");
        }

        return AppendNumber(builder, value.GetFloat());
    }

    /// <summary>
    /// Writes the number, and the three values that are not numbers as the quoted words the
    /// reference uses.
    /// </summary>
    /// <remarks>
    /// Infinity and NaN are not numbers and cannot be written as literals, so the reference quotes
    /// them as text and lets the conversion back do the work. A .NET formatter would print
    /// "Infinity" and "NaN" the same way but without the quotes, which would be a syntax error in a
    /// statement that has to run again.
    /// </remarks>
    /// <param name="builder">the builder to append to</param>
    /// <param name="value">the number</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StringBuilder AppendNumber(StringBuilder builder, float value)
    {
        if (float.IsPositiveInfinity(value))
        {
            return builder.Append("'Infinity'");
        }

        if (float.IsNegativeInfinity(value))
        {
            return builder.Append("'-Infinity'");
        }

        if (float.IsNaN(value))
        {
            return builder.Append("'NaN'");
        }

        return builder.Append(JavaNumberFormat.FloatToString(value));
    }

    /// <summary>
    /// Appends the SQL representation of a DOUBLE PRECISION.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendDouble(StringBuilder builder, ValueDouble value, int sqlFlags)
    {
        if ((sqlFlags & HasSql.NoCasts) == 0)
        {
            return AppendValue(_ = builder.Append("CAST("), value.GetDouble()).Append(" AS DOUBLE PRECISION)");
        }

        return AppendValue(builder, value.GetDouble());
    }

    /// <summary>
    /// Appends the value, quoting the three values that are not numbers in SQL.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the number</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StringBuilder AppendValue(StringBuilder builder, double value)
    {
        if (double.IsPositiveInfinity(value))
        {
            return builder.Append("'Infinity'");
        }
        else if (double.IsNegativeInfinity(value))
        {
            return builder.Append("'-Infinity'");
        }
        else if (double.IsNaN(value))
        {
            return builder.Append("'NaN'");
        }

        // The Java spelling, so that 0.0 prints as 0.0 and 1e7 as 1.0E7.
        return builder.Append(JavaNumberFormat.DoubleToString(value));
    }

    /// <summary>
    /// Appends the SQL representation of a DECFLOAT.
    /// </summary>
    /// <remarks>
    /// A DECFLOAT prints inside a CAST, because a bare number comes back as whatever type the
    /// parser chose and 34 digits are not what it would choose. The three values that are not
    /// numbers are printed as quoted words, which is how they are written.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendDecfloat(StringBuilder builder, ValueDecfloat value, int sqlFlags)
    {
        if ((sqlFlags & HasSql.NoCasts) == 0)
        {
            return AppendDecfloatValue(builder.Append("CAST("), value).Append(" AS DECFLOAT)");
        }

        return AppendDecfloatValue(builder, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StringBuilder AppendDecfloatValue(StringBuilder builder, ValueDecfloat value)
    {
        BigDecimal? v = value.GetDecimalValue();
        if (v is not null)
        {
            return builder.Append(v);
        }

        if (value == ValueDecfloat.PositiveInfinity)
        {
            return builder.Append("'Infinity'");
        }

        return value == ValueDecfloat.NegativeInfinity ? builder.Append("'-Infinity'") : builder.Append("'NaN'");
    }

    /// <summary>
    /// Appends the SQL representation of a CHAR.
    /// </summary>
    /// <remarks>
    /// A quoted string is a VARCHAR, so a CHAR says its length out loud in a CAST, and the length
    /// is at least 1: a CHAR of length 0 is spelled CHAR(1). NO_CASTS drops the wrapper for the
    /// caller that already knows the type.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendChar(StringBuilder builder, ValueChar value, int sqlFlags)
    {
        string s = value.GetString();
        if ((sqlFlags & HasSql.NoCasts) == 0)
        {
            int length = s.Length;
            return StringUtils.QuoteStringSql(builder.Append("CAST("), s)
                .Append(" AS CHAR(")
                .Append(length > 0 ? length : 1)
                .Append("))");
        }

        return StringUtils.QuoteStringSql(builder, s);
    }

    /// <summary>
    /// Appends the SQL representation of a VARCHAR: the text quoted, every quote inside doubled.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendVarchar(StringBuilder builder, ValueVarchar value, int sqlFlags)
        => StringUtils.QuoteStringSql(builder, value.GetString());

    /// <summary>
    /// Appends the SQL representation of a VARCHAR_IGNORECASE.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendVarcharIgnoreCase(
        StringBuilder builder, ValueVarcharIgnoreCase value, int sqlFlags)
    {
        string s = value.GetString();
        if ((sqlFlags & HasSql.NoCasts) == 0)
        {
            return _ = StringUtils
                .QuoteStringSql(_ = builder.Append("CAST("), s)
                .Append(" AS VARCHAR_IGNORECASE(")
                .Append(s.Length.ToString(CultureInfo.InvariantCulture))
                .Append("))");
        }

        return StringUtils.QuoteStringSql(builder, s);
    }

    /// <summary>
    /// Appends the SQL representation of a byte array value: the bytes as hex, <c>X'...'</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendBytes(StringBuilder builder, ValueBytesBase value, int sqlFlags)
    {
        builder.Append("X'");
        _ = StringUtils.ConvertBytesToHex(builder, value.GetBytesNoCopy()!);
        return builder.Append('\'');
    }

    /// <summary>
    /// Appends the SQL representation of a BINARY.
    /// </summary>
    /// <remarks>
    /// A hex literal is a VARBINARY, so a BINARY has to say what it is: the reference wraps it
    /// in a CAST, and the length it writes is at least 1 because a BINARY of length 0 is spelled
    /// that way rather than as BINARY(0). A caller that sets NO_CASTS gets the bare literal, which
    /// is what a store that already knows the type asks for.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendBinary(StringBuilder builder, ValueBinary value, int sqlFlags)
    {
        if ((sqlFlags & HasSql.NoCasts) == 0)
        {
            int length = value.GetBytesNoCopy()!.Length;
            return AppendBytes(builder.Append("CAST("), value, sqlFlags)
                .Append(" AS BINARY(")
                .Append(length > 0 ? length : 1)
                .Append("))");
        }

        return AppendBytes(builder, value, sqlFlags);
    }

    /// <summary>
    /// Appends the SQL representation of a UUID: <c>UUID '...'</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendUuid(StringBuilder builder, ValueUuid value, int sqlFlags)
        => AppendUuid(_ = builder.Append("UUID '"), value.GetHigh(), value.GetLow()).Append('\'');

    /// <summary>
    /// Appends the UUID text: the two halves in hex, in five dash-separated groups.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="high">the most significant bits</param>
    /// <param name="low">the least significant bits</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendUuid(StringBuilder builder, long high, long low)
    {
        _ = Hex.AppendHex(builder, high >> 32, 4).Append('-');
        _ = Hex.AppendHex(builder, high >> 16, 2).Append('-');
        _ = Hex.AppendHex(builder, high, 2).Append('-');
        _ = Hex.AppendHex(builder, low >> 48, 2).Append('-');
        return Hex.AppendHex(builder, low, 6);
    }

    /// <summary>
    /// Appends the SQL representation of a DATE: <c>DATE '...'</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendDate(StringBuilder builder, ValueDate value, int sqlFlags)
        => DateTimeUtils.AppendDate(_ = builder.Append("DATE '"), value.GetDateValue()).Append('\'');

    /// <summary>
    /// Appends the SQL representation of a TIME: <c>TIME '...'</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendTime(StringBuilder builder, ValueTime value, int sqlFlags)
        => DateTimeUtils.AppendTime(_ = builder.Append("TIME '"), value.GetNanos()).Append('\'');

    /// <summary>
    /// Appends the SQL representation of a TIME WITH TIME ZONE: <c>TIME WITH TIME ZONE '...'</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendTimeWithTimeZone(
        StringBuilder builder, ValueTimeTimeZone value, int sqlFlags)
        => AppendTimeWithTimeZone(
            _ = builder.Append("TIME WITH TIME ZONE '"),
            value.GetNanos(),
            value.GetTimeZoneOffsetSeconds()).Append('\'');

    /// <summary>
    /// Appends a time of day and its offset from Greenwich.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="nanos">the nanoseconds since midnight</param>
    /// <param name="timeZoneOffsetSeconds">the offset from Greenwich in seconds</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendTimeWithTimeZone(
        StringBuilder builder, long nanos, int timeZoneOffsetSeconds)
        => DateTimeUtils.AppendTimeZone(DateTimeUtils.AppendTime(builder, nanos), timeZoneOffsetSeconds);

    /// <summary>
    /// Appends the SQL representation of a TIMESTAMP: <c>TIMESTAMP '...'</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendTimestamp(StringBuilder builder, ValueTimestamp value, int sqlFlags)
        => AppendTimestamp(
            _ = builder.Append("TIMESTAMP '"),
            value.GetDateValue(),
            value.GetTimeNanos(),
            iso: false).Append('\'');

    /// <summary>
    /// Appends a date and a time of day, separated the SQL way or the ISO way.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="dateValue">the date value, a bit field with bits for the year, month and day</param>
    /// <param name="timeNanos">the nanoseconds since midnight</param>
    /// <param name="iso">whether the separator is the ISO T instead of a space</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendTimestamp(
        StringBuilder builder, long dateValue, long timeNanos, bool iso)
    {
        DateTimeUtils.AppendDate(builder, dateValue).Append(iso ? 'T' : ' ');
        return DateTimeUtils.AppendTime(builder, timeNanos);
    }

    /// <summary>
    /// Appends the SQL representation of a TIMESTAMP WITH TIME ZONE:
    /// <c>TIMESTAMP WITH TIME ZONE '...'</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendTimestampWithTimeZone(
        StringBuilder builder, ValueTimestampTimeZone value, int sqlFlags)
        => AppendTimestampWithTimeZone(
            _ = builder.Append("TIMESTAMP WITH TIME ZONE '"),
            value.GetDateValue(),
            value.GetTimeNanos(),
            value.GetTimeZoneOffsetSeconds(),
            iso: false).Append('\'');

    /// <summary>
    /// Appends a date, a time of day and the offset from Greenwich.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="dateValue">the date value, a bit field with bits for the year, month and day</param>
    /// <param name="timeNanos">the nanoseconds since midnight</param>
    /// <param name="timeZoneOffsetSeconds">the offset from Greenwich in seconds</param>
    /// <param name="iso">whether the separator is the ISO T instead of a space</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendTimestampWithTimeZone(
        StringBuilder builder, long dateValue, long timeNanos, int timeZoneOffsetSeconds, bool iso)
    {
        DateTimeUtils.AppendDate(builder, dateValue).Append(iso ? 'T' : ' ');
        DateTimeUtils.AppendTime(builder, timeNanos);
        return DateTimeUtils.AppendTimeZone(builder, timeZoneOffsetSeconds);
    }

    /// <summary>
    /// Appends the SQL representation of an interval:
    /// <c>INTERVAL '1 02:03:04' DAY TO SECOND</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendInterval(StringBuilder builder, ValueInterval value, int sqlFlags)
        => IntervalUtils.AppendInterval(
            builder, value.GetQualifier(), value.IsNegative(), value.GetLeading(), value.GetRemaining());

    /// <summary>
    /// Appends the SQL representation of a JSON document: <c>JSON '...'</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendJson(StringBuilder builder, ValueJson value, int sqlFlags)
    {
        // The ASCII rendering, and not the stored bytes: a document that stored non-ASCII as itself
        // has to be printed as escapes, or the SQL literal it sits in would depend on the encoding
        // of whoever runs it.
        string s = JsonBytesSource.Parse(value.GetBytesNoCopy()!, new JsonStringTarget(true));
        return builder.Append("JSON '").Append(s).Append('\'');
    }

    /// <summary>
    /// Appends the SQL representation of an ENUM.
    /// </summary>
    /// <remarks>
    /// Without the flag the value is written as a cast of its label to its own type, which
    /// is how a reader can tell an enum from a string that happens to look like one.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendEnum(StringBuilder builder, ValueEnum value, int sqlFlags)
    {
        if ((sqlFlags & HasSql.NoCasts) == 0)
        {
            _ = StringUtils.QuoteStringSql(_ = builder.Append("CAST("), value.GetString()).Append(" AS ");
            return value.GetEnumerators().GetEnumType().GetSql(builder, sqlFlags).Append(')');
        }

        return StringUtils.QuoteStringSql(builder, value.GetString());
    }

    /// <summary>
    /// Appends the SQL representation of a GEOMETRY.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    internal static StringBuilder AppendGeometry(StringBuilder builder, ValueGeometry value, int sqlFlags)
    {
        _ = builder.Append("GEOMETRY ");
        if ((sqlFlags & HasSql.AddPlanInformation) != 0)
        {
            EWKBUtils.ParseEWKB(
                value.GetBytesNoCopy()!,
                new EWKTUtils.EWKTTarget(_ = builder.Append('\''), value.GetDimensionSystem()));
            _ = builder.Append('\'');
        }
        else
        {
            _ = AppendBytes(builder, value, HasSql.DefaultSqlFlags);
        }

        return builder;
    }

    /// <summary>
    /// Appends the SQL representation of a BLOB.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    internal static StringBuilder AppendBlob(StringBuilder builder, ValueBlob value, int sqlFlags)
    {
        if ((sqlFlags & HasSql.ReplaceLobsForTrace) != 0
            && (value.LobData is not LobDataInMemory || value.LobOctetLength > SysProperties.MaxTraceDataLength))
        {
            // The data itself is not put into a trace: what goes in is how long it is and where it
            // is, which is enough to follow the statement and small enough to read.
            _ = builder.Append("CAST(REPEAT(CHAR(0), ").Append(value.LobOctetLength).Append(") AS BINARY VARYING");
            value.FormatLobDataComment(builder);
        }
        else if ((sqlFlags & (HasSql.ReplaceLobsForTrace | HasSql.NoCasts)) == 0)
        {
            _ = Hex.AppendBytesHex(_ = builder.Append("CAST(X'"), value.GetBytesNoCopy()!)
                .Append("' AS BINARY LARGE OBJECT(")
                .Append(value.LobOctetLength)
                .Append("))");
        }
        else
        {
            _ = Hex.AppendBytesHex(_ = builder.Append("X'"), value.GetBytesNoCopy()!).Append('\'');
        }

        return builder;
    }

    /// <summary>
    /// Appends the SQL representation of a CLOB.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    internal static StringBuilder AppendClob(StringBuilder builder, ValueClob value, int sqlFlags)
    {
        if ((sqlFlags & HasSql.ReplaceLobsForTrace) != 0
            && (value.LobData is not LobDataInMemory || value.LobCharLength > SysProperties.MaxTraceDataLength))
        {
            // The text is not put into a trace: how long it is and where it is will do.
            _ = builder.Append("SPACE(").Append(value.LobCharLength);
            value.FormatLobDataComment(builder);
        }
        else if ((sqlFlags & (HasSql.ReplaceLobsForTrace | HasSql.NoCasts)) == 0)
        {
            _ = StringUtils.QuoteStringSql(_ = builder.Append("CAST("), value.GetString())
                .Append(" AS CHARACTER LARGE OBJECT(")
                .Append(value.LobCharLength)
                .Append("))");
        }
        else
        {
            _ = StringUtils.QuoteStringSql(builder, value.GetString());
        }

        return builder;
    }

    /// <summary>
    /// Appends the SQL representation of an ARRAY: <c>ARRAY [...]</c>, the elements as their own
    /// literals.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    internal static StringBuilder AppendArray(StringBuilder builder, ValueArray value, int sqlFlags)
    {
        _ = builder.Append("ARRAY [");
        Value[] values = value.GetList();
        int length = values.Length;
        for (int i = 0; i < length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            values[i].GetSql(builder, sqlFlags);
        }

        return builder.Append(']');
    }

    /// <summary>
    /// Appends the SQL representation of a ROW: <c>ROW (...)</c>, the fields as their own
    /// literals.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    internal static StringBuilder AppendRow(StringBuilder builder, ValueRow value, int sqlFlags)
    {
        _ = builder.Append("ROW (");
        Value[] values = value.GetList();
        int length = values.Length;
        for (int i = 0; i < length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            values[i].GetSql(builder, sqlFlags);
        }

        return builder.Append(')');
    }

    /// <summary>
    /// Appends the SQL representation of a JAVA_OBJECT.
    /// </summary>
    /// <remarks>
    /// Without the flag the value is written as a cast, because a bare run of bytes would
    /// read as a string; with the flag the raw rendering is what the reader asked for.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the value</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the same builder</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static StringBuilder AppendJavaObject(StringBuilder builder, ValueJavaObject value, int sqlFlags)
    {
        if ((sqlFlags & HasSql.NoCasts) == 0)
        {
            return AppendBytes(
                _ = builder.Append("CAST("), value, HasSql.DefaultSqlFlags).Append(" AS JAVA_OBJECT)");
        }

        return AppendBytes(builder, value, HasSql.DefaultSqlFlags);
    }
}
