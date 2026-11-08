/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of MemoryFilePath: root delete, move of a missing file, lock
 * contention, negative length, growth, pin/unpin, stream.
 */
using System;
using System.IO;
using H2Sharp.Store.FileSystem;
using Xunit;

namespace H2Sharp.Tests;

public sealed class MemoryFilePathCoverageTests : IDisposable
{
    private readonly string root;

    public MemoryFilePathCoverageTests()
    {
        root = "memFS:/mem_cov_" + Guid.NewGuid().ToString("N");
    }

    public void Dispose()
    {
        try { FilePath.Get(root).Delete(); } catch { }
    }

    [Fact]
    public void MemoryFileHandlesItsEdges()
    {
        // Deleting the root does nothing.
        FilePath.Get("memFS:").Delete();

        // Moving a missing file creates it at the destination.
        string src = root + "/src";
        string dst = root + "/dst";
        FilePath.Get(src).MoveTo(FilePath.Get(dst), false);
        Assert.True(FilePath.Get(dst).Exists());

        using FileChannel channel = FileUtils.Open(dst, "rw");

        // Locking twice: the second answers false.
        Assert.True(channel.TryLock());
        Assert.False(channel.TryLock());
        channel.ReleaseLock();

        // A negative length refuses.
        Assert.Throws<ArgumentOutOfRangeException>(() => channel.SetLength(-1));

        // Growing the file covers the capacity-growth path.
        channel.SetLength(100);
        Assert.Equal(100, channel.Length);

        // EnsureCapacity's no-op branch (needed <= capacity).
        channel.SetLength(50);

        // The stream view and pin/unpin are exercised.
        using Stream stream = channel.Stream;
        stream.WriteByte(7);
        stream.Position = 0;
        Assert.Equal(7, stream.ReadByte());

        // Reading past the end returns nothing.
        channel.Position = channel.Length + 5;
        Assert.Equal(0, channel.Read(new byte[4]));
    }
}
