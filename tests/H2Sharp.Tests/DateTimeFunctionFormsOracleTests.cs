/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 96: the date-time function forms the field-oriented tests do not reach —
 * DATEADD over the millennium/century/decade and every time unit (including the
 * timezone fields and their refusals), EXTRACT of sub-second parts from an
 * INTERVAL and of fields an interval does not have, DATE_TRUNC for the
 * millennium and over TIME and TIME WITH TIME ZONE, the fields DATE_TRUNC
 * refuses, and DATEDIFF between values that carry a time zone.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DateTimeFunctionFormsOracleTests
{
    [JavaOracleFact]
    public void DateAddEveryFieldMatchesJdbc()
    {
        ExplainOracleTests.Check([
            "SELECT CAST(DATEADD('MILLENNIUM', 1, DATE '2024-01-01') AS VARCHAR)",
            "SELECT CAST(DATEADD('CENTURY', 1, DATE '2024-01-01') AS VARCHAR)",
            "SELECT CAST(DATEADD('DECADE', 1, DATE '2024-01-01') AS VARCHAR)",
            "SELECT CAST(DATEADD('QUARTER', 1, DATE '2024-01-01') AS VARCHAR)",
            "SELECT CAST(DATEADD('WEEK', 1, DATE '2024-01-01') AS VARCHAR)",
            "SELECT CAST(DATEADD('ISO_WEEK', 1, DATE '2024-01-01') AS VARCHAR)",
            "SELECT CAST(DATEADD('DAY_OF_YEAR', 1, DATE '2024-01-01') AS VARCHAR)",
            "SELECT CAST(DATEADD('DOW', 1, DATE '2024-01-01') AS VARCHAR)",
            "SELECT CAST(DATEADD('ISO_DAY_OF_WEEK', 1, DATE '2024-01-01') AS VARCHAR)",
            "SELECT CAST(DATEADD('MINUTE', 1, TIMESTAMP '2024-01-01 10:00:00') AS VARCHAR)",
            "SELECT CAST(DATEADD('SECOND', 1, TIMESTAMP '2024-01-01 10:00:00') AS VARCHAR)",
            "SELECT CAST(DATEADD('EPOCH', 1, TIMESTAMP '2024-01-01 10:00:00') AS VARCHAR)",
            "SELECT CAST(DATEADD('MILLISECOND', 1, TIMESTAMP '2024-01-01 10:00:00') AS VARCHAR)",
            "SELECT CAST(DATEADD('MICROSECOND', 1, TIMESTAMP '2024-01-01 10:00:00') AS VARCHAR)",
            "SELECT CAST(DATEADD('NANOSECOND', 1, TIMESTAMP '2024-01-01 10:00:00') AS VARCHAR)",
            "SELECT CAST(DATEADD('TIMEZONE_HOUR', 1, "
                + "TIMESTAMP WITH TIME ZONE '2024-01-01 10:00:00+02') AS VARCHAR)",
            "SELECT CAST(DATEADD('TIMEZONE_MINUTE', 30, TIME WITH TIME ZONE '10:00:00+02') AS VARCHAR)",
            "SELECT CAST(DATEADD('TIMEZONE_SECOND', 30, "
                + "TIMESTAMP WITH TIME ZONE '2024-01-01 10:00:00+02') AS VARCHAR)",
            // A count too wide for an int is refused, and a time has no date part to add.
            "SELECT DATEADD('SECOND', 2147483648, TIMESTAMP '2024-01-01 10:00:00')",
            "SELECT DATEADD('YEAR', 1, TIME '10:00:00')",
            "SELECT DATEADD('DAY', 1, TIME '10:00:00')",
        ]);
    }

    [JavaOracleFact]
    public void ExtractIntervalPartsAndRefusalsMatchJdbc()
    {
        ExplainOracleTests.Check([
            "SELECT EXTRACT(MILLISECOND FROM INTERVAL '1 02:03:04.567' DAY TO SECOND)",
            "SELECT EXTRACT(MICROSECOND FROM INTERVAL '1 02:03:04.567891' DAY TO SECOND)",
            "SELECT EXTRACT(NANOSECOND FROM INTERVAL '1 02:03:04.567891234' DAY TO SECOND)",
            "SELECT EXTRACT(DAY_OF_WEEK FROM DATE '2024-05-06')",
            // An interval has no day of the week and no quarter.
            "SELECT EXTRACT(DAY_OF_WEEK FROM INTERVAL '1 02:03:04' DAY TO SECOND)",
            "SELECT EXTRACT(QUARTER FROM INTERVAL '1 02:03:04' DAY TO SECOND)",
        ]);
    }

    [JavaOracleFact]
    public void DateTruncMillenniumAndTimeMatchJdbc()
    {
        ExplainOracleTests.Check([
            "SELECT CAST(DATE_TRUNC('MILLENNIUM', DATE '1999-06-01') AS VARCHAR)",
            "SELECT CAST(DATE_TRUNC('MILLENNIUM', "
                + "TIMESTAMP WITH TIME ZONE '1999-06-01 00:00:00+02') AS VARCHAR)",
            "SELECT CAST(DATE_TRUNC('SECOND', TIME '10:00:00.123') AS VARCHAR)",
            "SELECT CAST(DATE_TRUNC('QUARTER', TIME '10:00:00') AS VARCHAR)",
            "SELECT CAST(DATE_TRUNC('YEAR', TIME WITH TIME ZONE '10:00:00+02') AS VARCHAR)",
            // The last day of a week year takes the next-year start as its truncation.
            "SELECT CAST(DATE_TRUNC('WEEK_YEAR', DATE '2018-12-31') AS VARCHAR)",
            // Proleptic years before the common era take the negative-year branches.
            "SELECT CAST(DATE_TRUNC('DECADE', DATE '-0001-06-01') AS VARCHAR)",
            "SELECT CAST(DATE_TRUNC('CENTURY', DATE '-0100-06-01') AS VARCHAR)",
            "SELECT CAST(DATE_TRUNC('MILLENNIUM', DATE '-1500-06-01') AS VARCHAR)",
            // Fields without a truncation are refused.
            "SELECT DATE_TRUNC('TIMEZONE_HOUR', TIMESTAMP '2024-01-01 10:00:00')",
            "SELECT DATE_TRUNC('EPOCH', TIMESTAMP '2024-01-01 10:00:00')",
        ]);
    }

    [JavaOracleFact]
    public void DateDiffAcrossTimeZonesMatchJdbc()
    {
        ExplainOracleTests.Check([
            "SELECT DATEDIFF(HOUR, TIMESTAMP WITH TIME ZONE '2024-05-06 07:00:00+03', "
                + "TIMESTAMP WITH TIME ZONE '2024-05-06 09:00:00+05')",
            "SELECT DATEDIFF(SECOND, TIME WITH TIME ZONE '10:00:00+02', "
                + "TIME WITH TIME ZONE '09:00:00+05')",
            "SELECT DATEDIFF(DAY, TIMESTAMP WITH TIME ZONE '2024-05-06 23:00:00+03', "
                + "TIMESTAMP WITH TIME ZONE '2024-05-07 01:00:00+05')",
            // The earlier instant comes second: the count runs into negative days.
            "SELECT DATEDIFF(DAY, TIMESTAMP WITH TIME ZONE '2024-05-06 23:00:00+03', "
                + "TIMESTAMP WITH TIME ZONE '2024-05-05 01:00:00+05')",
        ]);
    }

    /// <summary>
    /// PostgreSQL numbers the days of the week from Sunday, which is a mode branch of the
    /// DOW extract. The mode is chosen when the database opens (D-029).
    /// </summary>
    [JavaOracleFact]
    public void PostgreSqlDayOfWeekMatchesJdbc()
    {
        ExplainOracleTests.Check([
            "SELECT EXTRACT(DOW FROM DATE '2024-05-06')",
            "SELECT EXTRACT(DOW FROM TIMESTAMP '2024-05-05 10:00:00')",
        ], "MODE=PostgreSQL");
    }
}
