/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 254: value-layer edges that SQL cannot reach — TO_DATE format tokens the reference
 * scripts do not use, the LIKE matcher's escape forms, a row's own type and equality, and
 * the ENUM definition's ordinal and label lookups.
 */
using System;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Modes;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class CoreEdgeSurfaceTests
{
    private static SessionLocal Session()
        => new(new Database(ConnectionInfo.Memory("core_edge_" + Guid.NewGuid().ToString("N"))), "sa");

    [Fact]
    public void ToDateFormatTokensCoverTheirOwnForms()
    {
        SessionLocal session = Session();
        try
        {
            Assert.Equal(2202, Year(ToDateParser.ToDate(session, "202", "YYY")));
            Assert.Equal(2004, Year(ToDateParser.ToDate(session, "04", "RRRR")));
            Assert.Equal(2, Number("IV", "RM"));
            Assert.Equal(1, Day("1", "D"));
            Assert.Equal(9, Day("2024-03-09", "YYYY-MM-DD"));

            Assert.ThrowsAny<H2SharpException>(() => ToDateParser.ToDate(session, "1", "Q"));
            Assert.ThrowsAny<H2SharpException>(() => ToDateParser.ToDate(session, "24", "EE"));
            Assert.ThrowsAny<H2SharpException>(() => ToDateParser.ToDate(session, "24", "E"));
        }
        finally
        {
            session.GetDatabase().Close();
        }

        static int Year(ValueTimestamp value)
            => DateTimeUtils.YearFromDateValue(value.GetDateValue());

        static int Number(string input, string format)
        {
            SessionLocal s = Session();
            try
            {
                ValueTimestamp value = ToDateParser.ToDate(s, input, format);
                return DateTimeUtils.MonthFromDateValue(value.GetDateValue());
            }
            finally
            {
                s.GetDatabase().Close();
            }
        }

        static int Day(string input, string format)
        {
            SessionLocal s = Session();
            try
            {
                ValueTimestamp value = ToDateParser.ToDate(s, input, format);
                return DateTimeUtils.DayFromDateValue(value.GetDateValue());
            }
            finally
            {
                s.GetDatabase().Close();
            }
        }
    }

    [Fact]
    public void LikeMatcherHandlesEscapesAndWildcards()
    {
        const char escape = '\\';
        SessionLocal session = Session();
        CompareLike like = new(session.GetDatabase().GetCompareMode(), "\\", null, false, false,
            null, null, CompareLike.LikeType.Like);
        try
        {
            Assert.True(like.Test("a%c", "abc", escape));
            Assert.True(like.Test("a_", "ab", escape));
            Assert.True(like.Test("a\\_c", "a_c", escape));
            Assert.True(like.Test("a\\%%", "a%", escape));
            Assert.False(like.Test("a%c", "AbC", escape));
            Assert.False(like.Test("a_cX", "aXc", escape));
        }
        finally
        {
            session.GetDatabase().Close();
        }
    }

    [Fact]
    public void RowValuesCarryTheirOwnTypeAndRejectBadShapes()
    {
        ValueRow row = ValueRow.Get([ValueInteger.Get(1), ValueVarchar.Get("a")]);
        Assert.Equal("ROW (1, a)", row.GetString());
        Assert.Equal(Value.Row, row.GetValueType());
        Assert.Equal(0, row.CompareTypeSafe(row, null!, null));
        Assert.False(row.Equals("not a row"));
        Assert.True(row.Equals(ValueRow.Get([ValueInteger.Get(1), ValueVarchar.Get("a")])));
        Assert.Equal(row.GetHashCode(), row.GetHashCode());

        ValueRow nullable = ValueRow.GetNullable([null, ValueNull.Instance]);
        Assert.Equal(2, ((ExtTypeInfoRow)nullable.GetTypeInfo().GetExtTypeInfo()!).FieldCount);

        Assert.ThrowsAny<H2SharpException>(() => ValueRow.Get(new Value[Constants.MaxColumns + 1]));
        Assert.ThrowsAny<Exception>(() => ValueRow.Get(
            new TypeInfo(Value.Integer, -1, -1, null), [ValueInteger.Get(1)]));
        Assert.ThrowsAny<Exception>(() => row.CloneWithOrder([0]));
    }

    [Fact]
    public void EnumLookupsUseOrdinalsLabelsAndTheProvider()
    {
        ExtTypeInfoEnum definition = new(["A", "B"]);
        Assert.Equal(2, definition.GetCount());
        Assert.Equal("A", definition.GetEnumerator(0));
        Assert.Equal("A", definition.GetValue(1, null).GetString());
        Assert.Equal("B", definition.GetValue(2, null).GetString());
        Assert.ThrowsAny<H2SharpException>(() => definition.GetValue(0, null));
        Assert.ThrowsAny<H2SharpException>(() => definition.GetValue(3, null));

        Assert.Equal("A", definition.GetValueOrNull(" a ", null)!.GetString());
        Assert.Null(definition.GetValueOrNull("X", null));
        Assert.ThrowsAny<H2SharpException>(() => definition.GetValue("X", null));

        TestCastDataProvider zeroBased = new();
        Assert.Equal("A", definition.GetValue(0, zeroBased).GetString());
        Assert.Equal("B", definition.GetValueOrNull("b", zeroBased)!.GetString());
        Assert.ThrowsAny<H2SharpException>(() => definition.GetValue(2, zeroBased));

        Assert.ThrowsAny<H2SharpException>(() => ExtTypeInfoEnum.GetEnumeratorsForBinaryOperation(
            ValueInteger.Get(1), ValueVarchar.Get("x")));
    }
}
