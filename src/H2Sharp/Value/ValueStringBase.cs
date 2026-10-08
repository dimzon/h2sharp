/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueStringBase.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Globalization;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;

namespace H2Sharp.Values;

/// <summary>
/// Base implementation of String based data types.
/// </summary>
public abstract class ValueStringBase : Value
{
    /// <summary>
    /// The value.
    /// </summary>
    protected string value;

    private TypeInfo? type;

    /// <summary>
    /// Creates a string value.
    /// </summary>
    /// <param name="v">the string</param>
    /// <exception cref="H2SharpException">if the string is longer than a value may be</exception>
    protected ValueStringBase(string v)
    {
        int length = v.Length;
        if (length > Constants.MaxStringLength)
        {
            throw DbException.GetValueTooLongException(
                Value.GetTypeName(GetValueType()), v, length);
        }

        value = v;
    }

    /// <inheritdoc/>
    public sealed override TypeInfo GetTypeInfo()
    {
        TypeInfo? cached = type;
        if (cached is not null)
        {
            return cached;
        }

        int length = value.Length;
        type = cached = new TypeInfo(GetValueType(), length, 0, null);
        return cached;
    }

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value v, CompareMode mode, CastDataProvider? provider)
        => mode.CompareString(value, ((ValueStringBase)v).value, false);

    /// <inheritdoc/>
    /// <remarks>
    /// The reference mixes in <c>getClass().hashCode()</c>, which in Java is an identity hash:
    /// a different number on every run, in the reference itself. So a string's hash is not a
    /// reproducible value on either side, and it is deliberately not pinned by the differential
    /// test. Equality, which is what a hash has to agree with, is compared.
    /// </remarks>
    public override int GetHashCode()
    {
        unchecked
        {
            // The class part stands in for the reference's identity hash, which is the only
            // honest thing to do with it: any number here would differ from the reference's, and
            // none would be stable either.
            int classHash = GetType().GetHashCode();
            int stringHash = 0;
            foreach (char c in value)
            {
                stringHash = (31 * stringHash) + c;
            }

            return classHash ^ stringHash;
        }
    }

    /// <inheritdoc/>
    public sealed override string GetString() => value;

    /// <inheritdoc/>
    public sealed override byte[]? GetBytes() => Encoding.UTF8.GetBytes(value);

    /// <inheritdoc/>
    /// <remarks>
    /// The reference builds a <c>BigDecimal</c> from the trimmed text and asks for its sign.
    /// A .NET <see cref="decimal"/> covers plain decimals exactly but not exponent notation, and
    /// a <see cref="double"/> covers the notation but loses values below its range, where it
    /// would answer zero for a nonzero number. So the text is validated by parsing and the sign
    /// is then read off the digits, which is exact in both cases: the sign of a decimal is
    /// whether any digit is nonzero.
    /// </remarks>
    public sealed override bool GetBoolean()
    {
        string s = value.Trim();
        if (s.Equals("true", StringComparison.OrdinalIgnoreCase)
            || s.Equals("t", StringComparison.OrdinalIgnoreCase)
            || s.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || s.Equals("y", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        else if (s.Equals("false", StringComparison.OrdinalIgnoreCase)
            || s.Equals("f", StringComparison.OrdinalIgnoreCase)
            || s.Equals("no", StringComparison.OrdinalIgnoreCase)
            || s.Equals("n", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsJavaNumber(s))
        {
            GetDataConversionError(Value.Boolean);
            return false;
        }

        return HasNonZeroDigit(s);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Parsed as <see cref="sbyte"/> and not as <see cref="byte"/>, because .NET's
    /// <c>byte.TryParse("128")</c> answers true and hands back 128 while <c>sbyte.TryParse</c>
    /// refuses it, and <c>Byte.parseByte</c> in the reference refuses it. The signed type has
    /// exactly the range Java's signed byte has, so it is the one to go through.
    /// </remarks>
    public sealed override sbyte GetByte()
        => Parse(value, static (string s, NumberStyles st, IFormatProvider p, out sbyte r) => sbyte.TryParse(s, st, p, out r));

    /// <inheritdoc/>
    public sealed override short GetShort()
        => Parse(value, static (string s, NumberStyles st, IFormatProvider p, out short r) => short.TryParse(s, st, p, out r));

    /// <inheritdoc/>
    public sealed override int GetInt() => Parse(value, static (string s, NumberStyles st, IFormatProvider p, out int r) => int.TryParse(s, st, p, out r));

    /// <inheritdoc/>
    public sealed override long GetLong() => Parse(value, static (string s, NumberStyles st, IFormatProvider p, out long r) => long.TryParse(s, st, p, out r));

    /// <inheritdoc/>
    // NumberStyles.Float rather than Integer, because a float may carry a point and an exponent
    // and Java's parser accepts both. Float is the style that allows exactly those two and not
    // a thousands separator, which Java also refuses. The type suffix is Java's too.
    public sealed override float GetFloat()
        => Parse(
            value,
            static (string s, NumberStyles st, IFormatProvider p, out float r)
                => float.TryParse(StripTypeSuffix(s), st, p, out r),
            NumberStyles.Float);

    /// <summary>
    /// Removes the optional trailing type suffix that Java's numeric parsers accept.
    /// </summary>
    /// <remarks>
    /// Only a single trailing <c>d</c>, <c>D</c>, <c>f</c> or <c>F</c>, and only when what
    /// remains is not empty. A bare <c>d</c> stays as it is so that it is still refused, which
    /// is what the reference does with it.
    /// </remarks>
    private static string StripTypeSuffix(string text)
    {
        int last = text.Length - 1;
        if (last <= 0)
        {
            return text;
        }

        char c = text[last];
        if (c is 'd' or 'D' or 'f' or 'F')
        {
            return text[..last];
        }

        return text;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Java's <c>Double.parseDouble</c> accepts an optional trailing type suffix, so
    /// <c>1d</c> and <c>1f</c> are the numbers one. .NET's parser refuses both, so the suffix
    /// is taken off here before parsing. What is still missing is Java's hexadecimal float form,
    /// <c>0x1p3</c>, which needs a hexadecimal significand and a binary exponent; nothing in the
    /// corpus reaches it and guessing at it would put the difference somewhere it is not
    /// written down.
    /// </remarks>
    public sealed override double GetDouble()
        => Parse(
            value,
            static (string s, NumberStyles st, IFormatProvider p, out double r)
                => double.TryParse(StripTypeSuffix(s), st, p, out r),
            NumberStyles.Float);

    /// <inheritdoc/>
    public sealed override int GetMemory() =>
        /*
         * Java 11 with -XX:-UseCompressedOops
         * Empty string: 88 bytes
         * 1 to 4 UTF-16 chars: 96 bytes
         */
        (value.Length * 2) + 94;

    /// <inheritdoc/>
    public override bool Equals(object? other)
        => other is ValueStringBase v
            && v.GetType() == GetType()
            && string.Equals(value, v.value, StringComparison.Ordinal);

    /// <summary>
    /// Whether the text is a number Java's parser would accept.
    /// </summary>
    /// <remarks>
    /// Only the forms that matter for the sign test: an optional sign, digits with an optional
    /// decimal point, and an optional exponent. Anything else is rejected, which is what turns
    /// "not a number" into a conversion error rather than into false.
    /// <para>
    /// The three special words are rejected up front even though .NET's parser accepts them,
    /// because the reference asks a <c>BigDecimal</c>, and <c>new BigDecimal("Infinity")</c> is a
    /// NumberFormatException. So <c>getBoolean()</c> on the string <c>Infinity</c> is a conversion
    /// error there and would be <c>false</c> here if the platform parser were asked first.
    /// </para>
    /// </remarks>
    private static bool IsJavaNumber(string s)
    {
        if (s.Equals("NaN", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Infinity", StringComparison.OrdinalIgnoreCase)
            || s.Equals("-Infinity", StringComparison.OrdinalIgnoreCase)
            || s.Equals("\u221e", StringComparison.Ordinal)
            || s.Equals("-\u221e", StringComparison.Ordinal))
        {
            return false;
        }

        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
            || double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return true;
        }

        // Exponent notation with more digits than a decimal or a double can hold, such as
        // 1e-400: the reference's BigDecimal takes it, and its sign is one.
        int at = 0;
        if (at < s.Length && s[at] is '+' or '-')
        {
            at++;
        }

        bool digits = false;
        while (at < s.Length && char.IsAsciiDigit(s[at]))
        {
            at++;
            digits = true;
        }

        if (at < s.Length && s[at] == '.')
        {
            at++;
            while (at < s.Length && char.IsAsciiDigit(s[at]))
            {
                at++;
                digits = true;
            }
        }

        if (!digits)
        {
            return false;
        }

        if (at < s.Length && s[at] is 'e' or 'E')
        {
            at++;
            if (at < s.Length && s[at] is '+' or '-')
            {
                at++;
            }

            bool exponentDigits = false;
            while (at < s.Length && char.IsAsciiDigit(s[at]))
            {
                at++;
                exponentDigits = true;
            }

            if (!exponentDigits)
            {
                return false;
            }
        }

        return at == s.Length;
    }

    /// <summary>
    /// Whether any digit of the text is nonzero, which is the sign of the number it spells.
    /// </summary>
    private static bool HasNonZeroDigit(string s)
    {
        foreach (char c in s)
        {
            if (char.IsAsciiDigit(c) && c != '0')
            {
                return true;
            }
        }

        return false;
    }

    private static T Parse<T>(
        string text,
        TryParse<T> tryParse,
        NumberStyles style = NumberStyles.Integer)
    {
        try
        {
            if (!tryParse(text.Trim(), style, CultureInfo.InvariantCulture, out T result))
            {
                throw new FormatException(text);
            }

            return result;
        }
        catch (FormatException e)
        {
            throw DbException.Get(
                ErrorCode.DataConversionError1, e, text);
        }
    }

    /// <summary>
    /// A parser that reports whether it succeeded, so that a refusal can carry the reference's
    /// own error shape: the conversion error with the offending text.
    /// </summary>
    private delegate bool TryParse<T>(string text, NumberStyles style, IFormatProvider provider, out T result);
}
