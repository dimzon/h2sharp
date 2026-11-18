/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 293: concrete non-defensive branches that SQL does not reach — the object type's
 * predicates and storage allocation, IndexCursor's max selection, and the aggregate's extra
 * argument getter.
 */
using System;
using System.Reflection;
using H2Sharp.Engine;
using H2Sharp.Expressions.Aggregate;
using H2Sharp.MvStore.Type;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class FunctionalUncoveredTests
{
    // AutoDetectDataType is internal with an internal constructor; the test assembly is a friend,
    // so it can subclass and drive its CreateStorage override directly (no SQL path reaches it).
    private sealed class ProbeType : ObjectDataType.AutoDetectDataType
    {
        internal ProbeType() : base(0)
        {
        }

        public override object?[] Read(Span<byte> buff, ref int position) => throw new NotSupportedException();

        internal override object? ReadTagged(Span<byte> buff, ref int position, int tag)
            => throw new NotSupportedException();
    }

    [Fact]
    public void ObjectDataTypeKnowsItsShapes()
    {
        Assert.True(ObjectDataType.IsDate(DateTime.UnixEpoch));
        Assert.False(ObjectDataType.IsDate("not a date"));
        Assert.True(ObjectDataType.IsArray(new int[0]));
        Assert.False(ObjectDataType.IsArray("not an array"));
        Assert.Equal(3, new ObjectDataType().CreateStorage(3).Length);
        Assert.Equal(4, new ProbeType().CreateStorage(4).Length);
    }

    [Fact]
    public void IndexCursorChoosesTheMaxValue()
    {
        Database database = new(ConnectionInfo.Memory("index_cursor_max"));
        SessionLocal session = new(database, "sa");
        try
        {
            IndexCursor cursor = new();
            typeof(IndexCursor).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(cursor, session);

            Value one = ValueInteger.Get(1);
            Value two = ValueInteger.Get(2);
            Assert.Same(two, cursor.GetMax(one, two, bigger: true));
            Assert.Same(one, cursor.GetMax(one, two, bigger: false));
            Assert.Same(one, cursor.GetMax(one, null, bigger: true));
            Assert.Same(two, cursor.GetMax(null, two, bigger: true));
            Assert.Same(two, cursor.GetMax(ValueNull.Instance, two, bigger: true));
            Assert.Same(one, cursor.GetMax(one, ValueNull.Instance, bigger: true));
            Assert.Same(one, cursor.GetMax(one, one, bigger: true));
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void AnAggregateReportsItsExtraArguments()
    {
        Aggregate aggregate = new(AggregateType.Mode, [], null!, false);
        Assert.Null(aggregate.GetExtraArguments());
    }
}
