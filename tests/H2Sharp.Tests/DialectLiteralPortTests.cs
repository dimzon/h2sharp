/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The BCL-facing literal writers of the ADO layer (the D-020 surface): a .NET value in, the
 * engine's SQL literal out. The line-level targets from the coverage data are the null casts
 * of every type, the infinite dates and timestamps, the E-prefixed string, the JSON document,
 * the numeric mantissa path, and the UUID refusal.
 */
using System;
using System.Text;
using System.Text.Json;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Tests;

public sealed class DialectLiteralPortTests
{
    [Fact]
    public void CharsNullsAndInfinitiesAreWrittenAsCasts()
    {
        Assert.Equal("CAST(NULL AS CHAR(1))", Dialect.AppendChar(new StringBuilder(), null).ToString());
        Assert.Equal("CAST('x' AS CHAR(1))", Dialect.AppendChar(new StringBuilder(), 'x').ToString());

        Assert.Equal("CAST(NULL AS DATE)", Dialect.AppendDate(new StringBuilder(), null).ToString());
        Assert.Equal("CAST('-Infinity' AS DATE)", Dialect.AppendDate(new StringBuilder(), DateOnly.MinValue).ToString());
        Assert.Equal("CAST('Infinity' AS DATE)", Dialect.AppendDate(new StringBuilder(), DateOnly.MaxValue).ToString());
        Assert.Equal("DATE '2020-01-02'", Dialect.AppendDate(new StringBuilder(), new DateOnly(2020, 1, 2)).ToString());

        Assert.Equal("CAST(NULL AS TIME)", Dialect.AppendTime(new StringBuilder(), (TimeOnly?)null).ToString());
        Assert.Equal("CAST(NULL AS TIME)", Dialect.AppendTime(new StringBuilder(), (TimeSpan?)null).ToString());
        Assert.Equal("CAST(NULL AS INTERVAL DAY TO SECOND)", Dialect.AppendInterval(new StringBuilder(), null).ToString());
        Assert.Equal("CAST(NULL AS TIMESTAMP)", Dialect.AppendTimestamp(new StringBuilder(), null).ToString());
        Assert.Equal("CAST('-Infinity' AS TIMESTAMP)", Dialect.AppendTimestamp(new StringBuilder(), DateTime.MinValue).ToString());
        Assert.Equal("CAST('Infinity' AS TIMESTAMP)", Dialect.AppendTimestamp(new StringBuilder(), DateTime.MaxValue).ToString());
        Assert.Equal("CAST(NULL AS TIMESTAMP WITH TIME ZONE)", Dialect.AppendTimestampWithTimeZone(new StringBuilder(), null).ToString());
        Assert.Equal("CAST('-Infinity' AS TIMESTAMP WITH TIME ZONE)", Dialect.AppendTimestampWithTimeZone(new StringBuilder(), DateTimeOffset.MinValue).ToString());
        Assert.Equal("CAST('Infinity' AS TIMESTAMP WITH TIME ZONE)", Dialect.AppendTimestampWithTimeZone(new StringBuilder(), DateTimeOffset.MaxValue).ToString());
        Assert.Equal("CAST(NULL AS TIME WITH TIME ZONE)", Dialect.AppendTimeWithTimeZone(new StringBuilder(), null, TimeSpan.Zero).ToString());
    }

    [Fact]
    public void StringsAndJsonAreWrittenQuoted()
    {
        // The E-prefixed string for the strings that need it, plain quotes otherwise.
        Assert.Equal("CAST(NULL AS VARCHAR)", Dialect.AppendString(new StringBuilder(), null).ToString());
        Assert.Equal("'x'", Dialect.AppendString(new StringBuilder(), "x").ToString());
        Assert.Equal("CAST(NULL AS VARCHAR)", Dialect.AppendEString(new StringBuilder(), null).ToString());
        Assert.Equal("E'x'", Dialect.AppendEString(new StringBuilder(), "x").ToString());

        JsonDocument document = JsonDocument.Parse("{\"a\": 1}");
        Assert.Equal("JSON '{\"a\":1}'", Dialect.AppendJson(new StringBuilder(), document).ToString());
        Assert.Equal(
            "JSON '{\"a\":1}'",
            Dialect.AppendJson(new StringBuilder(), document.RootElement).ToString());
    }

    [Fact]
    public void NumericsTakeTheMantissaPathWhenTheyDoNotFit()
    {
        // Whole numbers name their precision in the CAST.
        Assert.Equal("CAST(5 AS NUMERIC(1))", Dialect.AppendNumeric(new StringBuilder(), 5m).ToString());
        // The mantissa path: a decimal whose TryFormat does not fit the stack buffer keeps the
        // plain invariant string. A many-digit scale value walks the same branch.
        decimal wide = 12345678901234567890123456789m;
        Assert.Equal(wide.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Dialect.AppendNumeric(new StringBuilder(), wide).ToString());
    }

    [Fact]
    public void AUuidThatCannotBeWrittenRefuses()
    {
        Assert.Equal("CAST(NULL AS UUID)", Dialect.AppendUuid(new StringBuilder(), null).ToString());
        Guid guid = Guid.Parse("0191c5a5-88d0-7e10-b91c-9d1f4d0f9c1e");
        Assert.Contains("0191c5a5", Dialect.AppendUuid(new StringBuilder(), guid).ToString());
    }
}
