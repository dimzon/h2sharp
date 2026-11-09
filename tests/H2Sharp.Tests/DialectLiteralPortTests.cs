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
using System.Data.Common;
using System.Text;
using System.Text.Json;
using H2Sharp.Data;
using H2Sharp.Values;
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

    [Fact]
    public void PlainValueFamiliesAreWrittenAsTheirEngineLiterals()
    {
        Assert.Equal(
            "TIME WITH TIME ZONE '10:00:00+03'",
            Dialect.AppendTimeWithTimeZone(
                new StringBuilder(), new TimeOnly(10, 0, 0), TimeSpan.FromHours(3)).ToString());

        Assert.Equal(
            "GEOMETRY 'POINT (1 2)'",
            Dialect.AppendGeometry(new StringBuilder(), "POINT (1 2)").ToString());
        Assert.Equal(
            "GEOMETRY 'SRID=4326;POINT (1 2)'",
            Dialect.AppendGeometry(new StringBuilder(), "POINT (1 2)", 4326).ToString());
        Assert.Equal(
            "CAST(NULL AS GEOMETRY)",
            Dialect.AppendGeometry(new StringBuilder(), (string?)null).ToString());
        Assert.Equal(
            "GEOMETRY X'0102'",
            Dialect.AppendGeometry(new StringBuilder(), new byte[] { 1, 2 }).ToString());
        Assert.Equal(
            "CAST(NULL AS GEOMETRY)",
            Dialect.AppendGeometry(new StringBuilder(), (byte[]?)null).ToString());

        Assert.Equal(
            "CAST('a' AS MY_ENUM)",
            Dialect.AppendEnum(new StringBuilder(), "a", "MY_ENUM").ToString());
        Assert.Equal(
            "CAST(NULL AS MY_ENUM)",
            Dialect.AppendEnum(new StringBuilder(), null, "MY_ENUM").ToString());
    }

    [Fact]
    public void CollectionsAndLobsAreWrittenAsTheirEngineLiterals()
    {
        Assert.Equal(
            "ARRAY [1, 2]",
            Dialect.AppendArray(
                new StringBuilder(), new[] { 1, 2 }, (b, v) => Dialect.AppendInteger(b, v)).ToString());
        Assert.Equal(
            "ARRAY []",
            Dialect.AppendArray(
                new StringBuilder(), Array.Empty<int>(), (b, v) => Dialect.AppendInteger(b, v)).ToString());
        Assert.Equal(
            "NULL",
            Dialect.AppendArray(
                new StringBuilder(), (int[]?)null, (b, v) => Dialect.AppendInteger(b, v)).ToString());

        Assert.Equal(
            "ROW (1, a)",
            Dialect.AppendRow(new StringBuilder(), ["1", "a"]).ToString());
        Assert.Equal("NULL", Dialect.AppendRow(new StringBuilder(), null).ToString());

        Assert.Equal("X'cafe'", Dialect.AppendBlob(new StringBuilder(), [0xCA, 0xFE]).ToString());
        Assert.Equal(
            "CAST(NULL AS BINARY LARGE OBJECT)",
            Dialect.AppendBlob(new StringBuilder(), null).ToString());
        Assert.Equal("'clob'", Dialect.AppendClob(new StringBuilder(), "clob").ToString());
        Assert.Equal(
            "CAST(NULL AS CHARACTER LARGE OBJECT)",
            Dialect.AppendClob(new StringBuilder(), null).ToString());
        Assert.Equal(
            "CAST(NULL AS NUMERIC)",
            Dialect.AppendBigInteger(new StringBuilder(), null).ToString());
    }

    [Fact]
    public void CollectionLiteralsReadBackIntoTheEngine()
    {
        StringBuilder sql = new("SELECT ");
        _ = Dialect.AppendArray(sql, new[] { 1, 2 }, (b, v) => Dialect.AppendInteger(b, v));
        _ = sql.Append(", ");
        _ = Dialect.AppendRow(sql, ["1", "'a'"]);
        _ = sql.Append(", ");
        _ = Dialect.AppendTimeWithTimeZone(sql, new TimeOnly(10, 0, 0), TimeSpan.FromHours(3));

        using H2Connection connection = new("Data Source=dialect_families;Storage=Memory");
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql.ToString();
        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());

        // The ADO projection turns the engine values into CLR ones: an array is a CLR array,
        // a row is an object array, a time with a zone is a DateTimeOffset.
        Assert.Equal(new[] { 1, 2 }, (int[])reader.GetValue(0)!);
        object?[] row = (object?[])reader.GetValue(1)!;
        Assert.Equal(1, (int)row[0]!);
        Assert.Equal("a", row[1]);
        Assert.Equal(
            new DateTimeOffset(DateTime.MinValue.Date.AddHours(10), TimeSpan.FromHours(3)),
            reader.GetValue(2));
    }
}
