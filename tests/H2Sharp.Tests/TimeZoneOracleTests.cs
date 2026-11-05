/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * AT TIME ZONE / AT LOCAL and the time-with-time-zone literal forms: the operator
 * converts a timestamp (or time) into a zone, and a TIME '...' literal with an offset
 * is accepted where a TIME WITH TIME ZONE column is. Both compare with H2 2.5 JDBC.
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class TimeZoneOracleTests
{
    [JavaOracleFact]
    public void AtTimeZoneMatchesJdbc()
    {
        FreshIssuesOracleTests.Compare([
            "SELECT TIMESTAMP '2024-01-01 10:00:00' AT TIME ZONE 'UTC'",
            "SELECT TIMESTAMP '2024-01-01 10:00:00' AT LOCAL",
            "SELECT TIME '10:00:00' AT TIME ZONE 'UTC'",
            // An interval is a valid zone offset.
            "SELECT TIMESTAMP '2024-01-01 10:00:00' AT TIME ZONE INTERVAL '2' HOUR",
            "SELECT TIMESTAMP '2024-01-01 10:00:00+02:00' AT TIME ZONE 'UTC'",
            // A timestamp with a zone converted to LOCAL keeps the instant.
            "SELECT TIMESTAMP '2024-01-01 10:00:00+02:00' AT LOCAL",
            // The operator type-checks its left operand.
            "SELECT DATE '2024-01-01' AT TIME ZONE 'UTC'",
        ]);
    }

    [JavaOracleFact]
    public void TimeWithTimeZoneRoundTrips()
    {
        FreshIssuesOracleTests.Compare([
            "CREATE TABLE T(ID INT PRIMARY KEY, W TIME WITH TIME ZONE)",
            "INSERT INTO T VALUES (1, TIME '10:00:00+02:00')",
            "SELECT W FROM T",
            "SELECT TIME '10:00:00+02:00'",
            "SELECT TIME '10:00:00Z'",
            "DROP TABLE T",
        ]);
    }
}
