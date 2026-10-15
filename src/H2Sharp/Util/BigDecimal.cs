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

    /// <summary>
    /// A decimal from its two halves, as the store format keeps them.
    /// </summary>
    /// <remarks>
    /// Internal rather than private because the store's object serializer reads and writes a decimal
    /// split this way, and it is the only caller that needs a value put together from parts.
    /// </remarks>
    /// <param name="unscaled">the unscaled value</param>
    /// <param name="scale">the scale</param>
    internal BigDecimal(BigInteger unscaled, int scale)
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
    /// A value of an integer, which is a decimal whose scale is zero.
    /// </summary>
    /// <remarks>
    /// The reference's <c>valueOf</c> and <c>new BigDecimal(BigInteger)</c>, which the values of
    /// the integral types hand over. An integer has no scale of its own and inventing one is how a
    /// NUMERIC column ends up printing a decimal point it was never given.
    /// </remarks>
    /// <param name="value">the integer</param>
    /// <returns>the value</returns>
    public static BigDecimal FromLong(long value) => new(new BigInteger(value), 0);

    /// <summary>
    /// A value of an arbitrary precision integer, which is a decimal whose scale is zero.
    /// </summary>
    /// <param name="value">the integer</param>
    /// <returns>the value</returns>
    public static BigDecimal FromBigInteger(BigInteger value) => new(value, 0);

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
    public static BigDecimal Parse(string text) => Parse(text.AsSpan(), text);

    /// <summary>
    /// Reads a decimal from part of a larger text, without copying that part out first.
    /// </summary>
    /// <remarks>
    /// A caller that has found a number inside something longer — the JSON parser, which has
    /// measured the extent of the literal without moving it — has a span, not a string. Handing it
    /// one and having it allocate would undo the measurement, so the whole grammar below works on a
    /// span and the string overload is the one that pays for a copy. Only the failure message needs
    /// a real string, and only on a path that is already throwing.
    /// </remarks>
    /// <param name="text">the span holding the decimal</param>
    /// <param name="forMessage">the text as a string, used only to report a failure</param>
    /// <returns>the value</returns>
    /// <exception cref="FormatException">if the span is not a decimal</exception>
    public static BigDecimal Parse(ReadOnlySpan<char> text, string forMessage)
    {
        if (!TryParse(text, out BigDecimal? result, out int errorPosition))
        {
            throw new FormatException(
                forMessage
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
        => TryParse(text.AsSpan(), out result, out errorPosition);

    /// <summary>
    /// Reads a decimal from part of a larger text, saying where it went wrong instead of throwing.
    /// </summary>
    /// <param name="text">the span holding the decimal</param>
    /// <param name="result">the value, when the text is a decimal</param>
    /// <param name="errorPosition">where the text went wrong, or -1 when it was empty</param>
    /// <returns>whether the text is a decimal</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out BigDecimal? result, out int errorPosition)
    {
        result = null;
        errorPosition = 0;
        if (text.IsEmpty)
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
        while (i < n)
        {
            int value = JavaCharacter.DigitValue(text[i]);
            if (value < 0)
            {
                break;
            }

            // The value goes into the buffer rather than the character, because a digit is not
            // necessarily a character from '0' to '9': the JDK reads a decimal written with
            // Arabic-Indic digits as the number they stand for, and the buffer is handed to the
            // platform's own radix conversion, which knows nothing but '0' to '9'.
            digits[digitCount++] = (char)('0' + value);
            i++;
            intDigits++;
        }

        int fracDigits = 0;
        if (i < n && text[i] == '.')
        {
            i++;
            while (i < n)
            {
                int value = JavaCharacter.DigitValue(text[i]);
                if (value < 0)
                {
                    break;
                }

                digits[digitCount++] = (char)('0' + value);
                i++;
                fracDigits++;
            }
        }

        // The grammar is Sign? (Digits? '.' Digits? | Digits) Exponent?, so what it asks for is one
        // digit somewhere and not a point on its own. "1." and "1.e5" are numbers to the JDK, and
        // an earlier version of this file refused both because a point with nothing after it reads
        // like a mistake; the tokenizer oracle found it, on "SELECT 1.", where the reference reads
        // a NUMERIC of one and this port called it a syntax error.
        if (intDigits == 0 && fracDigits == 0)
        {
            errorPosition = i;
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
            while (i < n)
            {
                int digit = JavaCharacter.DigitValue(text[i]);
                if (digit < 0)
                {
                    break;
                }

                if (exponent < MaxExponent)
                {
                    exponent = (exponent * 10) + digit;
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
                // Only a failing path needs a real string for the message.
                throw new FormatException(text.ToString());
            }

            scale = negativeExponent ? scale + (int)exponent : scale - (int)exponent;
            if (scale is > MaxScale or < MinScale)
            {
                // Only a failing path needs a real string for the message.
                throw new FormatException(text.ToString());
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
    /// This value as a long, dropping the fraction towards zero.
    /// </summary>
    /// <remarks>
    /// The JDK's <c>longValue</c>, which key resolution needs after it has checked the value
    /// fits: the division truncates the same way, and the in-range cast cannot overflow.
    /// </remarks>
    /// <returns>the truncated value</returns>
    public long LongValue()
    {
        BigInteger integer = scale <= 0
            ? unscaledValue * BigInteger.Pow(10, -scale)
            : unscaledValue / BigInteger.Pow(10, scale);
        return (long)integer;
    }

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

        // The common case fits in a long, where Java works on its compact form and divides
        // without allocating: one range check instead of a BigInteger division per zero.
        // The remainder keeps its sign in C#, but only zero matters here, so negatives
        // strip exactly like positives.
        if (unscaledValue.GetByteCount() <= sizeof(long))
        {
            long work = (long)unscaledValue;
            int stripped = 0;
            while (work % 10 == 0)
            {
                work /= 10;
                stripped++;
            }

            return stripped == 0 ? this : new BigDecimal(new BigInteger(work), scale - stripped);
        }

        BigInteger big = BigInteger.Abs(unscaledValue);
        int bigStripped = 0;
        BigInteger ten = Ten;
        while (true)
        {
            BigInteger division = BigInteger.DivRem(big, ten, out BigInteger rest);
            if (!rest.IsZero)
            {
                break;
            }

            big = division;
            bigStripped++;
        }

        return bigStripped == 0
            ? this
            : new BigDecimal(unscaledValue.Sign < 0 ? BigInteger.Negate(big) : big, scale - bigStripped);
    }

    /// <summary>
    /// The sum of two values, exactly, with no precision to lose.
    /// </summary>
    /// <remarks>
    /// The two scales are made equal first by extending the coarser one, which keeps the result
    /// exact: a decimal never has to round to be added. A sum of zero is the one place the JDK
    /// reaches for a rule of its own, and the rule is that the zero keeps the coarser of the two
    /// scales, so 1.5 + (-1.50) is 0.00 and not 0.
    /// </remarks>
    /// <param name="other">the value to add</param>
    /// <returns>the sum</returns>
    public BigDecimal Add(BigDecimal other)
    {
        if (scale == other.scale)
        {
            BigInteger same = unscaledValue + other.unscaledValue;
            return same.IsZero ? ZeroWithScale(scale) : new BigDecimal(same, scale);
        }

        BigDecimal coarser = scale > other.scale ? this : other;
        BigDecimal finer = scale > other.scale ? other : this;

        // A finer value of zero adds nothing, and saying so matters more than it looks: the scales
        // can differ by billions of digits in values that arrive from a hostile literal, and
        // extending a zero by 10 to the two billionth power is not an answer anybody wants.
        if (finer.unscaledValue.IsZero)
        {
            return coarser;
        }

        long difference = (long)coarser.scale - finer.scale;
        BigInteger extended = BigInteger.Multiply(finer.unscaledValue, BigInteger.Pow(Ten, (int)difference));
        BigInteger total = coarser.unscaledValue + extended;
        return total.IsZero ? ZeroWithScale(coarser.scale) : new BigDecimal(total, coarser.scale);
    }
    /// <summary>
    /// The difference of two values, exactly.
    /// </summary>
    /// <param name="other">the value to subtract</param>
    /// <returns>the difference</returns>
    public BigDecimal Subtract(BigDecimal other) => Add(other.Negate());

    /// <summary>
    /// The product of two values, exactly.
    /// </summary>
    /// <remarks>
    /// The scales add, because the unscaled values do: that is what the shape of the number means.
    /// Nothing here can overflow, which is the whole reason a decimal is not a double.
    /// </remarks>
    /// <param name="other">the value to multiply by</param>
    /// <returns>the product</returns>
    public BigDecimal Multiply(BigDecimal other)
        => new(BigInteger.Multiply(unscaledValue, other.unscaledValue), scale + other.scale);

    /// <summary>
    /// A zero of the given scale, which is what the JDK hands back where a sum of two values is
    /// zero.
    /// </summary>
    /// <param name="zeroScale">the scale of the zero</param>
    /// <returns>the zero</returns>
    private static BigDecimal ZeroWithScale(int zeroScale)
        => zeroScale == 0 ? Zero : new BigDecimal(BigInteger.Zero, zeroScale);
    /// <summary>
    /// This value with the given scale, keeping as many digits as it can.
    /// </summary>
    /// <remarks>
    /// Making the scale smaller drops digits, and those are rounded by the mode rather than cut,
    /// because a decimal that quietly loses a digit is not a decimal. Making it larger only adds
    /// zeros.
    /// <para>
    /// The JDK rounds the magnitude and restores the sign afterwards, which is what makes CEILING
    /// round a negative value downwards and HALF_UP send -0.5 away from zero rather than towards
    /// the even digit zero. Both behaviours are visible in the answers and neither follows from
    /// looking at the digits.
    /// </para>
    /// </remarks>
    /// <param name="newScale">the scale to give the value</param>
    /// <param name="mode">what to do with the digits that do not fit</param>
    /// <returns>the value at the new scale</returns>
    /// <exception cref="ArithmeticException">
    /// if the mode is <see cref="RoundingMode.Unnecessary"/> and digits would be lost
    /// </exception>
    public BigDecimal SetScale(int newScale, RoundingMode mode)
    {
        if (newScale == scale)
        {
            return this;
        }

        if (newScale > scale)
        {
            return new BigDecimal(BigInteger.Multiply(unscaledValue, BigInteger.Pow(Ten, newScale - scale)), newScale);
        }

        BigInteger divisor = BigInteger.Pow(Ten, scale - newScale);
        bool negative = unscaledValue.Sign < 0;
        BigInteger magnitude = RoundMagnitude(
            BigInteger.Abs(unscaledValue), divisor, MagnitudeMode(mode, negative));
        return new BigDecimal(negative ? BigInteger.Negate(magnitude) : magnitude, newScale);
    }

    /// <summary>
    /// The quotient of two values, given to the scale asked for and rounded by the mode.
    /// </summary>
    /// <remarks>
    /// The scale is the number of digits wanted after the point, so a division cannot be asked for
    /// a scale it does not need: the JDK answers the whole quotient and throws the digits away when
    /// the mode is UNNECESSARY.
    /// </remarks>
    /// <param name="divisor">the value to divide by</param>
    /// <param name="quotientScale">the scale of the quotient</param>
    /// <param name="mode">what to do with the digits that do not fit</param>
    /// <returns>the quotient</returns>
    /// <exception cref="ArithmeticException">if the divisor is zero</exception>
    /// <exception cref="ArithmeticException">
    /// if the mode is <see cref="RoundingMode.Unnecessary"/> and digits would be lost
    /// </exception>
    public BigDecimal Divide(BigDecimal divisor, int quotientScale, RoundingMode mode)
    {
        if (divisor.unscaledValue.IsZero)
        {
            throw new DivideByZeroException();
        }

        BigInteger numerator = BigInteger.Abs(unscaledValue);
        BigInteger denominator = BigInteger.Abs(divisor.unscaledValue);
        bool negative = unscaledValue.Sign * divisor.unscaledValue.Sign < 0;

        // The quotient as a number is (unscaled / unscaled) shifted by the difference of the two
        // scales, so a quotient at the scale asked for is that shift plus the scale asked for.
        // Getting this wrong is not a rounding question at all: 1.5 divided by 2 at a scale of 0 is
        // 0.75 rounded to nothing, and a shift that forgot the scales would answer 8.
        long shift = (long)quotientScale - scale + divisor.scale;
        BigInteger magnitude;
        if (shift >= 0)
        {
            // The quotient at that scale is one multiplication and one division rather than a
            // division that produces an endless decimal and a second pass over it.
            magnitude = RoundMagnitude(
                BigInteger.Multiply(numerator, BigInteger.Pow(Ten, (int)shift)),
                denominator,
                MagnitudeMode(mode, negative));
        }
        else
        {
            magnitude = RoundMagnitude(
                numerator,
                BigInteger.Multiply(denominator, BigInteger.Pow(Ten, (int)-shift)),
                MagnitudeMode(mode, negative));
        }

        int sign = unscaledValue.Sign * divisor.unscaledValue.Sign;
        return new BigDecimal(sign < 0 ? BigInteger.Negate(magnitude) : magnitude, quotientScale);
    }

    /// <summary>
    /// What is left of this value after dividing it by the divisor and keeping the whole part of
    /// the quotient.
    /// </summary>
    /// <remarks>
    /// The remainder is this value minus the whole part of the quotient times the divisor, and the
    /// scale of the answer follows from that rather than from the divisor: a remainder that is not
    /// zero keeps the scale of the subtraction, so 1 modulo 3E-10 is 1E-10 and 1.2340 modulo 0.10
    /// is 0.0340. A remainder of zero is the one case the JDK decides on its own, and it gives
    /// back the scale of this value: 1.5 modulo 1E-10 is 0.0 rather than 0.0000000000, and 1 modulo
    /// 1E-10 is 0.
    /// </remarks>
    /// <param name="divisor">the value to divide by</param>
    /// <returns>the remainder, which takes the sign of this value</returns>
    /// <exception cref="ArithmeticException">if the divisor is zero</exception>
    public BigDecimal Remainder(BigDecimal divisor)
    {
        if (divisor.unscaledValue.IsZero)
        {
            throw new DivideByZeroException();
        }

        BigDecimal remainder = Subtract(divisor.Multiply(DivideTruncated(divisor)));
        return remainder.unscaledValue.IsZero ? ZeroWithScale(scale) : remainder;
    }

    /// <summary>
    /// The quotient with every digit after the point cut off, which is what the remainder needs.
    /// </summary>
    /// <remarks>
    /// The cut is towards zero, so that a remainder takes the sign of the value it was cut from,
    /// which is what the JDK's remainder does and what a query with a negative dividend expects.
    /// The scales are part of the division here for the same reason they are part of the rounding
    /// one: 1.5 divided by 2 has no whole part, and a division of the unscaled values alone would
    /// say it had seven.
    /// </remarks>
    /// <param name="divisor">the value to divide by</param>
    /// <returns>the quotient, cut at the point</returns>
    private BigDecimal DivideTruncated(BigDecimal divisor)
    {
        BigInteger numerator = BigInteger.Abs(unscaledValue);
        BigInteger denominator = BigInteger.Abs(divisor.unscaledValue);
        long shift = (long)divisor.scale - scale;
        BigInteger whole = shift >= 0
            ? BigInteger.Divide(BigInteger.Multiply(numerator, BigInteger.Pow(Ten, (int)shift)), denominator)
            : BigInteger.Divide(numerator, BigInteger.Multiply(denominator, BigInteger.Pow(Ten, (int)-shift)));

        int sign = unscaledValue.Sign * divisor.unscaledValue.Sign;
        return new BigDecimal(sign < 0 ? BigInteger.Negate(whole) : whole, 0);
    }

    /// <summary>
    /// The mode to apply to the magnitude of a value that is being rounded.
    /// </summary>
    /// <remarks>
    /// Only two modes depend on the sign, and both because they name a direction on the number
    /// line rather than a direction in the digits. A ceiling of -0.5 is 0, and 0 is the smaller
    /// magnitude, so on the magnitude a ceiling of a negative value rounds down; a floor of -0.5
    /// is -1, and that is the larger magnitude, so it rounds up. Everything else means the same
    /// thing whichever side of zero the value is on: HALF_UP sends -0.5 away from zero, not to the
    /// even digit zero.
    /// </remarks>
    /// <param name="mode">the mode the value asks for</param>
    /// <param name="negative">whether the value is negative</param>
    /// <returns>the mode to apply to its magnitude</returns>
    private static RoundingMode MagnitudeMode(RoundingMode mode, bool negative)
    {
        if (!negative)
        {
            return mode;
        }

        return mode switch
        {
            RoundingMode.Ceiling => RoundingMode.Down,
            RoundingMode.Floor => RoundingMode.Up,
            _ => mode,
        };
    }

    /// <summary>
    /// Divides a magnitude by another, rounding the part that is left by the mode.
    /// </summary>
    /// <remarks>
    /// The comparison of twice the remainder with the divisor is what decides a half, and every
    /// half rule in the JDK is that one comparison: exactly half is the boundary and each mode says
    /// which side of it to fall on, with HALF_EVEN asking whether the digit kept is even.
    /// </remarks>
    /// <param name="dividend">the magnitude to divide</param>
    /// <param name="divisor">the magnitude to divide by</param>
    /// <param name="mode">what to do with the part that is left</param>
    /// <returns>the quotient, rounded</returns>
    /// <exception cref="ArithmeticException">
    /// if the mode is <see cref="RoundingMode.Unnecessary"/> and something is left over
    /// </exception>
    private static BigInteger RoundMagnitude(BigInteger dividend, BigInteger divisor, RoundingMode mode)
    {
        BigInteger quotient = BigInteger.Divide(dividend, divisor);
        BigInteger remainder = dividend - (quotient * divisor);
        if (remainder.IsZero)
        {
            return quotient;
        }

        // A .NET integer compares with the distance between the numbers, so the sign is taken
        // from it rather than from the comparison itself.
        BigInteger twice = remainder << 1;
        int half = twice == divisor ? 0 : twice > divisor ? 1 : -1;
        bool up = mode switch
        {
            RoundingMode.Up => true,
            RoundingMode.Down => false,

            // The value is a magnitude, so a ceiling is up and a floor is down whatever the sign
            // of the original was.
            RoundingMode.Ceiling => true,
            RoundingMode.Floor => false,

            RoundingMode.HalfUp => half >= 0,
            RoundingMode.HalfDown => half > 0,
            RoundingMode.HalfEven => half > 0 || (half == 0 && (!quotient.IsEven)),

            _ => throw new ArithmeticException("Rounding necessary"),
        };

        return up ? quotient + BigInteger.One : quotient;
    }

    /// <summary>
    /// This value written out in full, with every zero the scale stands for written out too, so
    /// that 1E+100 is a hundred and one digits rather than an exponent.
    /// </summary>
    /// <remarks>
    /// This is what the reference prints a NUMERIC as, and it is a different method from the
    /// general one: a value has to come back out of a query as a number a person can read, and a
    /// store's own values are read far more often than they are written.
    /// </remarks>
    public string ToPlainString()
    {
        string digits = BigInteger.Abs(unscaledValue).ToString(CultureInfo.InvariantCulture);

        // A zero keeps a positive scale, because the zeros after the point are part of how it was
        // written, and drops a negative one: the zeros of 0E+10 stand for nothing, so writing them
        // out would say the number had eleven digits when it has one.
        if (unscaledValue.IsZero)
        {
            return scale <= 0 ? "0" : "0." + new string('0', scale);
        }

        if (scale == 0)
        {
            return unscaledValue.Sign < 0 ? "-" + digits : digits;
        }

        StringBuilder b = new(digits.Length + Math.Max(scale, 0) + 2);
        if (unscaledValue.Sign < 0)
        {
            _ = b.Append('-');
        }

        if (scale < 0)
        {
            // The zeros of a negative scale are zeros of the value, and in plain form they are
            // written out after the digits rather than left to an exponent.
            return b.Append(digits).Append('0', -scale).ToString();
        }

        if (digits.Length > scale)
        {
            return b.Append(digits, 0, digits.Length - scale)
                .Append('.')
                .Append(digits, digits.Length - scale, scale)
                .ToString();
        }

        return b.Append("0.").Append('0', scale - digits.Length).Append(digits).ToString();
    }

    /// <summary>
    /// This value as a double, rounded to the nearest double the way the JDK rounds it.
    /// </summary>
    /// <remarks>
    /// Both platforms convert decimal text to a double by rounding to nearest and breaking a tie
    /// towards the even value, so handing the plain form to the platform's own converter is the
    /// same answer as the JDK's own arithmetic and it costs nothing to write.
    /// </remarks>
    public double ToDouble() => double.Parse(ToPlainString(), NumberStyles.Float, CultureInfo.InvariantCulture);
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
                // A larger adjusted exponent is a larger magnitude, which is the larger value
                // only when both are positive: for two negatives it is the smaller one.
                int magnitude = leftAdjusted < rightAdjusted ? -1 : 1;
                return unscaledValue.Sign < 0 ? -magnitude : magnitude;
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
    /// <remarks>
    /// A field rather than a property: the sum is the same ten every time, and rebuilding it
    /// per read spent ~200 bytes of garbage on every caller.
    /// </remarks>
    private static readonly BigInteger Ten = BigInteger.One + BigInteger.One + BigInteger.One + BigInteger.One
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
