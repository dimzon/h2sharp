/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A file of whole blocks.
 *
 * The three things that make this file what it is, and that the tests here pin: the header is three
 * blocks and is written before anything else; nothing may be read, written, sought or resized by an
 * amount that is not whole blocks; and the length the object believes in is the length on the disk.
 *
 * Encryption is not ported, so the tests are about the layout that is left: all three header blocks
 * are written whether there is a cipher or not, which is what makes a file written here readable by
 * the reference.
 */
using System;
using System.IO;
using H2Sharp.Engine;
using H2Sharp.Store;
using H2Sharp.Store.FileSystem;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class FileStoreTests : IDisposable
{
    private const int Block = 16;

    private readonly string directory;

    private readonly RecordingHandler handler = new();

    public FileStoreTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "h2sharp-filestore-" + Guid.NewGuid().ToString("N"));
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
            // Somebody still holds a file; the next run makes another directory.
        }
    }

    private string Disk(string name) => System.IO.Path.Combine(directory, name).Replace('\\', '/');

    [Fact]
    public void ANewFileBeginsWithThreeBlocksOfHeader()
    {
        FileStore store = FileStore.Open(handler, Disk("new.db"), "rw");
        store.Init();

        // The header is written, and the file is exactly as long as it says it is.
        Assert.Equal(FileStore.HeaderLength, store.Length());
        store.Close();

        byte[] bytes = File.ReadAllBytes(Disk("new.db"));
        Assert.Equal(FileStore.HeaderLength, bytes.Length);

        // All three blocks are the same sixteen bytes, and the first block is the magic cut to fit.
        Assert.Equal("-- H2 0.5/B -- \n", System.Text.Encoding.Latin1.GetString(bytes, 0, Block));
        Assert.Equal(bytes[0..Block], bytes[Block..(2 * Block)]);
        Assert.Equal(bytes[0..Block], bytes[(2 * Block)..(3 * Block)]);
    }

    [Fact]
    public void AFileWithTheHeaderIsAcceptedAgain()
    {
        FileStore first = FileStore.Open(handler, Disk("again.db"), "rw");
        first.Init();
        first.Close();

        // Opening and initialising again finds the header where it expects it.
        FileStore second = FileStore.Open(handler, Disk("again.db"), "rw");
        second.Init();
        Assert.Equal(FileStore.HeaderLength, second.Length());
        second.Close();
    }

    [Fact]
    public void AFileOfAnotherVersionIsRefused()
    {
        File.WriteAllBytes(
            System.IO.Path.Combine(directory, "other.db"),
            new byte[FileStore.HeaderLength + Block]);

        FileStore store = FileStore.Open(handler, Disk("other.db"), "rw");
        Assert.ThrowsAny<H2SharpException>(store.Init);
        store.Close();
    }

    [Fact]
    public void BlocksAreWrittenAndReadBack()
    {
        FileStore store = FileStore.Open(handler, Disk("blocks.db"), "rw");
        store.Init();

        byte[] block = new byte[Block];
        for (int i = 0; i < Block; i++)
        {
            block[i] = (byte)(i + 1);
        }

        store.Write(block, 0, Block);
        Assert.Equal(FileStore.HeaderLength + Block, store.Length());

        store.Seek(FileStore.HeaderLength);
        byte[] back = new byte[Block];
        store.ReadFully(back, 0, Block);
        Assert.Equal(block, back);
        store.Close();
    }

    [Fact]
    public void TheFilePointerMovesWithWhatWasReadAndWritten()
    {
        FileStore store = FileStore.Open(handler, Disk("pointer.db"), "rw");
        store.Init();

        Assert.Equal(FileStore.HeaderLength, store.GetFilePointer());
        store.Write(new byte[Block], 0, Block);
        Assert.Equal(FileStore.HeaderLength + Block, store.GetFilePointer());

        // And reading moves it on by what was read, from wherever the pointer is.
        store.Seek(FileStore.HeaderLength);
        byte[] buffer = new byte[Block];
        store.ReadFully(buffer, 0, Block);
        Assert.Equal(FileStore.HeaderLength + Block, store.GetFilePointer());
        store.Close();
    }

    [Fact]
    public void ReadingPastTheEndOfTheFileIsAFailure()
    {
        FileStore store = FileStore.Open(handler, Disk("pastend.db"), "rw");
        store.Init();

        // The file ends after the header, and a block that is not there is damage rather than an
        // answer: half a page would be taken for a whole one.
        store.Seek(FileStore.HeaderLength);
        Assert.ThrowsAny<Exception>(() => store.ReadFully(new byte[Block], 0, Block));
        store.Close();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(Block - 1)]
    [InlineData(Block + 1)]
    public void NothingIsWrittenInAnythingButWholeBlocks(int len)
    {
        FileStore store = FileStore.Open(handler, Disk($"unaligned{len}.db"), "rw");
        store.Init();

        // A half block is a database that cannot be recovered, so it is refused rather than rounded.
        Assert.ThrowsAny<Exception>(() => store.Write(new byte[len], 0, len));
        Assert.ThrowsAny<Exception>(() => store.ReadFully(new byte[len], 0, len));
        Assert.ThrowsAny<Exception>(() => store.SetLength(len));
        store.Close();
    }

    [Fact]
    public void NothingIsSeekedToButABlockBoundary()
    {
        FileStore store = FileStore.Open(handler, Disk("seek.db"), "rw");
        store.Init();

        Assert.ThrowsAny<Exception>(() => store.Seek(1));
        store.Seek(0);
        Assert.Equal(0, store.GetFilePointer());
        store.Close();
    }

    [Fact]
    public void AFileCanBeMadeLongerAndShorter()
    {
        FileStore store = FileStore.Open(handler, Disk("resize.db"), "rw");
        store.Init();

        store.SetLength(FileStore.HeaderLength + (4 * Block));
        Assert.Equal(FileStore.HeaderLength + (4 * Block), store.Length());

        // The space made by growing is zeros, which is what a page that has just been made room for
        // reads as.
        store.Seek(FileStore.HeaderLength);
        byte[] first = new byte[Block];
        store.ReadFully(first, 0, Block);
        Assert.Equal(new byte[Block], first);

        store.SetLength(FileStore.HeaderLength);
        Assert.Equal(FileStore.HeaderLength, store.Length());
        store.Close();
    }

    [Fact]
    public void TheFileCanBeClosedAndOpenedAgainWithThePointerWhereItWas()
    {
        FileStore store = FileStore.Open(handler, Disk("reopen.db"), "rw");
        store.Init();
        store.Write(new byte[Block], 0, Block);
        long where = store.GetFilePointer();

        store.CloseFile();
        store.OpenFile();
        Assert.Equal(where, store.GetFilePointer());
        store.Close();
    }

    [Fact]
    public void TheDatabaseIsAskedBeforeEveryWrite()
    {
        FileStore store = FileStore.Open(handler, Disk("checked.db"), "rw");
        store.Init();

        // The header is written without asking: there is nothing yet to ask about.
        int before = handler.WritingChecks;
        store.Write(new byte[Block], 0, Block);
        Assert.Equal(before + 1, handler.WritingChecks);

        // And with the checking turned off it does not ask.
        store.SetCheckedWriting(false);
        store.Write(new byte[Block], 0, Block);
        Assert.Equal(before + 1, handler.WritingChecks);
        store.Close();
    }

    [Fact]
    public void TheDatabaseIsAskedAboutThePowerOnEveryWriteAndRead()
    {
        FileStore store = FileStore.Open(handler, Disk("power.db"), "rw");
        store.Init();

        int before = handler.PowerChecks;
        store.Write(new byte[Block], 0, Block);
        store.Seek(FileStore.HeaderLength);
        store.ReadFully(new byte[Block], 0, Block);
        Assert.True(handler.PowerChecks >= before + 2);
        store.Close();
    }

    [Fact]
    public void AFileThatCannotBeWrittenIsOpenedForReading()
    {
        string name = Disk("readonly.db");
        File.WriteAllBytes(name, new byte[FileStore.HeaderLength]);
        File.SetAttributes(name, FileAttributes.ReadOnly);
        try
        {
            FileStore store = FileStore.Open(handler, Disk("readonly.db"), "rw");

            // Opening for reading is not the same as writing, and the store finds out when it tries.
            Assert.ThrowsAny<Exception>(() => store.Write(new byte[Block], 0, Block));
            store.Close();
        }
        finally
        {
            File.SetAttributes(name, FileAttributes.Normal);
        }
    }

    [Fact]
    public void AFileCanBeMarkedToDeleteItselfWhenNobodyIsUsingIt()
    {
        FileStore store = FileStore.Open(handler, Disk("autodelete.db"), "rw");
        store.Init();
        store.AutoDelete();
        store.StopAutoDelete();

        // Closing and deleting is one operation: the reference refuses it once the file is already
        // closed, because there is then nothing it is certain of deleting, and so does this.
        store.CloseAndDeleteSilently();
        Assert.False(File.Exists(System.IO.Path.Combine(directory, "autodelete.db")));
    }

    [Fact]
    public void LockingSaysWhetherNobodyElseHasTheFile()
    {
        FileStore store = FileStore.Open(handler, Disk("locked.db"), "rw");
        store.Init();

        Assert.True(store.TryLock());
        Assert.True(store.TryLock());
        store.ReleaseLock();

        store.ReleaseLock();
        store.Close();
    }

    /// <summary>
    /// A database that answers everything and writes down what it was asked, which is the only way a
    /// test can tell that it was asked at all.
    /// </summary>
    private sealed class RecordingHandler : DataHandler
    {
        public int PowerChecks { get; private set; }

        public int WritingChecks { get; private set; }

        public string GetDatabasePath() => string.Empty;

        public FileStore OpenFile(string name, string mode, bool mustExist) => FileStore.Open(this, name, mode);

        public void CheckPowerOff() => PowerChecks++;

        public void CheckWritingAllowed() => WritingChecks++;

        public int GetMaxLengthInplaceLob() => Constants.DefaultMaxLengthInplaceLob;

        public TempFileDeleter GetTempFileDeleter() => TempFileDeleter.GetInstance();

        public object GetLobSyncObject() => sync;

        public SmallLRUCache<string, string[]>? GetLobFileListCache() => null;

        public CompareMode GetCompareMode() => CompareMode.GetInstance(null, 0);

        private readonly object sync = new();
    }
}
