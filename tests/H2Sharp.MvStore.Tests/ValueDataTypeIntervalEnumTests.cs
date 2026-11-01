// Copyright 2026 H2Sharp contributors.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
using H2Sharp.Api;
using H2Sharp.MvStore.Db;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.MvStore.Tests;

/// <summary>
/// The store serialization of intervals and enums: the row format must round-trip them.
/// </summary>
public sealed class ValueDataTypeIntervalEnumTests
{
    [Fact]
    public void IntervalsRoundTrip()
    {
        ValueDataType dataType = new(null, CompareMode.GetInstance(null, 0), null, null);
        foreach (IntervalQualifier qualifier in System.Enum.GetValues<IntervalQualifier>())
        {
            foreach (bool negative in new[] { false, true })
            {
                long leading = 5;
                long remaining = qualifier < IntervalQualifier.Second ? 0 : 9;
                ValueInterval value = ValueInterval.From(qualifier, negative, leading, remaining);

                WriteBuffer buff = new();
                dataType.Write(buff, value);
                byte[] bytes = buff.ToArray();
                int position = 0;
                ValueInterval read = (ValueInterval)dataType.Read(bytes, ref position);

                Assert.Equal(bytes.Length, position);
                Assert.Equal(value.GetQualifier(), read.GetQualifier());
                Assert.Equal(value.IsNegative(), read.IsNegative());
                Assert.Equal(value.GetLeading(), read.GetLeading());
                Assert.Equal(value.GetRemaining(), read.GetRemaining());
            }
        }
    }

    [Fact]
    public void EnumOrdinalRoundTrip()
    {
        ValueDataType dataType = new(null, CompareMode.GetInstance(null, 0), null, null);
        WriteBuffer buff = new();
        buff.Put(Enum).PutVarInt(3);
        byte[] bytes = buff.ToArray();
        int position = 0;

        ValueInteger read = (ValueInteger)dataType.Read(bytes, ref position);
        Assert.Equal(3, read.GetInt());
        Assert.Equal(bytes.Length, position);
    }

    private const byte Enum = 25;
}
