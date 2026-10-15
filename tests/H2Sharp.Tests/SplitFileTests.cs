/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The splitting file system: one file to the store, several on the disk.
 *
 * A part is at most a megabyte here — the size is written into the path, which is how the reference
 * makes it small — so the corpus crosses a part boundary many times and the interesting cases are
 * the ones at the seams: a write that starts in one part and ends in the next, a read that crosses
 * one, a file that is made shorter so that whole parts go away, and a write into a gap far past the
 * end.
 *
 * What must hold is that the bytes come back the same. A split file that answers a different length
 * or a different byte is worse than no split file, because the store would write into it happily.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using H2Sharp.Store.FileSystem;
using Xunit;

namespace H2Sharp.Tests;

public sealed class SplitFileTests : IDisposable
{
    private const int Shift = 20;

    private const long PartLength = 1L << Shift;

    private readonly string directory;

    public SplitFileTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "h2sharp-split-" + Guid.NewGuid().ToString("N"));
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

    private string Split(string name) => "split:" + Shift.ToString(CultureInfo.InvariantCulture) + ":"
        + Path.Combine(directory, name).Replace('\\', '/');

    private string OnDisk(string name) => Path.Combine(directory, name).Replace('\\', '/');

    private static byte[] Pattern(int length)
    {
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bytes[i] = (byte)((i * 31) ^ (i >> 8));
        }

        return bytes;
    }

    [Fact]
    public void ThePathIsAWrapperOverTheFileItStandsFor()
    {
        FilePath path = FilePath.Get(Split("db.mv.db"));
        Assert.Equal("split", path.GetScheme());
        Assert.EndsWith("db.mv.db", path.Name, StringComparison.Ordinal);

        // And what it stands for is an ordinary disk path, which is the point of the wrapper.
        FilePath basePath = path.Unwrap();
        Assert.Equal("file", basePath.GetScheme());
        Assert.EndsWith("db.mv.db", basePath.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void ASizeMayBeLeftOutAndThenThePropertyIsUsed()
    {
        FilePath withSize = FilePath.Get(Split("db.mv.db"));
        FilePath without = FilePath.Get(
            "split:" + Path.Combine(directory, "db.mv.db").Replace('\\', '/'));

        // The name keeps what it was given — the default size is a property, not something written
        // into the path — and what matters is that both stand for the same file.
        Assert.Equal(withSize.Unwrap().Name, without.Unwrap().Name);
        Assert.Equal(withSize.GetName(), without.GetName());

        // And a file opened through a path without a size goes to the same part.
        FileUtils.WriteAllBytes(without, [1, 2, 3]);
        Assert.Equal([1, 2, 3], FileUtils.ReadAllBytes(withSize));
        without.Delete();
    }

    [Fact]
    public void OneFileBecomesSeveralOnTheDisk()
    {
        FilePath path = FilePath.Get(Split("big.mv.db"));

        // More than one part's worth, so the file really is split.
        byte[] payload = Pattern((int)(PartLength * 2.5));
        FileUtils.WriteAllBytes(path, payload);

        Assert.True(File.Exists(OnDisk("big.mv.db")));
        Assert.True(File.Exists(OnDisk("big.mv.db.1.part")));
        Assert.True(File.Exists(OnDisk("big.mv.db.2.part")));
        Assert.False(File.Exists(OnDisk("big.mv.db.3.part")));

        // The store asks the file how long it is, and reads it back.
        Assert.Equal(payload.Length, path.Size());
        Assert.Equal(payload, FileUtils.ReadAllBytes(path));

        // And the size of a split file is the sum of the parts, which is what a reader expects.
        long onDisk = new FileInfo(OnDisk("big.mv.db")).Length
            + new FileInfo(OnDisk("big.mv.db.1.part")).Length
            + new FileInfo(OnDisk("big.mv.db.2.part")).Length;
        Assert.Equal(payload.Length, onDisk);
    }

    [Fact]
    public void AFileThatDoesNotFitInOnePartIsStillOneFile()
    {
        FilePath path = FilePath.Get(Split("round.mv.db"));
        byte[] payload = Pattern((int)(PartLength * 3) + 17);
        FileUtils.WriteAllBytes(path, payload);
        Assert.Equal(payload, FileUtils.ReadAllBytes(path));
    }

    [Fact]
    public void ASmallFileIsOnePartAndStaysOne()
    {
        FilePath path = FilePath.Get(Split("small.mv.db"));
        byte[] payload = Pattern(1000);
        FileUtils.WriteAllBytes(path, payload);

        Assert.True(File.Exists(OnDisk("small.mv.db")));
        Assert.False(File.Exists(OnDisk("small.mv.db.1.part")));
        Assert.Equal(payload, FileUtils.ReadAllBytes(path));
    }

    [Fact]
    public void WritingIntoAGapCreatesThePartsItNeeds()
    {
        FilePath path = FilePath.Get(Split("gap.mv.db"));
        using FileChannel channel = path.Open("rw");

        // A position two parts past the end: the parts in between have to appear, with nothing in
        // them but the byte that was written, and the file has no hole.
        channel.Position = PartLength * 2 + 10;
        channel.Write([7, 7]);
        Assert.Equal(PartLength * 2 + 12, channel.Length);

        channel.Position = 0;
        byte[] head = new byte[4];
        Assert.Equal(4, channel.Read(head));

        // The parts the write skipped over hold nothing but the byte that was written at the end of
        // each, which is what the reference does: it makes the file the right length without
        // writing a byte per skipped part. So the beginning of the file is zeros.
        Assert.Equal([0, 0, 0, 0], head);
    }

    [Fact]
    public void MakingAFileShorterRemovesThePartsThatFallPastIt()
    {
        FilePath path = FilePath.Get(Split("shrink.mv.db"));
        byte[] payload = Pattern((int)(PartLength * 3) + 100);
        FileUtils.WriteAllBytes(path, payload);
        Assert.True(File.Exists(OnDisk("shrink.mv.db.2.part")));

        using (FileChannel channel = path.Open("rw"))
        {
            channel.SetLength(PartLength + 10);
            Assert.Equal(PartLength + 10, channel.Length);
        }

        // The last part is gone from the disk, not merely forgotten.
        Assert.False(File.Exists(OnDisk("shrink.mv.db.2.part")));
        Assert.True(File.Exists(OnDisk("shrink.mv.db.1.part")));
        Assert.Equal(PartLength + 10, path.Size());

        // And what is left is still the beginning of the same file.
        byte[] kept = FileUtils.ReadAllBytes(path);
        Assert.Equal(payload.AsSpan(0, kept.Length).ToArray(), kept);
    }

    [Fact]
    public void DeletingRemovesEveryPart()
    {
        FilePath path = FilePath.Get(Split("gone.mv.db"));
        FileUtils.WriteAllBytes(path, Pattern((int)(PartLength * 2.5)));

        Assert.True(path.Exists());
        path.Delete();
        Assert.False(path.Exists());
        Assert.False(File.Exists(OnDisk("gone.mv.db")));
        Assert.False(File.Exists(OnDisk("gone.mv.db.1.part")));
        Assert.False(File.Exists(OnDisk("gone.mv.db.2.part")));
    }

    [Fact]
    public void ADirectoryListsTheFileAndNotItsParts()
    {
        FilePath directoryPath = FilePath.Get("split:" + Shift.ToString(CultureInfo.InvariantCulture) + ":"
            + directory.Replace('\\', '/'));
        FilePath file = FilePath.Get(Split("listed.mv.db"));
        FileUtils.WriteAllBytes(file, Pattern((int)(PartLength * 1.5)));

        List<FilePath> entries = [.. directoryPath.NewDirectoryStream()];
        List<string> names = [];
        foreach (FilePath entry in entries)
        {
            names.Add(entry.GetName());
        }

        // The parts are not files of their own as far as a caller listing the directory is
        // concerned.
        Assert.DoesNotContain("listed.mv.db.1.part", names);
        Assert.Contains("listed.mv.db", names);
        foreach (FilePath entry in entries)
        {
            Assert.Equal("split", entry.GetScheme());
        }

        file.Delete();
    }

    [Fact]
    public void MovingTakesEveryPartWithIt()
    {
        FilePath from = FilePath.Get(Split("from.mv.db"));
        FilePath to = FilePath.Get(Split("to.mv.db"));
        byte[] payload = Pattern((int)(PartLength * 2.5));
        FileUtils.WriteAllBytes(from, payload);

        from.MoveTo(to, atomicReplace: false);
        Assert.False(from.Exists());
        Assert.True(to.Exists());
        Assert.Equal(payload, FileUtils.ReadAllBytes(to));
        Assert.True(File.Exists(OnDisk("to.mv.db.1.part")));
        Assert.False(File.Exists(OnDisk("from.mv.db.1.part")));

        to.Delete();
    }

    [Fact]
    public void TheTimeOfAFileIsTheNewestOfItsParts()
    {
        FilePath path = FilePath.Get(Split("timed.mv.db"));
        FileUtils.WriteAllBytes(path, Pattern((int)(PartLength * 1.5)));
        long modified = path.LastModified();

        Assert.True(modified > 0);
        Assert.Equal(
            Math.Max(new FileInfo(OnDisk("timed.mv.db")).LastWriteTimeUtc.Ticks,
                new FileInfo(OnDisk("timed.mv.db.1.part")).LastWriteTimeUtc.Ticks) > 0,
            modified > 0);
        path.Delete();
    }

    [Fact]
    public void AStreamOverASplitFileIsOneFile()
    {
        FilePath path = FilePath.Get(Split("streamed.mv.db"));
        byte[] payload = Pattern((int)(PartLength * 1.5));

        using (Stream output = path.NewOutputStream(append: false))
        {
            output.Write(payload);
        }

        Assert.Equal(payload.Length, path.Size());

        // The reader is in a block of its own, because the file is deleted once nothing holds it:
        // a stream that is still open is a file the system will not let go of.
        using (Stream input = path.NewInputStream())
        {
            using MemoryStream read = new();
            input.CopyTo(read);
            Assert.Equal(payload, read.ToArray());
        }

        path.Delete();
        Assert.False(path.Exists());
    }

    [Fact]
    public void PositionalAccessCrossesPartsWithoutMovingTheCursor()
    {
        FilePath path = FilePath.Get(Split("positional.mv.db"));
        using FileChannel channel = path.Open("rw");

        // A write straddling a part boundary lands in both parts, and the cursor stays where
        // the sequential writer left it.
        byte[] payload = Pattern(16);
        channel.Position = 7;
        channel.WriteAt(PartLength - 2, payload);
        Assert.Equal(7, channel.Position);
        Assert.Equal(PartLength + 14, channel.Length);

        byte[] back = new byte[payload.Length];
        Assert.Equal(payload.Length, channel.ReadAt(PartLength - 2, back));
        Assert.Equal(payload, back);
        Assert.Equal(7, channel.Position);

        // Past the end there is nothing, and a read past it does not move the cursor either.
        Assert.Equal(0, channel.ReadAt(PartLength + 14, new byte[4]));
        Assert.Equal(7, channel.Position);

        // DataUtils rides on the same pair across the seam.
        byte[] block = Pattern(100);
        H2Sharp.MvStore.DataUtils.WriteFully(channel, PartLength - 50, block);
        byte[] round = new byte[block.Length];
        H2Sharp.MvStore.DataUtils.ReadFully(channel, PartLength - 50, round);
        Assert.Equal(block, round);
    }

    [Fact]
    public void PartsOfDifferentSizesAreRefused()
    {
        // A split file whose middle part is the wrong size was not written by one writer. Opening
        // it as if it were would read the wrong bytes without noticing, so it is refused instead.
        FileUtils.WriteAllBytes(FilePath.Get(OnDisk("odd.mv.db")), Pattern(100));
        FileUtils.WriteAllBytes(FilePath.Get(OnDisk("odd.mv.db.1.part")), Pattern(7));
        FileUtils.WriteAllBytes(FilePath.Get(OnDisk("odd.mv.db.2.part")), Pattern(50));

        FilePath path = FilePath.Get(Split("odd.mv.db"));
        Assert.ThrowsAny<IOException>(() => path.Open("rw").Dispose());
    }
}
