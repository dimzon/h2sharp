/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * An arbitrary precision decimal, with the answers the platform's own decimal does not give.
 *
 * H2 2.5 uses java.math.BigDecimal directly, so this is a port of the JDK's class rather than of
 * something in the reference tree. It is here because .NET's decimal is not a substitute: it is
 * limited to 28 or 29 significant digits and to a scale of 0 to 28, while NUMERIC(38, 10) and
 * DECFLOAT(34, 7) have to round-trip values that decimal cannot hold, and the rounding rules and
 * the printed form are the JDK's, not .NET's.
 *
 * The value is the JDK's shape: an unscaled value with a scale, where the value is
 * unscaled * 10^-scale. Everything about the arithmetic follows from that.
 *
 * The part that has to be written down, because it is the part that is easy to get subtly wrong
 * and impossible to guess, is how a value is printed. The JDK's rule, as measured on the JVM
 * rather than assumed, is:
 *
 * - A scale of 0 prints as the digits themselves, however many there are.
 * - A negative scale prints in scientific notation, because the value then has zeros that are not
 *   in the unscaled value: 1E+7 prints as 1E+7 and not as 10000000.
 * - A positive scale prints in plain notation while the first significant digit's exponent, the
 *   adjusted exponent, is at least -6, and in scientific notation below it. So 0.000001 prints
 *   as itself and 0.0000001 prints as 1E-7, and the difference between the two is a single step
 *   of that rule rather than anything about the value.
 * - In scientific notation the digits that are not part of the exponent are all written out, and
 *   a point is placed where the scale says it falls: 1234567890 with a scale of -1 prints as
 *   1.234567890E+10.
 * - A plain value with more leading zeros after the point than it has digits gets them: the value
 *   0.0012 prints as 0.0012, and the unscaled value 12 with a scale of 7 is 0.0000012.
 */
using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace H2Sharp.Util;

/// <summary>
/// An immutable arbitrary precision decimal, ported from the JDK's <c>java.math.BigDecimal</c>.
/// </summary>
public sealed class BigDecimal : IComparable<BigDecimal>, IEquatable<BigDecimal>
{
    /// <summary>
    /// The unscaled value, which together with <see cref="scale"/> is the whole number.
    /// </summary>
    private readonly BigInteger unscaledValue;

    /// <summary>
    /// The number of digits to the right of the decimal point, negative when there are zeros to
    /// the left of it that are not in the unscaled value.
    /// </summary>
    private readonly int scale;

    /// <summary>
    /// Zero.
    /// </summary>
    public static readonly BigDecimal Zero = new(BigInteger.Zero, 0);

    /// <summary>
    /// One.
    /// </summary>
    public static readonly BigDecimal One = new(BigInteger.One, 0);

    /// <summary>
    /// Ten.
    /// </summary>
    public static readonly BigDecimal TenValue = new(BigInteger.One + BigInteger.One + BigInteger.One + BigInteger.One + BigInteger.One + BigInteger.One + BigInteger.One + BigInteger.One + BigInteger.One + BigInteger.One, 0);

    private BigDecimal(BigInteger unscaled, int scale)
    {
        unscaledValue = unscaled;
        this.scale = scale;
    }

    /// <summary>
    /// The unscaled value, which is not the number itself: the number is this times 10 to the
    /// minus <see cref="Scale"/>.
    /// </summary>
    public BigInteger UnscaledValue => unscaledValue;

    /// <summary>
    /// The number of digits to the right of the decimal point.
    /// </summary>
    public int Scale => scale;

    /// <summary>
    /// Reads a decimal from text.
    /// </summary>
    /// <remarks>
    /// The JDK accepts an optional sign, digits with an optional point and an optional exponent,
    /// and nothing else: no surrounding spaces, no group separators, no <c>Infinity</c> and no
    /// <c>NaN</c>. Anything else is a <see cref="FormatException"/>, which is what the callers
    /// here turn into a conversion error. Note that this is why a string value asking to be read
    /// as a decimal refuses <c>Infinity</c> even though .NET would have parsed it.
    /// </remarks>
    /// <param name="text">the text</param>
    /// <exception cref="FormatException">if the text is not a decimal</exception>
    public static BigDecimal Parse(string text)
    {
        if (!TryParse(text, out BigDecimal? result, out int errorPosition))
        {
            throw new FormatException(
                text
                + (errorPosition >= 0
                    ? " at position " + errorPosition.ToString(CultureInfo.InvariantCulture)
                    : string.Empty));
        }

        return result!;
    }

    /// <summary>
    /// Reads a decimal from text, saying where it went wrong instead of throwing.
    /// </summary>
    /// <remarks>
    /// The position is the index of the first character the JDK would complain about, or -1 when
    /// the text is empty, which is a different complaint from a malformed number and the callers
    /// do treat the two differently.
    /// </remarks>
    /// <param name="text">the text</param>
    /// <param name="result">the value, when the text is a decimal</param>
    /// <param name="errorPosition">where the text went wrong, or -1 when it was empty</param>
    /// <returns>whether the text is a decimal</returns>
    public static bool TryParse(string text, out BigDecimal? result, out int errorPosition)
    {
        result = null;
        errorPosition = 0;
        if (string.IsNullOrEmpty(text))
        {
            errorPosition = -1;
            return false;
        }

        int i = 0;
        int n = text.Length;
        bool negative = false;
        char c = text[0];
        if (c == '-')
        {
            negative = true;
            i = 1;
        }
        else if (c == '+')
        {
            i = 1;
        }

        // The digits are gathered first and turned into a value once. Multiplying by ten per digit
        // is the obvious way to read a decimal and it is quadratic in the number of limbs: forty
        // digits measured 818 ns that way against 60 ns when the digits are handed to the
        // platform's own radix conversion, and a NUMERIC(38) literal is forty digits more often
        // than it is anything else.
        //
        // The sign goes into the buffer as well, so that the digits are handed over as they are
        // and no string is built on the way.
        Span<char> digits = n <= StackDigits ? stackalloc char[n] : new char[n];
        int first = 0;
        if (negative)
        {
            digits[0] = '-';
            first = 1;
        }

        int digitCount = first;
        int intDigits = 0;
        while (i < n && char.IsAsciiDigit(text[i]))
        {
            digits[digitCount++] = text[i];
            i++;
            intDigits++;
        }

        int fracDigits = 0;
        bool point = false;
        if (i < n && text[i] == '.')
        {
            point = true;
            i++;
            while (i < n && char.IsAsciiDigit(text[i]))
            {
                digits[digitCount++] = text[i];
                i++;
                fracDigits++;
            }
        }

        // A point with no digits after it is not a number, and neither is a point on its own: the
        // JDK refuses "1." as surely as it refuses ".", where a permissive reading would call the
        // first one the whole number and the second one zero.
        if (intDigits == 0 && fracDigits == 0)
        {
            errorPosition = i;
            return false;
        }

        if (point && fracDigits == 0)
        {
            errorPosition = i - 1;
            return false;
        }

        int scale = fracDigits;
        if (i < n && (text[i] == 'e' || text[i] == 'E'))
        {
            i++;
            bool negativeExponent = false;
            if (i < n && (text[i] == '-' || text[i] == '+'))
            {
                negativeExponent = text[i] == '-';
                i++;
            }

            // The exponent is read into a long and anything longer than that cannot be a scale:
            // the JDK refuses it too, and refusing it is the only answer that can be right without
            // holding on to a number that will not fit in the scale it produces.
            int exponentStart = i;
            long exponent = 0;
            bool exponentTooLarge = false;
            while (i < n && char.IsAsciiDigit(text[i]))
            {
                if (exponent < MaxExponent)
                {
                    exponent = (exponent * 10) + (text[i] - '0');
                }
                else
                {
                    exponentTooLarge = true;
                }

                i++;
            }

            if (i == exponentStart)
            {
                errorPosition = i;
                return false;
            }

            if (exponentTooLarge || exponent > MaxScale)
            {
                throw new FormatException(text);
            }

            scale = negativeExponent ? scale + (int)exponent : scale - (int)exponent;
            if (scale is > MaxScale or < MinScale)
            {
                throw new FormatException(text);
            }
        }

        if (i != n)
        {
            errorPosition = i;
            return false;
        }

        // Leading zeros are kept in the buffer: they are what makes the precision come out the
        // way the JDK's does, and the platform's own conversion drops them without being asked.
        BigInteger unscaled = digitCount == 0
            ? BigInteger.Zero
            : BigInteger.Parse(digits[..digitCount], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        result = new BigDecimal(unscaled, scale);
        return true;
    }

    /// <summary>
    /// How many digits are gathered on the stack before a heap array is used instead.
    /// </summary>
    private const int StackDigits = 128;

    /// <summary>
    /// The largest exponent the parser accumulates before deciding the scale is out of range.
    /// </summary>
    private const long MaxExponent = MaxScale;

    /// <summary>
    /// The largest scale the JDK will accept.
    /// </summary>
    private const int MaxScale = 2147483647;

    /// <summary>
    /// The smallest scale the JDK will accept.
    /// </summary>
    private const int MinScale = -2147483647;

    /// <summary>
    /// The number of significant digits, which is the number of digits of the unscaled value.
    /// </summary>
    public int Precision() => Digits(unscaledValue);

    /// <summary>
    /// The exponent of the first significant digit, which is the width of the unscaled value
    /// less the scale and one. Zero is its own case: a zero has one digit and no first digit to
    /// speak of, so its exponent is minus its scale.
    /// </summary>
    private int AdjustedExponent() => (int)Digits(unscaledValue) - scale - 1;

    /// <summary>
    /// Whether this value is negative, and not merely negative zero: a decimal has no negative
    /// zero, so zero is never negative.
    /// </summary>
    public int Signum() => unscaledValue.Sign;

    /// <summary>
    /// This value without its sign.
    /// </summary>
    public BigDecimal Abs() => unscaledValue.Sign < 0 ? new BigDecimal(BigInteger.Negate(unscaledValue), scale) : this;

    /// <summary>
    /// This value with its sign the other way round.
    /// </summary>
    public BigDecimal Negate() => new(BigInteger.Negate(unscaledValue), scale);

    /// <summary>
    /// This value without the zeros at the end of its fractional part.
    /// </summary>
    /// <remarks>
    /// The JDK's result can have a negative scale, and that is not an accident: the zeros of
    /// 1E+7 are zeros that belong to the value and not to the unscaled value, so removing them
    /// leaves the exponent. A zero becomes a zero of scale zero rather than a zero of the scale it
    /// came in with.
    /// </remarks>
    public BigDecimal StripTrailingZeros()
    {
        if (unscaledValue.IsZero)
        {
            return Zero;
        }

        BigInteger work = BigInteger.Abs(unscaledValue);
        int stripped = 0;
        BigInteger ten = Ten;
        while (true)
        {
            BigInteger division = BigInteger.DivRem(work, ten, out BigInteger rest);
            if (!rest.IsZero)
            {
                break;
            }

            work = division;
            stripped++;
        }

        return stripped == 0
            ? this
            : new BigDecimal(unscaledValue.Sign < 0 ? BigInteger.Negate(work) : work, scale - stripped);
    }

    /// <summary>
    /// Compares two values numerically, so that a scale makes no difference: 1.0 and 1.00 are the
    /// same number even though they are not the same value.
    /// </summary>
    /// <param name="other">the value to compare against</param>
    /// <returns>-1, 0 or 1</returns>
    public int CompareTo(BigDecimal? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (ReferenceEquals(this, other))
        {
            return 0;
        }

        int sign = unscaledValue.Sign.CompareTo(other.unscaledValue.Sign);
        if (sign != 0)
        {
            return sign;
        }

        if (scale == other.scale)
        {
            return unscaledValue.CompareTo(other.unscaledValue);
        }

        // The exponent of the first digit decides the answer whenever the two differ, and that is
        // most of the time: comparing 1E-100 against 1 by extending the first value to the second's
        // scale means raising ten to the hundredth power, which measured 518 ns for a comparison
        // that the exponents settle on their own. A zero has no first digit, so the shortcut is
        // not its business: 0.0 and 0.00 are the same number whatever their scales say.
        if (!unscaledValue.IsZero && !other.unscaledValue.IsZero)
        {
            int leftAdjusted = AdjustedExponent();
            int rightAdjusted = other.AdjustedExponent();
            if (leftAdjusted != rightAdjusted)
            {
                return leftAdjusted < rightAdjusted ? -1 : 1;
            }
        }

        // Bring both to the same scale by extending the one with the larger scale, which costs a
        // multiplication and keeps the comparison exact: this is what makes it possible to compare
        // values whose scales are thousands of digits apart without loss.
        int difference = scale - other.scale;
        BigInteger left = unscaledValue;
        BigInteger right = other.unscaledValue;
        if (difference > 0)
        {
            right = BigInteger.Multiply(right, BigInteger.Pow(Ten, difference));
        }
        else if (difference < 0)
        {
            left = BigInteger.Multiply(left, BigInteger.Pow(Ten, -difference));
        }

        return left.CompareTo(right);
    }

    /// <summary>
    /// Whether two values are the same number with the same scale, which is stricter than
    /// <see cref="CompareTo"/>: the JDK's equals says 1.0 is not 1.00.
    /// </summary>
    /// <param name="other">the value to compare against</param>
    /// <returns>whether they are equal as values</returns>
    public bool Equals(BigDecimal? other)
        => other is not null
            && scale == other.scale
            && unscaledValue.Equals(other.unscaledValue);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BigDecimal other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(unscaledValue, scale);

    /// <inheritdoc/>
    public override string ToString()
    {
        bool negative = unscaledValue.Sign < 0;
        string digits = BigInteger.Abs(unscaledValue).ToString(CultureInfo.InvariantCulture);
        int length = digits.Length;

        if (scale == 0)
        {
            return negative ? "-" + digits : digits;
        }

        // The exponent of the first significant digit, which is what decides between plain and
        // scientific notation.
        int adjusted = length - scale - 1;
        if (scale < 0 || adjusted < -6)
        {
            return Scientific(negative, digits, adjusted);
        }

        if (length > scale)
        {
            StringBuilder b = new(length + 3);
            if (negative)
            {
                _ = b.Append('-');
            }

            _ = b.Append(digits, 0, length - scale).Append('.').Append(digits, length - scale, scale);
            return b.ToString();
        }

        // Fewer digits than the scale: the digits are all to the right of the point, and the
        // zeros in between are written out.
        StringBuilder padded = new(scale + 2);
        if (negative)
        {
            _ = padded.Append('-');
        }

        _ = padded.Append("0.").Append('0', scale - length).Append(digits);
        return padded.ToString();
    }

    /// <summary>
    /// Writes the value in scientific notation, which is what the JDK does for a negative scale
    /// and for a value whose first digit sits below -6.
    /// </summary>
    /// <remarks>
    /// The mantissa is every digit of the unscaled value with a point after the first one, and no
    /// zeros are added: 1.5 with a scale of 8 is 1.5E-7 and not 1.500000E-7, and 0.0000000 is 0E-7
    /// rather than 0.000000E-7. A single digit gets no point at all.
    /// </remarks>
    /// <param name="negative">whether the value is negative</param>
    /// <param name="digits">the digits of the unscaled value, without a sign</param>
    /// <param name="adjusted">the exponent of the first digit</param>
    /// <returns>the printed form</returns>
    private static string Scientific(bool negative, string digits, int adjusted)
    {
        StringBuilder b = new(digits.Length + 8);
        if (negative)
        {
            _ = b.Append('-');
        }

        _ = b.Append(digits[0]);
        if (digits.Length > 1)
        {
            _ = b.Append('.').Append(digits, 1, digits.Length - 1);
        }

        _ = b.Append('E');
        if (adjusted >= 0)
        {
            _ = b.Append('+');
        }

        return b.Append(adjusted.ToString(CultureInfo.InvariantCulture)).ToString();
    }

    /// <summary>
    /// The number of decimal digits of a value, counting a zero as one digit because that is what
    /// the JDK counts.
    /// </summary>
    /// <summary>
    /// Ten, which BigInteger does not have as a constant.
    /// </summary>
    private static BigInteger Ten => BigInteger.One + BigInteger.One + BigInteger.One + BigInteger.One
        + BigInteger.One + BigInteger.One + BigInteger.One + BigInteger.One + BigInteger.One + BigInteger.One;

    /// <summary>
    /// Powers of ten, so that counting digits can be settled with a comparison instead of a
    /// multiplication.
    /// </summary>
    private static readonly BigInteger[] PowersOfTen = BuildPowersOfTen();

    /// <summary>
    /// How far the table reaches, and past it the platform is asked instead.
    /// </summary>
    private const int PowerTableLength = 129;

    /// <summary>
    /// The width in bits of the largest number in the table, which is where the table stops being
    /// worth trusting and asking the platform becomes the cheaper answer.
    /// </summary>
    private const int PowerTableBits = 428;

    private static BigInteger[] BuildPowersOfTen()
    {
        BigInteger[] powers = new BigInteger[PowerTableLength];
        powers[0] = BigInteger.One;
        for (int i = 1; i < powers.Length; i++)
        {
            powers[i] = powers[i - 1] * 10;
        }

        return powers;
    }

    /// <summary>
    /// The number of decimal digits of a value.
    /// </summary>
    /// <remarks>
    /// The logarithm alone is not enough, because it lands a hair below the whole number at a
    /// power of ten: 10^120 came out with 120 digits instead of 121, and 10^19 - 1 came out with 20
    /// instead of 19, in both directions on either side. So the logarithm gives the estimate and
    /// two comparisons against powers of ten say which way it was wrong. Verified against the
    /// string form on every power of ten up to 10^400, on its neighbours, and on three hundred
    /// thousand random values.
    /// </remarks>
    /// <param name="value">the value</param>
    /// <returns>the number of digits</returns>
    private static int Digits(BigInteger value)
    {
        if (value.IsZero)
        {
            return 1;
        }

        BigInteger work = BigInteger.Abs(value);
        if ((int)work.GetBitLength() > PowerTableBits)
        {
            return work.ToString(CultureInfo.InvariantCulture).Length;
        }

        int estimate = (int)Math.Floor(BigInteger.Log10(work)) + 1;
        if (estimate > 0 && work < PowersOfTen[estimate - 1])
        {
            estimate--;
        }
        else if (work >= PowersOfTen[estimate])
        {
            estimate++;
        }

        return estimate;
    }
}
