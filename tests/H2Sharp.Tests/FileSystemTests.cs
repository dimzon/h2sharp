/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What the store will do to a file, done to both file systems.
 *
 * There is no reference to compare a file system against — a disk is not a function of its input —
 * so these are the store's own expectations written down: what a path answers, what a fresh file
 * answers, and that bytes go in and come out. The part that is a function of its input, which scheme
 * a path resolves to and what its name is, is compared against the reference in
 * FilePathOracleTests.
 *
 * Both file systems run the same body, because the point of the abstraction is that they answer
 * alike. Where the reference itself answers differently for memory than for disk, the case is named.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using H2Sharp.Store.FileSystem;
using Xunit;

namespace H2Sharp.Tests;

public sealed class FileSystemTests : IDisposable
{
    private readonly string directory;

    public FileSystemTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "h2sharp-fs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A file somebody still holds; the next run makes another directory.
        }
    }

    public static TheoryData<string> Schemes => new() { "file", "memFS" };

    /// <summary>
    /// A path in the file system named. A disk path goes inside this test's own directory: a bare
    /// name would be a file in the working directory, and every test in this class would be writing
    /// the same one at the same time. A memory path is one name in one map, and the methods of a
    /// class do not run at the same time, so the name itself is enough.
    /// </summary>
    /// <param name="scheme">the file system</param>
    /// <param name="name">the file</param>
    /// <returns>the path</returns>
    private FilePath At(string scheme, string name)
        => scheme == "memFS"
            ? FilePath.Get(scheme + ":" + name)
            : FilePath.Get(Path.Combine(directory, name).Replace('\\', '/'));

    [Theory]
    [MemberData(nameof(Schemes))]
    public void APathAnswersItsOwnScheme(string scheme)
    {
        FilePath path = At(scheme, "some/where/x.h2.db");
        Assert.Equal(scheme, path.GetScheme());
        Assert.Equal("x.h2.db", path.GetName());

        // The name is the file system's own, which is why the disk's has no "file:" in it, and
        // feeding the whole path back gets the same path again — which is what the store does when
        // it keeps a path in a configuration and opens it later.
        Assert.Equal(path.Name, FilePath.Get(path.ToString()).Name);
        Assert.Equal(scheme, FilePath.Get(path.ToString()).GetScheme());
    }

    [Fact]
    public void APathWithoutASchemeGoesToTheDisk()
    {
        // A path with no scheme is the disk's, whichever scheme the rest of the test uses.
        Assert.Equal("file", FilePath.Get("plain.h2.db").GetScheme());
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void TheNameIsWhatIsAfterTheLastSeparator(string scheme)
    {
        Assert.Equal("x.h2.db", At(scheme, "a/b/c/x.h2.db").GetName());
        Assert.Equal("x", FilePath.Get("memFS:x").GetName());
        Assert.Equal("bare", FilePath.Get("bare").GetName());
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void BytesGoInAndComeBackOut(string scheme)
    {
        FilePath path = At(scheme, "roundtrip.bin");
        Assert.True(path.CreateFile());
        Assert.True(path.Exists());

        byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8, 0, 255];
        using (FileChannel channel = path.Open("rw"))
        {
            channel.Position = 0;
            channel.Write(payload);
            channel.Flush(flushToDisk: false);
            Assert.Equal(payload.Length, channel.Length);
        }

        Assert.Equal(payload.Length, path.Size());

        using FileChannel read = path.Open("r");
        Assert.Equal(payload.Length, read.Length);
        byte[] back = new byte[payload.Length];
        read.Position = 0;
        int at = 0;
        while (at < back.Length)
        {
            int got = read.Read(back.AsSpan(at));
            Assert.True(got > 0, $"read stopped at {at}");
            at += got;
        }

        Assert.Equal(payload, back);

        // Reading at the end answers nothing rather than throwing, which is what the store does when
        // it asks for a block that is past the last one.
        read.Position = read.Length;
        Assert.Equal(0, read.Read(new byte[4]));
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void AShorterFileCutsWhatWasThere(string scheme)
    {
        FilePath path = At(scheme, "truncate.bin");
        using (FileChannel channel = path.Open("rw"))
        {
            channel.Write([1, 2, 3, 4, 5, 6, 7, 8]);
            channel.SetLength(3);
            Assert.Equal(3, channel.Length);
        }

        Assert.Equal(3, path.Size());

        // And it may be grown again, with the new space zeroed, which is what a store does before
        // it writes into a page it has just made room for.
        using (FileChannel channel = path.Open("rw"))
        {
            channel.SetLength(6);
            Assert.Equal(6, channel.Length);
            channel.Position = 0;
            byte[] back = new byte[6];
            Assert.Equal(6, channel.Read(back));
            Assert.Equal([1, 2, 3, 0, 0, 0], back);
        }
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void CreatingTwiceIsNotCreating(string scheme)
    {
        FilePath path = At(scheme, "once.bin");
        Assert.True(path.CreateFile());
        Assert.False(path.CreateFile());
        Assert.True(path.Exists());
        path.Delete();
        Assert.False(path.Exists());

        // Deleting what is not there is not an error, and neither is deleting twice.
        path.Delete();
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void APathCanBeMovedAndReplaced(string scheme)
    {
        FilePath first = At(scheme, "from.bin");
        FilePath second = At(scheme, "to.bin");
        using (FileChannel channel = first.Open("rw"))
        {
            channel.Write([7, 7, 7]);
        }

        first.MoveTo(second, atomicReplace: false);
        Assert.False(first.Exists());
        Assert.True(second.Exists());
        Assert.Equal(3, second.Size());

        // Onto a file that is already there, without a replace asked for, is a failure on both
        // sides: the reference moves with no options and the move refuses to overwrite. The
        // memory file system answers with the reference's own database error, the disk one with the
        // framework's, because the reference's disk path wraps it in the same error and ours does
        // not.
        using (FileChannel channel = first.Open("rw"))
        {
            channel.Write([8]);
        }

        Assert.ThrowsAny<Exception>(() => first.MoveTo(second, atomicReplace: false));
        Assert.Equal(3, second.Size());

        // And with the replace asked for, the old contents go.
        first.MoveTo(second, atomicReplace: true);
        Assert.Equal(1, second.Size());
        Assert.False(first.Exists());
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void AStreamIsTheSameBytesWithLessBookkeeping(string scheme)
    {
        FilePath path = At(scheme, "stream.bin");

        using (Stream output = path.NewOutputStream(append: false))
        {
            output.Write([1, 2, 3]);
        }

        Assert.Equal(3, path.Size());

        // Appending adds to what is there, and not appending cuts it.
        using (Stream output = path.NewOutputStream(append: true))
        {
            output.Write([4, 5]);
        }

        Assert.Equal(5, path.Size());
        Assert.Equal([1, 2, 3, 4, 5], FileUtils.ReadAllBytes(path));

        using (Stream output = path.NewOutputStream(append: false))
        {
            output.Write([6]);
        }

        Assert.Equal([6], FileUtils.ReadAllBytes(path));

        using Stream input = path.NewInputStream();
        using MemoryStream read = new();
        input.CopyTo(read);
        Assert.Equal([6], read.ToArray());
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void ATemporaryFileHasANameNobodyElseHas(string scheme)
    {
        FilePath first = At(scheme, "temp").CreateTempFile(".db", inTempDir: false);
        FilePath second = At(scheme, "temp").CreateTempFile(".db", inTempDir: false);

        Assert.NotEqual(first.Name, second.Name);
        Assert.StartsWith(At(scheme, "temp").Name, first.Name, StringComparison.Ordinal);
        Assert.EndsWith(".db", first.Name, StringComparison.Ordinal);
        Assert.True(first.Exists());

        first.Delete();
        second.Delete();
    }

    [Fact]
    public void ADirectoryIsOnlyADirectoryOnTheDisk()
    {
        // Memory has no directories: a name there is a file, and there is nothing to make.
        Assert.False(At("memFS", "dir").IsDirectory());
        Assert.False(At("memFS", "dir").Exists());
        At("memFS", "dir").CreateDirectory();
        Assert.False(At("memFS", "dir").Exists());

        DiskFilePath disk = (DiskFilePath)FilePath.Get(Path.Combine(directory, "sub").Replace('\\', '/'));
        Assert.True(disk.IsAbsolute());
        Assert.False(disk.Exists());
        disk.CreateDirectory();
        Assert.True(disk.Exists());
        Assert.True(disk.IsDirectory());
        Assert.False(disk.IsRegularFile());

        DiskFilePath inside = (DiskFilePath)FilePath.Get(Path.Combine(directory, "sub/inside.txt").Replace('\\', '/'));
        Assert.True(inside.CreateFile());
        Assert.True(inside.IsRegularFile());
        Assert.False(inside.IsDirectory());

        // The directory lists what is in it, by full name.
        IReadOnlyList<FilePath> entries = disk.NewDirectoryStream();
        Assert.Single(entries);
        Assert.EndsWith("inside.txt", entries[0].Name, StringComparison.Ordinal);

        // And the parent of a path is the directory it is in.
        DiskFilePath parent = (DiskFilePath)inside.GetParent()!;
        Assert.Equal(disk.ToRealPath().Name, parent.ToRealPath().Name);

        // Time moves, and a file that has just been made has a time.
        Assert.True(inside.LastModified() > 0);
    }

    [Fact]
    public void AWrapperWouldSayWhatItStandsFor()
    {
        // Nothing is a wrapper yet, so unwrapping returns the path itself. The call exists because
        // the LOB file is a wrapper over a split file, and the store asks what is underneath.
        FilePath path = At("memFS", "x.bin");
        Assert.Same(path, path.Unwrap());
        Assert.Same(path, FileUtils.Unwrap(path));
    }

    [Fact]
    public void AnUnknownSchemeFallsBackToTheDiskAndIsThenRefused()
    {
        // The reference does not refuse a scheme it does not know — it hands the path to the disk, the
        // way it hands a path with no scheme at all. The disk then refuses the name, because the text
        // before the colon is not one letter and so cannot be a drive. Which file system got the path
        // is visible in what it says about it.
        Assert.ThrowsAny<ArgumentException>(() => FilePath.Get("nosuchscheme:/tmp/x"));
        Assert.ThrowsAny<ArgumentException>(() => FilePath.Get("nosuchscheme:x"));

        // A name with no colon at all is the disk's, with nothing to refuse.
        Assert.Equal("file", FilePath.Get("zzz").GetScheme());
        Assert.Equal("zzz", FilePath.Get("zzz").Name);
    }

    [Fact]
    public void ADriveLetterIsNotAScheme()
    {
        // One character before the colon is a drive, not a scheme.
        Assert.Equal("file", FilePath.Get("C:/data/x.h2.db").GetScheme());
    }

    [Fact]
    public void ABackslashIsAForwardSlash()
    {
        Assert.Equal("file", FilePath.Get("some\\where\\x.h2.db").GetScheme());
        Assert.Equal("some/where/x.h2.db", FilePath.Get("some\\where\\x.h2.db").Name);
    }

    [Fact]
    public void TheFileSystemsOwnPrefixIsDroppedFromTheName()
    {
        // The scheme says which file system it is; the name is what that file system knows. So
        // "file:x", "nio:x" and "x" are one and the same file on disk.
        Assert.Equal("x.h2.db", FilePath.Get("file:x.h2.db").Name);
        Assert.Equal("x.h2.db", FilePath.Get("nio:x.h2.db").Name);
        Assert.Equal("x.h2.db", FilePath.Get("x.h2.db").Name);

        // The memory file system keeps its prefix in the name, and adds a slash after it, so that
        // "memFS:x" and "memFS:/x" are one name.
        Assert.Equal("memFS:/x.h2.db", FilePath.Get("memFS:x.h2.db").Name);
        Assert.Equal("memFS:/x.h2.db", FilePath.Get("memFS:/x.h2.db").Name);

        // "mem" is not a scheme of this file system and is not a scheme of the reference's either:
        // it is how an in-memory database is written, and that is the database's word. So it falls
        // through to the disk, where a colon in a file name is a refusal rather than a file.
        Assert.ThrowsAny<ArgumentException>(() => FilePath.Get("mem:x.h2.db"));
    }

    [Fact]
    public void ATildeIsTheHomeDirectoryOnlyWhenItStandsAlone()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(home + "/x.h2.db", DiskFilePath.TranslateFileName("~/x.h2.db"));
        Assert.Equal(home, DiskFilePath.TranslateFileName("~"));
        Assert.Equal("~user/x", DiskFilePath.TranslateFileName("~user/x"));
        Assert.Equal("a~/x", DiskFilePath.TranslateFileName("a~/x"));
    }

    [Fact]
    public void TheTemporaryDirectoryIsSomewhereReal()
    {
        Assert.True(Directory.Exists(FileUtils.TempDirectory));
        Assert.NotEmpty(FileUtils.TempDirectory);
    }

    [Fact]
    public void EveryEntryOfADirectoryIsAPathOfTheSameSystem()
    {
        DiskFilePath disk = (DiskFilePath)FilePath.Get(directory.Replace('\\', '/'));
        foreach (FilePath entry in disk.NewDirectoryStream())
        {
            Assert.Equal("file", entry.GetScheme());
        }
    }

    [Fact]
    public void TheModeDecidesWhetherTheFileIsWrittenTo()
    {
        Assert.True(FileUtils.IsReadOnly("r"));
        Assert.False(FileUtils.IsReadOnly("rw"));
        Assert.False(FileUtils.IsReadOnly("rws"));
        Assert.False(FileUtils.IsReadOnly("rwd"));
    }

    [Fact]
    public void TheMemoryFileSystemCanBeForgotten()
    {
        FilePath path = At("memFS", "gone.bin");
        Assert.True(path.CreateFile());
        Assert.True(path.Exists());
        path.Delete();
        Assert.False(path.Exists());

        // An empty file reads as nothing rather than as a failure, which is what a store sees when
        // it opens a database file for the first time.
        Assert.Equal([], FileUtils.ReadAllBytes(path));
    }
}
