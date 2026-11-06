/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The IOException wrappers of FileStore, forced through a file system whose channel fails on
 * demand: every wrapper converts the failure into a database exception that carries the file
 * name, and every silent close swallows its own one. Targets taken line by line from the
 * coverage data (uncovered.py): the close, seek, write, set-length, sync and silent-close
 * wrappers, the DEBUG length cross-check, and the length-query conversion.
 */
using System;
using System.Collections.Generic;
using System.IO;
using H2Sharp.Engine;
using H2Sharp.Store;
using H2Sharp.Store.FileSystem;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class FileStoreFailurePortTests : IDisposable
{
    private readonly FailingFileSystem fileSystem = new(string.Empty);

    private readonly RecordingDataHandler handler = new();

    public FileStoreFailurePortTests()
    {
        FilePath.Register(fileSystem);
    }

    public void Dispose()
    {
        FilePath.Unregister(fileSystem);
    }

    [Fact]
    public void AFailedSeekIsConvertedAndNamesTheFile()
    {
        fileSystem.FailOnSeek = true;
        FileStore store = FileStore.Open(handler, "failfs:/seek.db", "rw");
        store.Init();

        H2SharpException e = Assert.Throws<H2SharpException>(() => store.Seek(16));
        Assert.Contains("failfs:/seek.db", e.Message);
    }

    [Fact]
    public void AFailedWriteClosesTheFileAndNamesIt()
    {
        FileStore store = FileStore.Open(handler, "failfs:/write.db", "rw");
        store.Init();

        // The write refuses, and the file behind it is closed for good.
        fileSystem.FailOnWrite = true;
        H2SharpException e = Assert.Throws<H2SharpException>(
            () => store.Write(new byte[Constants.FileBlockSize], 0, Constants.FileBlockSize));
        Assert.Contains("failfs:/write.db", e.Message);
    }

    [Fact]
    public void AFailedSetLengthIsConvertedAndNamesTheFile()
    {
        fileSystem.FailOnSetLength = true;
        FileStore store = FileStore.Open(handler, "failfs:/setlen.db", "rw");
        store.Init();

        H2SharpException e = Assert.Throws<H2SharpException>(
            () => store.SetLength(Constants.FileBlockSize));
        Assert.Contains("failfs:/setlen.db", e.Message);
    }

    [Fact]
    public void AFailedSyncClosesTheFileAndNamesIt()
    {
        fileSystem.FailOnFlush = true;
        FileStore store = FileStore.Open(handler, "failfs:/sync.db", "rw");
        store.Init();

        H2SharpException e = Assert.Throws<H2SharpException>(() => store.Sync());
        Assert.Contains("failfs:/sync.db", e.Message);
    }

    [Fact]
    public void AFailedCloseIsSwallowedByTheSilentForms()
    {
        fileSystem.FailOnDispose = true;
        FileStore store = FileStore.Open(handler, "failfs:/close.db", "rw");
        store.Init();

        // CloseSilently catches what Close throws; the silent close-and-delete walks the
        // same guard.
        Assert.Throws<H2SharpException>(store.Close);
        store.CloseSilently();
        store.CloseAndDeleteSilently();
    }

    [Fact]
    public void ALengthMismatchIsAnInternalError()
    {
        FileStore store = FileStore.Open(handler, "failfs:/mismatch.db", "rw");
        store.Init();

        // The DEBUG cross-check of Length(): the channel lies about its length, the store
        // refuses before writing into a wrong page.
        fileSystem.WrongLength = true;
        Exception e = Assert.ThrowsAny<Exception>(() => _ = store.Length());
        Assert.Contains("expected", e.Message);
    }

    [Fact]
    public void AFailedLengthQueryIsConvertedAndNamesTheFile()
    {
        FileStore store = FileStore.Open(handler, "failfs:/lenquery.db", "rw");
        store.Init();

        fileSystem.FailOnLength = true;
        H2SharpException e = Assert.Throws<H2SharpException>(() => _ = store.Length());
        Assert.Contains("failfs:/lenquery.db", e.Message);
    }

    private sealed class RecordingDataHandler : DataHandler
    {
        private readonly object sync = new();

        public string GetDatabasePath() => string.Empty;

        public FileStore OpenFile(string name, string mode, bool mustExist)
            => FileStore.Open(this, name, mode);

        public void CheckPowerOff()
        {
        }

        public void CheckWritingAllowed()
        {
        }

        public int GetMaxLengthInplaceLob() => Constants.DefaultMaxLengthInplaceLob;

        public TempFileDeleter GetTempFileDeleter() => TempFileDeleter.GetInstance();

        public object GetLobSyncObject() => sync;

        public SmallLRUCache<string, string[]>? GetLobFileListCache() => null;

        public CompareMode GetCompareMode() => CompareMode.GetInstance(null, 0);

        public LobStorageInterface GetLobStorage() => null!;
    }

    /// <summary>
    /// A one-file system whose channel fails the operation the test asks for. Everything else
    /// is an honest file in memory.
    /// </summary>
    private sealed class FailingFileSystem(string name) : FilePath(name)
    {
        public bool FailOnSeek;

        public bool FailOnWrite;

        public bool FailOnSetLength;

        public bool FailOnFlush;

        public bool FailOnDispose;

        public bool FailOnLength;

        public bool WrongLength;

        internal readonly Dictionary<string, byte[]> files = new();

        public override string GetScheme() => "failfs";

        public override FilePath GetPath(string path) => new FailingPath(this, path);

        public override long Size() => 0;

        public override bool Exists() => false;

        public override FileChannel Open(string mode) => GetPath("failfs:/stub").Open(mode);

        public override bool CreateFile() => false;

        public override void Delete()
        {
        }

        public override void CreateDirectory()
        {
        }

        public override IReadOnlyList<FilePath> NewDirectoryStream() => [];

        public override FilePath? GetParent() => null;

        public override FilePath ToRealPath() => this;

        public override bool IsDirectory() => false;

        public override bool IsRegularFile() => false;

        public override bool IsAbsolute() => true;

        public override long LastModified() => 0;

        public override bool CanWrite() => true;

        public override bool SetReadOnly() => false;

        public override void MoveTo(FilePath newName, bool atomicReplace)
            => throw new NotSupportedException();

        private sealed class FailingPath(FailingFileSystem fileSystem, string pathName) : FilePath(pathName)
        {
            private readonly string name = pathName;

            public override string GetScheme() => "failfs";

            public override FilePath GetPath(string path) => new FailingPath(fileSystem, path);

            public override long Size() => fileSystem.files.TryGetValue(this.name, out byte[]? data)
                ? data.Length
                : 0;

            public override bool Exists() => fileSystem.files.ContainsKey(this.name);

            public override FileChannel Open(string mode)
            {
                byte[] data = fileSystem.files.TryGetValue(name, out byte[]? saved)
                    ? [.. saved]
                    : [];
                return new FailingChannel(fileSystem, this.name, data);
            }

            public override bool CreateFile()
            {
                return fileSystem.files.TryAdd(this.name, []);
            }

            public override void Delete() => fileSystem.files.Remove(this.name);

            public override void CreateDirectory()
            {
            }

            public override IReadOnlyList<FilePath> NewDirectoryStream() => [];

            public override FilePath? GetParent() => name == "failfs:/" ? null : GetPath("failfs:/");

            public override FilePath ToRealPath() => this;

            public override bool IsDirectory() => false;

            public override bool IsRegularFile() => fileSystem.files.ContainsKey(this.name);

            public override bool IsAbsolute() => true;

            public override long LastModified() => 0;

            public override bool CanWrite() => true;

            public override bool SetReadOnly() => false;

            public override void MoveTo(FilePath newName, bool atomicReplace)
                => throw new NotSupportedException();

            private sealed class FailingChannel(FailingFileSystem fileSystem, string name, byte[] data)
                : FileChannel
            {
                private long position;

            private bool closed;

            private IOException Fail() => new($"failfs failure on {name}");

            public override long Length
            {
                get
                {
                    if (fileSystem.FailOnLength)
                    {
                        throw Fail();
                    }

                    long length = data.Length;
                    if (fileSystem.WrongLength)
                    {
                        length += 5;
                    }

                    return length;
                }
            }

            public override long Position
            {
                get => position;
                set
                {
                    if (fileSystem.FailOnSeek)
                    {
                        throw Fail();
                    }

                    position = value;
                }
            }

            public override Stream Stream => throw new NotSupportedException();

            public override void SetLength(long value)
            {
                if (fileSystem.FailOnSetLength)
                {
                    throw Fail();
                }

                Ensure(value);
                Array.Resize(ref data, (int)value);
            }

            public override int Read(Span<byte> buffer)
            {
                int count = Math.Min(buffer.Length, (int)(data.Length - position));
                if (count <= 0)
                {
                    return 0;
                }

                data.AsSpan((int)position, count).CopyTo(buffer);
                position += count;
                return count;
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                if (fileSystem.FailOnWrite)
                {
                    throw Fail();
                }

                Ensure(position + buffer.Length);
                buffer.CopyTo(data.AsSpan((int)position));
                position += buffer.Length;
            }

            public override int ReadAt(long fileOffset, Span<byte> buffer)
            {
                position = fileOffset;
                return Read(buffer);
            }

            public override void WriteAt(long fileOffset, ReadOnlySpan<byte> buffer)
            {
                position = fileOffset;
                Write(buffer);
            }

            public override void Flush(bool flushToDisk)
            {
                if (fileSystem.FailOnFlush)
                {
                    throw Fail();
                }

                fileSystem.files[name] = [.. data];
            }

            protected override void Dispose(bool disposing)
            {
                if (closed)
                {
                    return;
                }

                closed = true;
                if (fileSystem.FailOnDispose)
                {
                    throw Fail();
                }

                fileSystem.files[name] = [.. data];
            }

            private void Ensure(long needed)
            {
                if (needed > data.Length)
                {
                    Array.Resize(ref data, (int)needed);
                }
            }
        }
        }
    }
}
