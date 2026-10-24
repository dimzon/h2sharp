/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueDate.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Globalization;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the DATE data type.
/// </summary>
/// <remarks>
/// A date here is one number and not a year, a month and a day: the three are shifted into it, and
/// <see cref="DateValue"/> is the number. That is what makes two dates compared by one comparison
/// and a range read as one number, and it is why this class has no arithmetic: a date is not a
/// number, so there is nothing to add to it until a timestamp arrives.
/// </remarks>
public sealed class ValueDate : Value
{
    /// <summary>
    /// The precision and display size of the textual representation of a date.
    /// Example: 2000-01-02
    /// </summary>
    public const int Precision = 10;

    private readonly long dateValue;

    private ValueDate(long dateValue)
    {
        if (dateValue < DateTimeUtils.MinDateValue || dateValue > DateTimeUtils.MaxDateValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dateValue), "dateValue out of range " + dateValue);
        }

        this.dateValue = dateValue;
    }

    /// <summary>
    /// Get or create a date value for the given date.
    /// </summary>
    /// <param name="dateValue">the date value</param>
    /// <returns>the value</returns>
    public static ValueDate FromDateValue(long dateValue)
        => (ValueDate)Value.Cache(new ValueDate(dateValue))!;

    /// <summary>
    /// Parse a string to a date.
    /// </summary>
    /// <param name="s">the string to parse</param>
    /// <returns>the date</returns>
    /// <exception cref="H2SharpException">if the text is not a date</exception>
    public static ValueDate Parse(string s)
    {
        try
        {
            return FromDateValue(DateTimeUtils.ParseDateValue(s, 0, s.Length));
        }
        catch (Exception e) when (e is ArgumentException or FormatException or ArithmeticException)
        {
            throw DbException.Get(ErrorCode.InvalidDatetimeConstant2, e, "DATE", s);
        }
    }

    /// <summary>
    /// The date value, which is the year, month and day shifted into one number.
    /// </summary>
    /// <returns>the date value</returns>
    public long GetDateValue() => dateValue;

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeDate;

    /// <inheritdoc/>
    public override int GetValueType() => Value.Date;

    /// <inheritdoc/>
    public override string? GetString()
        => DateTimeUtils.AppendDate(new StringBuilder(Precision), dateValue).ToString();

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => Dialect.AppendDate(builder, this, sqlFlags);

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value o, CompareMode mode, CastDataProvider? provider)
        => dateValue.CompareTo(((ValueDate)o).dateValue);

    /// <inheritdoc/>
    public override bool Equals(object? other)
        => ReferenceEquals(this, other)
            || (other is ValueDate date && dateValue == date.dateValue);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(dateValue);
}
