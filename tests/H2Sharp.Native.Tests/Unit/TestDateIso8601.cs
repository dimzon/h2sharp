// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.unit.TestDateIso8601.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference's main() entry is the runner; xUnit runs Test() directly. The
// Java enum-with-static-field trick (the type drives the shared parse helper) becomes a
// delegate field.
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Native.Tests.Unit;

/// <summary>
/// Test cases for DateTimeIso8601Utils.
/// </summary>
public sealed class TestDateIso8601
{
    private enum Type
    {
        Date,
        Timestamp,
        TimestampTimezone0,
        TimestampTimezonePlus18,
        TimestampTimezoneMinus18,
    }

    private static Type type;

    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public void Test()
    {
        type = Type.Date;
        DoTest();
        type = Type.Timestamp;
        DoTest();
        type = Type.TimestampTimezone0;
        DoTest();
        type = Type.TimestampTimezonePlus18;
        DoTest();
        type = Type.TimestampTimezoneMinus18;
        DoTest();
    }

    private static long Parse(string s)
    {
        switch (type)
        {
            case Type.Date:
                return ValueDate.Parse(s).GetDateValue();
            case Type.Timestamp:
                return ValueTimestamp.Parse(s, null).GetDateValue();
            case Type.TimestampTimezone0:
                return ValueTimestampTimeZone.Parse(s + " 00:00:00.0Z", null).GetDateValue();
            case Type.TimestampTimezonePlus18:
                return ValueTimestampTimeZone.Parse(s + " 00:00:00+18:00", null).GetDateValue();
            case Type.TimestampTimezoneMinus18:
                return ValueTimestampTimeZone.Parse(s + " 00:00:00-18:00", null).GetDateValue();
            default:
                throw new InvalidOperationException();
        }
    }

    private static void DoTest()
    {
        TestIsoDayOfWeek();
        TestIsoWeekJanuary1thMonday();
        TestIsoWeekJanuary1thTuesday();
        TestIsoWeekJanuary1thWednesday();
        TestIsoWeekJanuary1thThursday();
        TestIsoWeekJanuary1thFriday();
        TestIsoWeekJanuary1thSaturday();
        TestIsoWeekJanuary1thSunday();
        TestIsoYearJanuary1thMonday();
        TestIsoYearJanuary1thTuesday();
        TestIsoYearJanuary1thWednesday();
        TestIsoYearJanuary1thThursday();
        TestIsoYearJanuary1thFriday();
        TestIsoYearJanuary1thSaturday();
        TestIsoYearJanuary1thSunday();
    }

    /// <summary>
    /// Test if day of week is returned as Monday = 1 to Sunday = 7.
    /// </summary>
    private static void TestIsoDayOfWeek()
    {
        Assert.Equal(1, DateTimeUtils.GetIsoDayOfWeek(Parse("2008-09-29")));
        Assert.Equal(2, DateTimeUtils.GetIsoDayOfWeek(Parse("2008-09-30")));
        Assert.Equal(3, DateTimeUtils.GetIsoDayOfWeek(Parse("2008-10-01")));
        Assert.Equal(4, DateTimeUtils.GetIsoDayOfWeek(Parse("2008-10-02")));
        Assert.Equal(5, DateTimeUtils.GetIsoDayOfWeek(Parse("2008-10-03")));
        Assert.Equal(6, DateTimeUtils.GetIsoDayOfWeek(Parse("2008-10-04")));
        Assert.Equal(7, DateTimeUtils.GetIsoDayOfWeek(Parse("2008-10-05")));
    }

    /// <summary>
    /// January 1st is a Monday therefore the week belongs to the next year.
    /// </summary>
    private static void TestIsoWeekJanuary1thMonday()
    {
        Assert.Equal(52, DateTimeUtils.GetIsoWeekOfYear(Parse("2006-12-31")));
        Assert.Equal(1, DateTimeUtils.GetIsoWeekOfYear(Parse("2007-01-01")));
        Assert.Equal(1, DateTimeUtils.GetIsoWeekOfYear(Parse("2007-01-07")));
        Assert.Equal(2, DateTimeUtils.GetIsoWeekOfYear(Parse("2007-01-08")));
    }

    /// <summary>
    /// January 1st is a Tuesday therefore the week belongs to the next year.
    /// </summary>
    private static void TestIsoWeekJanuary1thTuesday()
    {
        Assert.Equal(52, DateTimeUtils.GetIsoWeekOfYear(Parse("2007-12-30")));
        Assert.Equal(1, DateTimeUtils.GetIsoWeekOfYear(Parse("2007-12-31")));
        Assert.Equal(1, DateTimeUtils.GetIsoWeekOfYear(Parse("2008-01-01")));
        Assert.Equal(1, DateTimeUtils.GetIsoWeekOfYear(Parse("2008-01-06")));
        Assert.Equal(2, DateTimeUtils.GetIsoWeekOfYear(Parse("2008-01-07")));
    }

    /// <summary>
    /// January1th is a Wednesday therefore the week belongs to the next year.
    /// </summary>
    private static void TestIsoWeekJanuary1thWednesday()
    {
        Assert.Equal(52, DateTimeUtils.GetIsoWeekOfYear(Parse("2002-12-28")));
        Assert.Equal(52, DateTimeUtils.GetIsoWeekOfYear(Parse("2002-12-29")));
        Assert.Equal(1, DateTimeUtils.GetIsoWeekOfYear(Parse("2002-12-30")));
        Assert.Equal(1, DateTimeUtils.GetIsoWeekOfYear(Parse("2003-01-05")));
        Assert.Equal(2, DateTimeUtils.GetIsoWeekOfYear(Parse("2003-01-06")));
    }

    /// <summary>
    /// January 1st is a Thursday therefore the week belongs to the previous year.
    /// </summary>
    private static void TestIsoWeekJanuary1thThursday()
    {
        Assert.Equal(1, DateTimeUtils.GetIsoWeekOfYear(Parse("2003-01-01")));
        Assert.Equal(1, DateTimeUtils.GetIsoWeekOfYear(Parse("2005-01-02")));
    }

    /// <summary>
    /// January 1st is a Friday therefore the week belongs to the previous year.
    /// </summary>
    private static void TestIsoWeekJanuary1thFriday()
    {
        Assert.Equal(53, DateTimeUtils.GetIsoWeekOfYear(Parse("2004-01-01")));
        Assert.Equal(1, DateTimeUtils.GetIsoWeekOfYear(Parse("2010-01-01")));
    }

    /// <summary>
    /// January 1st is a Saturday therefore the week belongs to the previous year,
    /// but 1999 starts with a Friday, so it has 53 weeks.
    /// </summary>
    private static void TestIsoWeekJanuary1thSaturday()
    {
        Assert.Equal(52, DateTimeUtils.GetIsoWeekOfYear(Parse("2005-01-01")));
        Assert.Equal(53, DateTimeUtils.GetIsoWeekOfYear(Parse("2005-12-31")));
        Assert.Equal(1, DateTimeUtils.GetIsoWeekOfYear(Parse("2007-01-01")));
    }

    /// <summary>
    /// January 1st is a Sunday therefore the week belongs to the previous year.
    /// </summary>
    private static void TestIsoWeekJanuary1thSunday()
    {
        Assert.Equal(52, DateTimeUtils.GetIsoWeekOfYear(Parse("2006-01-01")));
        Assert.Equal(1, DateTimeUtils.GetIsoWeekOfYear(Parse("2007-01-01")));
    }

    /// <summary>
    /// January 1st is a Monday, so it belongs to the next year.
    /// </summary>
    private static void TestIsoYearJanuary1thMonday()
    {
        Assert.Equal(2007, DateTimeUtils.GetIsoWeekYear(Parse("2007-12-31")));
        Assert.Equal(2008, DateTimeUtils.GetIsoWeekYear(Parse("2008-01-01")));
    }

    /// <summary>
    /// January 1st is a Tuesday, so it belongs to the next year.
    /// </summary>
    private static void TestIsoYearJanuary1thTuesday()
    {
        Assert.Equal(2008, DateTimeUtils.GetIsoWeekYear(Parse("2007-12-31")));
        Assert.Equal(2008, DateTimeUtils.GetIsoWeekYear(Parse("2008-01-01")));
    }

    /// <summary>
    /// January 1st is a Wednesday, so it belongs to the next year.
    /// </summary>
    private static void TestIsoYearJanuary1thWednesday()
    {
        Assert.Equal(2002, DateTimeUtils.GetIsoWeekYear(Parse("2002-12-31")));
        Assert.Equal(2003, DateTimeUtils.GetIsoWeekYear(Parse("2003-01-01")));
    }

    /// <summary>
    /// January 1st is a Thursday, so it belongs to this year.
    /// </summary>
    private static void TestIsoYearJanuary1thThursday()
    {
        Assert.Equal(2003, DateTimeUtils.GetIsoWeekYear(Parse("2003-01-01")));
    }

    /// <summary>
    /// January 1st is a Friday, so it belongs to the previous year.
    /// </summary>
    private static void TestIsoYearJanuary1thFriday()
    {
        Assert.Equal(2003, DateTimeUtils.GetIsoWeekYear(Parse("2004-01-01")));
    }

    /// <summary>
    /// January 1st is a Saturday, so it belongs to the previous year.
    /// </summary>
    private static void TestIsoYearJanuary1thSaturday()
    {
        Assert.Equal(2004, DateTimeUtils.GetIsoWeekYear(Parse("2005-01-01")));
    }

    /// <summary>
    /// January 1st is a Sunday, so it belongs to the previous year.
    /// </summary>
    private static void TestIsoYearJanuary1thSunday()
    {
        Assert.Equal(2005, DateTimeUtils.GetIsoWeekYear(Parse("2006-01-01")));
    }
}
