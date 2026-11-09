/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of NullValueDataType's array write/read and Compare/BinarySearch.
 */
using System;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Db;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class NullValueDataTypeCoverageTests
{
    [Fact]
    public void CompareAndBinarySearchReturnZero()
    {
        NullValueDataType t = NullValueDataType.Instance;
        Assert.Equal(0, t.Compare(ValueNull.Instance, ValueNull.Instance));
        Value[] storage = new Value[4];
        Assert.Equal(0, t.BinarySearch(ValueNull.Instance, storage, 4, 0));
    }

    [Fact]
    public void ArrayWriteAndReadRoundTrip()
    {
        NullValueDataType t = NullValueDataType.Instance;
        Value[] storage = new Value[3];
        var buff = new WriteBuffer();
        t.Write(buff, storage, 3);
        byte[] bytes = buff.ToArray();
        int position = 0;
        t.Read(bytes, storage, 3, ref position);
        foreach (Value v in storage)
        {
            Assert.Same(ValueNull.Instance, v);
        }
    }
}