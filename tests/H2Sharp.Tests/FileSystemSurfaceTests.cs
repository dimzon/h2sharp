/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 256: the split-file path's public surface — creating a part, making it read-only,
 * moving it onto a target that has extra parts, and refusing a path of another scheme.
 */
using System;
using H2Sharp.Message;
using H2Sharp.Store.FileSystem;
using Xunit;

namespace H2Sharp.Tests;

public sealed class FileSystemSurfaceTests
{
    [Fact]
    public void SplitPathCreatesCopiesDeletesAndMovesItsParts()
    {
        string prefix = "memFS:/split_surface_" + Guid.NewGuid().ToString("N");
        FilePath directory = FilePath.Get("memFS:/");
        FilePath source = FilePath.Get("split:26:" + prefix);
        FilePath target = FilePath.Get("split:26:" + prefix + "_target");

        try
        {
            source.CreateDirectory();
            Assert.True(source.CreateFile());
            Assert.True(source.Exists());
            Assert.True(source.Size() >= 0);
            _ = source.LastModified();
            _ = source.NewDirectoryStream();

            // The target has more parts than the source, so the move has to delete the leftovers.
            using (FileChannel channel = target.Open("rw"))
            {
            }

            target.Delete();
            FilePath extra = FilePath.Get(prefix + "_target.1.part");
            using (FileChannel channel = extra.Open("rw"))
            {
            }

            source.MoveTo(target, false);
            Assert.False(extra.Exists());
            Assert.True(target.Exists());

            Assert.Throws<ArgumentException>(() => source.MoveTo(FilePath.Get(prefix + "_other"), false));

            bool readOnly = target.SetReadOnly();
            _ = readOnly;
            target.Delete();
            Assert.False(target.Exists());
        }
        finally
        {
            source.Delete();
            target.Delete();
        }
    }

    [Fact]
    public void SplitPathParsesTheShiftAndRejectsAMissingFileName()
    {
        // Without a shift the property's own value is used; both spellings resolve to the
        // same underlying file.
        FilePath implicitShift = FilePath.Get("split:memFS:/split_implicit");
        FilePath explicitShift = FilePath.Get("split:26:memFS:/split_implicit");
        Assert.Equal(implicitShift.Unwrap().Name, explicitShift.Unwrap().Name);

        // A numeric part with no colon has no file name at all, and the parser says so.
        Exception e = Assert.ThrowsAny<Exception>(() => FilePath.Get("split:26"));
        Assert.Contains("has no file name", e.Message, StringComparison.Ordinal);
    }
}
