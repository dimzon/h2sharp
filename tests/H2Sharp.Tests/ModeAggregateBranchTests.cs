/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 292: MODE's getAggregatedValue branch with no ORDER BY list. The SQL parser always
 * attaches an order to MODE(...), so this branch can only be driven through the aggregate's own
 * API; the probe class exposes GetAggregatedValue without changing the port.
 */
using System.Reflection;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Aggregate;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ModeAggregateBranchTests
{
    private sealed class ModeProbe : Aggregate
    {
        internal ModeProbe() : base(AggregateType.Mode, [], null!, false)
        {
        }

        internal Value Evaluate(SessionLocal session, AggregateData data)
            => GetAggregatedValue(session, data);
    }

    [Fact]
    public void ModeWithoutOrderChoosesTheMostFrequentValue()
    {
        Database database = new(ConnectionInfo.Memory("mode_no_order"));
        SessionLocal session = new(database, "sa");
        try
        {
            ModeProbe probe = new();
            FieldInfo orderBy = typeof(Aggregate).GetField(
                "orderByList", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Null(orderBy.GetValue(probe));

            AggregateDataDistinctWithCounts data = new(ignoreNulls: true, int.MaxValue);
            data.Add(session, ValueInteger.Get(1));
            data.Add(session, ValueInteger.Get(1));
            data.Add(session, ValueInteger.Get(2));

            Value result = probe.Evaluate(session, data);
            Assert.Equal(1, result.GetInt());
        }
        finally
        {
            database.Close();
        }
    }
}
