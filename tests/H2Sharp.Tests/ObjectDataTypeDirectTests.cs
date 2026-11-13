/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of ObjectDataType: auto-detect dispatch, inner-type
 * read/write/compare round trips, and the array memory path.
 */
using System;
using System.Numerics;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Type;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ObjectDataTypeDirectTests
{
    [Fact]
    public void RoundTripEveryInnerType()
    {
        var ot = new ObjectDataType();
        object?[] cases =
        {
            true, false,
            (sbyte)-7, (sbyte)7,
            (short)-12345, (short)12345,
            'A', 'Z',
            -1000000, 1000000,
            -1000000000000L, 1000000000000L,
            -1.5f, 2.5f,
            -Math.PI, Math.E,
            new BigInteger(12345678901234567890),
            "hello", "",
            Guid.NewGuid(),
            new sbyte[] { 1, 2, 3 },
            new int[] { 1, 2, 3 },
            new long[] { 7L },
            new double[] { 1.5 },
            new bool[] { true, false },
            new char[] { 'a', 'b' },
            new float[] { 0.5f },
            new short[] { 9 },
        };

        foreach (object? value in cases)
        {
            try
            {
                var buff = new WriteBuffer();
                ot.Write(buff, value);
                int pos = 0;
                object? back = ot.Read(buff.ToArray(), ref pos);
                Assert.NotNull(back);
                var b = back!;
                Assert.Equal(value!.GetType(), b.GetType());
                Assert.Equal(0, ot.Compare(value!, b));
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed on value={value} ({value?.GetType()})", ex);
            }
        }
    }

    [Fact]
    public void DateTimeAndArrayReachTypeTests()
    {
        var ot = new ObjectDataType();
        // GetMemory(DateTime) -> SwitchType -> GetTypeId -> IsDate (line 301).
        Assert.True(ot.GetMemory(new DateTime(2020, 1, 1)) > 0);
        // GetMemory(array) -> SwitchType -> GetTypeId -> IsArray (line 303).
        Assert.True(ot.GetMemory(new int[1]) > 0);
    }

    [Fact]
    public void CompareByteArrays()
    {
        byte[] a = { 1, 2, 3 };
        byte[] b = { 1, 2, 3 };
        byte[] c = { 1, 2, 4 };
        byte[] d = { 1, 2 };
        Assert.Equal(0, ObjectDataType.CompareNotNull(a, b));
        Assert.True(ObjectDataType.CompareNotNull(a, c) < 0);
        Assert.True(ObjectDataType.CompareNotNull(c, a) > 0);
        Assert.True(ObjectDataType.CompareNotNull(a, d) > 0);
        Assert.True(ObjectDataType.CompareNotNull(d, a) < 0);
    }

    [Fact]
    public void CreateStorageAndMemoryAndWrite()
    {
        var ot = new ObjectDataType();
        object?[] storage = ot.CreateStorage(3);
        Assert.Equal(3, storage.Length);
        Assert.True(ot.GetMemory("abc") > 0);
        var buff = new WriteBuffer();
        ot.Write(buff, "abc");
        Assert.True(buff.Position() > 0);
    }

    [Fact]
    public void NullTypeReadWrite()
    {
        var ot = new ObjectDataType();
        var buff = new WriteBuffer();
        ot.Write(buff, null);
        int pos = 0;
        Assert.Null(ot.Read(buff.ToArray(), ref pos));
    }
}
