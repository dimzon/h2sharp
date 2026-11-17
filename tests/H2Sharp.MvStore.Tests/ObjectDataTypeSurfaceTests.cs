/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 259: the object data type's tagged serialization — every value shape the file store
 * can name must survive a write/read round-trip with the read position advancing.
 */
using System;
using System.Linq;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Type;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class ObjectDataTypeSurfaceTests
{
    [Fact]
    public void TaggedValuesSurviveAWriteReadRoundTrip()
    {
        ObjectDataType type = new();
        Guid guid = Guid.NewGuid();
        object?[] values =
        [
            null, "text", 42L, 123, 1.5, 2.5f, true, guid,
            new byte[] { 1, 2, 3 }, new int[] { 4, 5, 6 },
        ];
        WriteBuffer buffer = new();
        foreach (object? value in values)
        {
            type.Write(buffer, value);
        }

        byte[] bytes = buffer.ToArray();
        Assert.True(bytes.Length > 0);
        int position = 0;
        Assert.Null(type.Read(bytes, ref position));
        Assert.Equal("text", type.Read(bytes, ref position));
        Assert.Equal(42L, type.Read(bytes, ref position));
        Assert.Equal(123, type.Read(bytes, ref position));
        Assert.Equal(1.5, type.Read(bytes, ref position));
        Assert.Equal(2.5f, type.Read(bytes, ref position));
        Assert.Equal(true, type.Read(bytes, ref position));
        Assert.Equal(guid, type.Read(bytes, ref position));
        Assert.True(((byte[])type.Read(bytes, ref position)!).SequenceEqual(new byte[] { 1, 2, 3 }));
        Assert.True(((int[])type.Read(bytes, ref position)!).SequenceEqual(new[] { 4, 5, 6 }));
        Assert.Equal(bytes.Length, position);

        Assert.True(type.GetMemory("text") > 0);
        Assert.True(type.Compare("a", "b") < 0);
        Assert.Equal(0, type.Compare(42L, 42L));
    }
}
