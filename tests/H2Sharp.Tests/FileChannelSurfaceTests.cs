/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 288: the file channel's seek and length surface on an in-memory file, including the
 * negative-position and invalid-origin refusals that the ordinary file stores never ask for.
 */
using System;
using System.IO;
using H2Sharp.Store.FileSystem;
using Xunit;

namespace H2Sharp.Tests;

public sealed class FileChannelSurfaceTests
{
    [Fact]
    public void AChannelStreamSeeksAndRefusesBadPositions()
    {
        FilePath path = FilePath.Get("memFS:/file_channel_" + Guid.NewGuid().ToString("N"));
        using FileChannel channel = path.Open("rw");
        Stream stream = channel.Stream;

        Assert.True(stream.CanWrite);
        Assert.True(channel.TryLock());
        channel.ReleaseLock();

        Assert.Equal(0, stream.Seek(0, SeekOrigin.Begin));
        stream.Write(new byte[] { 1, 2, 3 });
        Assert.Equal(2, stream.Seek(-1, SeekOrigin.Current));
        Assert.Equal(3, stream.Seek(0, SeekOrigin.End));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(0, (SeekOrigin)99));
        Assert.Throws<IOException>(() => stream.Seek(-10, SeekOrigin.Begin));

        stream.SetLength(10);
        Assert.Equal(10, stream.Length);
        channel.Flush(false);
    }
}
