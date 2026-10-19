/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.DayMonthNameFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A DAYNAME() or MONTHNAME() function.
/// </summary>
public sealed class DayMonthNameFunction : Function1
{
    /// <summary>
    /// DAYNAME() (non-standard).
    /// </summary>
    public const int DayName = 0;

    /// <summary>
    /// MONTHNAME() (non-standard).
    /// </summary>
    public const int MonthName = DayName + 1;

    private static readonly string[] Names = [
        "DAYNAME", "MONTHNAME"
    ];

    /// <summary>
    /// English names of months.
    /// </summary>
    private static readonly string[] Months = [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];

    /// <summary>
    /// English names of week days, with the empty name at index 0 as
    /// <c>DateFormatSymbols.getWeekdays()</c> has it.
    /// </summary>
    private static readonly string[] WeekDays = [
        "", "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"
    ];

    private readonly int function;

    /// <summary>
    /// Creates a day or month name function.
    /// </summary>
    /// <param name="arg">the argument</param>
    /// <param name="function">one of <see cref="DayName"/> or <see cref="MonthName"/></param>
    public DayMonthNameFunction(Expression arg, int function)
        : base(arg)
    {
        this.function = function;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v = arg.GetValue(session);
        if (v == ValueNull.Instance)
        {
            return ValueNull.Instance;
        }

        long dateValue = DateTimeUtils.DateAndTimeFromValue(v, session)[0];
        string result;
        switch (function)
        {
            case DayName:
                result = WeekDays[DateTimeUtils.GetDayOfWeek(dateValue, 0)];
                break;
            case MonthName:
                result = Months[DateTimeUtils.MonthFromDateValue(dateValue) - 1];
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        return ValueVarchar.Get(result, session);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        arg = arg.Optimize(session);
        type = TypeInfo.GetTypeInfo(Value.Varchar, 20, 0, null);
        if (arg.IsConstant())
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    /// <inheritdoc/>
    public override string GetName() => Names[function];
}
