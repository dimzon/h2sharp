/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 241: the SYSDATE/SYSTIMESTAMP value functions answer the session time in their own
 * type and keep the scale that was written in their SQL rendering.
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Modes;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class CompatibilityDateTimeValueFunctionTests
{
    [Fact]
    public void SysdateTruncatesToSecondsAndSystimestampKeepsItsScale()
    {
        Database database = new(ConnectionInfo.Memory("compat_datetime"));
        SessionLocal session = new(database, "sa");
        try
        {
            CompatibilityDateTimeValueFunction sysdate = new(
                CompatibilityDateTimeValueFunction.Sysdate, -1);
            Value value = sysdate.GetValue(session);
            Assert.Equal(Value.Timestamp, value.GetValueType());
            Assert.Equal(0, ((ValueTimestamp)value).GetTimeNanos() % 1_000_000_000);
            Assert.Equal("SYSDATE", sysdate.GetName());
            Assert.False(sysdate.IsEverything(ExpressionVisitor.DeterministicVisitor));
            Assert.Equal(1, sysdate.GetCost());

            CompatibilityDateTimeValueFunction timestamp = new(
                CompatibilityDateTimeValueFunction.Systimestamp, 3);
            Assert.Equal("SYSTIMESTAMP(3)", timestamp.GetUnenclosedSql(new StringBuilder(), 0).ToString());
            Assert.Equal(Value.TimestampTz, timestamp.GetValue(session).GetValueType());
            Assert.Equal("SYSTIMESTAMP", timestamp.GetName());
        }
        finally
        {
            database.Close();
        }
    }
}
