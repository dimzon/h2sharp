/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 114: the date-time arithmetic edges — month addition over a short month,
 * a timestamp plus an interval across midnight, the week-year truncations at the
 * year edge, single-digit date fields, the difference across a day boundary, and
 * the refusals of an unparsable time-with-zone and an out-of-range time.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DateTimeArithmeticEdgeOracleTests
{
    [JavaOracleFact]
    public void NormalizationsAndWeekYearEdgesMatchJdbc()
    {
        ExplainOracleTests.Check([
            "SELECT CAST(DATEADD('MONTH', 1, DATE '2020-01-31') AS VARCHAR)",
            "SELECT CAST(DATE '2020-01-31' + 1 AS VARCHAR)",
            "SELECT CAST(DATE '2020-1-2' AS VARCHAR)",
            "SELECT CAST(TIMESTAMP '2024-01-01 23:00:00' + INTERVAL '2' HOUR AS VARCHAR)",
            "SELECT DATEDIFF(DAY, TIMESTAMP '2024-01-01 23:00:00', TIMESTAMP '2024-01-02 01:00:00')",
            "SELECT CAST(DATE_TRUNC('WEEK_YEAR', DATE '2020-12-31') AS VARCHAR)",
            "SELECT CAST(DATE_TRUNC('ISO_WEEK_YEAR', DATE '2021-01-01') AS VARCHAR)",
            "SELECT TIME WITH TIME ZONE 'bad'",
            "SELECT TIME '23:00:00' + INTERVAL '2' HOUR",
        ]);
    }
}
