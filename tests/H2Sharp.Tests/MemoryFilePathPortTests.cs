/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The in-memory file system has no SQL of its own; the database reaches it through the file
 * store. These tests pin the paths the database itself does not walk: the directory listing,
 * the read-only and timestamp answers of the root, and the refusals.
 */
using System;
using System.IO;
using H2Sharp.Store.FileSystem;
using Xunit;

namespace H2Sharp.Tests;

public sealed class MemoryFilePathPortTests : IDisposable
{
    public void Dispose() => MemoryFilePath.Clear();

    [Fact]
    public void FilesAreCreatedAndDeletedByName()
    {
        FilePath path = FilePath.Get("memFS:/test/one");
        Assert.False(path.Exists());
        Assert.True(path.CreateFile());
        // Creating again answers false instead of truncating.
        Assert.False(path.CreateFile());
        Assert.True(path.Exists());
        Assert.True(path.IsRegularFile());
        Assert.False(path.IsDirectory());
        Assert.Equal(0, path.Size());

        path.Delete();
        Assert.False(path.Exists());
        // Deleting a missing file is not an error, and neither is deleting the root.
        path.Delete();
        FilePath.Get("memFS:/").Delete();
    }

    [Fact]
    public void DirectoryListsItsOwnPrefix()
    {
        _ = FilePath.Get("memFS:/dir/a").CreateFile();
        _ = FilePath.Get("memFS:/dir/b").CreateFile();
        _ = FilePath.Get("memFS:/other").CreateFile();

        // A directory in memory is only a name prefix; listing returns the files under it.
        System.Collections.Generic.IReadOnlyList<FilePath> entries =
            FilePath.Get("memFS:/dir").NewDirectoryStream();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.StartsWith("memFS:/dir/", e.Name));

        // The trailing-slash form lists the same entries.
        Assert.Equal(2, FilePath.Get("memFS:/dir/").NewDirectoryStream().Count);
    }

    [Fact]
    public void RootAnswersItsFixedFacts()
    {
        // The root is the bare scheme; only it is a directory.
        FilePath root = FilePath.Get("memFS:");
        Assert.True(root.IsDirectory());
        Assert.False(root.IsRegularFile());
        // The in-memory clock stands still and the files stay writable.
        Assert.Equal(0, root.LastModified());
        Assert.True(root.CanWrite());
        Assert.True(root.SetReadOnly());
        Assert.True(root.CanWrite());
    }

    [Fact]
    public void MoveRefusesATakenNameAndMovesOntoItselfKeepsTheFile()
    {
        FilePath source = FilePath.Get("memFS:/move/me");
        _ = source.CreateFile();
        FilePath target = FilePath.Get("memFS:/move/other");
        _ = target.CreateFile();

        // A taken name refuses without replace.
        Assert.Throws<H2Sharp.H2SharpException>(() => source.MoveTo(target, false));
        // The same name is not a move at all, and the file stays.
        source.MoveTo(source, false);
        Assert.True(source.Exists());
        // With replace the move lands.
        source.MoveTo(target, true);
        Assert.False(source.Exists());
        Assert.True(target.Exists());
    }
}
