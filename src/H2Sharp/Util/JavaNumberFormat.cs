/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Java's number formatting.
 *
 * The reference calls Double.toString and Float.toString and nothing else, and those two are
 * specified down to the last character: the shortest decimal that reads back as the same
 * value, always with a point, and scientific notation outside [1e-3, 1e7). The text reaches
 * stored SQL, error messages and index metadata, so it cannot be approximated.
 *
 * .NET has the shortest round-trip digits for most values but not for all of them: it prints
 * 1e7 as 10000000 where Java prints 1.0E7. So the shortest digit string has to be found by
 * asking for one digit at a time and checking whether it reads back.
 *
 * The search is written to allocate nothing per step: the candidate goes into a stack buffer
 * and is read back from a span. An earlier version built a format string and a substring per
 * step, which measured at about a kilobyte per formatted number; this allocates the result and
 * nothing else. The two methods are near copies of each other on purpose: a shared generic
 * version would need a delegate per step to format into the span, which costs more than it
 * saves in a loop this short.
 *
 * There is no counterpart class in the reference, and that is deliberate: the reference needs
 * no helper because the runtime provides one. The corpus is in
 * tests/JavaNumberFormatOracleTests.cs.
 */
using System;
using System.Globalization;

namespace H2Sharp.Util;

/// <summary>
/// Renders numbers the way Java does.
/// </summary>
public static class JavaNumberFormat
{
    /// <summary>
    /// The lower bound of the plain form: 1e-3 prints as 0.001, and below that as 1E-4.
    /// </summary>
    private const double PlainLowerBound = 1e-3;

    /// <summary>
    /// The upper bound of the plain form, exclusive: 9999999.0 prints plain, 1E7 does not.
    /// </summary>
    private const double PlainUpperBound = 1e7;

    /// <summary>
    /// Enough for "d.dddE+ddd" with the widest exponent a double can print, plus the point
    /// and padding of the plain form.
    /// </summary>
    private const int BufferLength = 48;

    /// <summary>
    /// The E format for each digit count, cached so the search does not build one per step.
    /// "E0" is one significant digit, "E16" is seventeen, which is every digit a double needs.
    /// </summary>
    private static readonly string[] DoubleFormats = BuildFormats(17);

    /// <summary>
    /// The same for a float, which needs at most nine digits.
    /// </summary>
    private static readonly string[] FloatFormats = BuildFormats(9);

    /// <summary>
    /// The significant digits of a positive number, and the exponent they belong to.
    /// </summary>
    /// <remarks>
    /// The value is <c>d[0].d[1..] * 10^Exponent</c>, which is the shape scientific notation
    /// prints and the shape the plain form is derived from. Carrying the exponent rather than
    /// a decimal point position is what makes trailing zeros work: 100 is the digit 1 with
    /// exponent 2, not the digits 100 with exponent 0.
    /// </remarks>
    /// <param name="Text">the significant digits, no point and no sign</param>
    /// <param name="Exponent">the power of ten the digits are scaled by</param>
    private readonly record struct Digits(string Text, int Exponent);

    /// <summary>
    /// Formats a double the way <c>Double.toString</c> does.
    /// </summary>
    /// <param name="value">the value</param>
    /// <returns>the Java text</returns>
    public static string DoubleToString(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "Infinity";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-Infinity";
        }

        if (value == 0.0)
        {
            // Java keeps the sign of negative zero, and IsNegative reads the sign bit, which
            // is what the sign bit is for.
            return double.IsNegative(value) ? "-0.0" : "0.0";
        }

        bool negative = value < 0;
        double magnitude = negative ? -value : value;

        Span<char> candidate = stackalloc char[BufferLength];
        foreach (string format in DoubleFormats)
        {
            _ = magnitude.TryFormat(candidate, out int written, format, CultureInfo.InvariantCulture);
            ReadOnlySpan<char> text = candidate[..written];
            if (double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) == magnitude)
            {
                return Render(FromScientific(text), magnitude, negative);
            }
        }

        throw new InvalidOperationException(
            $"No digit count reproduces {value.ToString(CultureInfo.InvariantCulture)}, which "
            + "should be impossible for a finite double.");
    }

    /// <summary>
    /// Formats a float the way <c>Float.toString</c> does.
    /// </summary>
    /// <remarks>
    /// The rules are the same, but the digits are the shortest that read back as the same
    /// <em>float</em>. Taking the double digits would print 0.10000000149011612 for the float
    /// 0.1f, which is what the float 0.1 prints as.
    /// </remarks>
    /// <param name="value">the value</param>
    /// <returns>the Java text</returns>
    public static string FloatToString(float value)
    {
        if (float.IsNaN(value))
        {
            return "NaN";
        }

        if (float.IsPositiveInfinity(value))
        {
            return "Infinity";
        }

        if (float.IsNegativeInfinity(value))
        {
            return "-Infinity";
        }

        if (value == 0.0f)
        {
            return float.IsNegative(value) ? "-0.0" : "0.0";
        }

        bool negative = value < 0;
        float magnitude = negative ? -value : value;

        Span<char> candidate = stackalloc char[BufferLength];
        foreach (string format in FloatFormats)
        {
            _ = magnitude.TryFormat(candidate, out int written, format, CultureInfo.InvariantCulture);
            ReadOnlySpan<char> text = candidate[..written];
            if (float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) == magnitude)
            {
                return Render(FromScientific(text), magnitude, negative);
            }
        }

        throw new InvalidOperationException(
            $"No digit count reproduces {value.ToString(CultureInfo.InvariantCulture)}, which "
            + "should be impossible for a finite float.");
    }

    /// <summary>
    /// Lays the digits out in Java's shape and returns the text.
    /// </summary>
    /// <remarks>
    /// One allocation, the result string. The plain form needs no more room than the digits
    /// plus the point, the zeros and the sign, and the scientific form needs the digits, the
    /// point, the E and the exponent, all of which fit the buffer with room to spare.
    /// </remarks>
    /// <param name="digits">the digits and the exponent</param>
    /// <param name="magnitude">the magnitude, only to decide which of the two forms applies</param>
    /// <param name="negative">whether to put a minus in front</param>
    /// <returns>the text</returns>
    private static string Render(Digits digits, double magnitude, bool negative)
    {
        string significant = digits.Text;
        int exponent = digits.Exponent;
        int integerDigits = exponent + 1;
        bool plain = magnitude >= PlainLowerBound && magnitude < PlainUpperBound;

        // The length is computed rather than bounded, because string.Create takes the length
        // of the result and not the length of the span it hands over: a span filled to
        // seven of eight chars and a string of eight would put a NUL at the end of every
        // number, which is the sort of thing a functional test catches once.
        int length;
        if (plain)
        {
            length = integerDigits <= 0
                ? 2 + (-integerDigits) + significant.Length
                : integerDigits >= significant.Length
                    ? integerDigits + 2
                    : significant.Length + 1;
        }
        else
        {
            length = 3 + Math.Max(significant.Length - 1, 1) + ExponentLength(exponent);
            if (exponent < 0)
            {
                length++;
            }
        }

        if (negative)
        {
            length++;
        }

        return string.Create(
            length,
            (significant, integerDigits, exponent, negative, plain),
            static (span, state) =>
            {
                string digits = state.significant;
                int integerDigits = state.integerDigits;
                int exponent = state.exponent;
                int at = 0;
                if (state.negative)
                {
                    span[at++] = '-';
                }

                if (state.plain)
                {
                    if (integerDigits <= 0)
                    {
                        // 0.00digits: the point sits before the first significant digit.
                        span[at++] = '0';
                        span[at++] = '.';
                        for (int i = 0; i < -integerDigits; i++)
                        {
                            span[at++] = '0';
                        }

                        digits.AsSpan().CopyTo(span[at..]);
                        at += digits.Length;
                    }
                    else if (integerDigits >= digits.Length)
                    {
                        // digits000.0: the point sits after the last significant digit.
                        digits.AsSpan().CopyTo(span[at..]);
                        at += digits.Length;
                        for (int i = digits.Length; i < integerDigits; i++)
                        {
                            span[at++] = '0';
                        }

                        span[at++] = '.';
                        span[at++] = '0';
                    }
                    else
                    {
                        digits.AsSpan(0, integerDigits).CopyTo(span[at..]);
                        at += integerDigits;
                        span[at++] = '.';
                        digits.AsSpan(integerDigits).CopyTo(span[at..]);
                        at += digits.Length - integerDigits;
                    }
                }
                else
                {
                    // Scientific: one digit before the point, at least one after, then E and
                    // the exponent. Java writes a minus for a negative exponent and nothing
                    // for a positive one, so 1.0E7 and 9.999999E-4.
                    span[at++] = digits[0];
                    span[at++] = '.';
                    if (digits.Length > 1)
                    {
                        digits.AsSpan(1).CopyTo(span[at..]);
                        at += digits.Length - 1;
                    }
                    else
                    {
                        span[at++] = '0';
                    }

                    span[at++] = 'E';
                    if (exponent < 0)
                    {
                        span[at++] = '-';
                    }

                    _ = ((uint)Math.Abs(exponent)).TryFormat(
                        span[at..],
                        out int written,
                        provider: CultureInfo.InvariantCulture);
                    at += written;
                }

                span[at..].Clear();
            });
    }

    /// <summary>
    /// Reads an <c>E</c> shaped rendering into digits and an exponent.
    /// </summary>
    /// <remarks>
    /// Only the shape this class produces itself is handled: one digit before the point, an
    /// optional point, and an exponent. Nothing else has to be understood, which is why there
    /// is no general number parser here.
    /// </remarks>
    /// <param name="text">the text</param>
    /// <returns>the digits and the exponent</returns>
    private static Digits FromScientific(ReadOnlySpan<char> text)
    {
        int e = text.IndexOf('E');
        int exponent = int.Parse(text[(e + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture);
        ReadOnlySpan<char> mantissa = text[..e];

        int point = mantissa.IndexOf('.');
        string significant = point < 0
            ? mantissa.ToString()
            : string.Concat(mantissa[..point], mantissa[(point + 1)..]);

        // The rendering had exactly one digit in front of the point, so the exponent it
        // carries is already the exponent of the first significant digit.
        return new Digits(significant, exponent);
    }

    /// <summary>
    /// How many characters the exponent of the scientific form takes, without its sign.
    /// </summary>
    /// <param name="exponent">the exponent</param>
    /// <returns>the number of characters</returns>
    private static int ExponentLength(int exponent)
        => (uint)Math.Abs(exponent) < 10 ? 1
            : (uint)Math.Abs(exponent) < 100 ? 2
            : (uint)Math.Abs(exponent) < 1000 ? 3
            : 4;

    private static string[] BuildFormats(int maxDigits)
    {
        string[] formats = new string[maxDigits];
        for (int i = 0; i < maxDigits; i++)
        {
            formats[i] = "E" + i.ToString(CultureInfo.InvariantCulture);
        }

        return formats;
    }
}
