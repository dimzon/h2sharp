/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.fs.mem.FilePathMem.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the reference keeps a map of paths to byte arrays and wraps each in a FileChannel.
 * Here the same, and the channel is this file's own object rather than a MemoryStream over a
 * shared buffer, because a shared MemoryStream shares its position too: two opens of one file would
 * move each other along, and a disk file never does that.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using H2Sharp.Api;
using H2Sharp.Message;

namespace H2Sharp.Store.FileSystem;

/// <summary>
/// A file that is never written to the disk, which is what an in-memory database and every test
/// that wants a database without one use.
/// </summary>
public sealed class MemoryFilePath : FilePath
{
    private static readonly Dictionary<string, MemoryFile> Files = new(StringComparer.Ordinal);

    private static readonly object Sync = new();

    internal MemoryFilePath()
        : base(string.Empty)
    {
    }

    private MemoryFilePath(string name)
        : base(name)
    {
    }

    /// <inheritdoc/>
    public override string GetScheme() => "memFS";

    /// <inheritdoc/>
    /// <remarks>
    /// The reference's canonical form puts a slash after the scheme, so <c>memFS:x</c> and
    /// <c>memFS:/x</c> are one name and not two files. And the scheme is <c>memFS</c>, not
    /// <c>mem</c>: <c>mem:</c> is how an in-memory *database* is written, and it belongs to the
    /// database, not to this file system. A path that says <c>mem:</c> therefore falls through to
    /// the disk, which is what the reference does with it.
    /// </remarks>
    public override FilePath GetPath(string path) => new MemoryFilePath(CanonicalPath(path));

    /// <summary>
    /// The name in the one form this file system has: slashes, and a slash after the scheme.
    /// </summary>
    /// <param name="fileName">the name</param>
    /// <returns>the canonical name</returns>
    public static string CanonicalPath(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        fileName = fileName.Replace('\\', '/');
        int index = fileName.IndexOf(':', StringComparison.Ordinal) + 1;
        if (fileName.Length > index && fileName[index] != '/')
        {
            fileName = fileName[..index] + "/" + fileName[index..];
        }

        return fileName;
    }

    /// <summary>
    /// Whether this path is the file system itself rather than a file in it.
    /// </summary>
    /// <returns>true for the bare scheme</returns>
    private bool IsRoot => string.Equals(Name, GetScheme() + ":", StringComparison.Ordinal);

    /// <inheritdoc/>
    public override long Size()
    {
        lock (Sync)
        {
            return Files.TryGetValue(Name, out MemoryFile? file) ? file.Length : 0;
        }
    }

    /// <inheritdoc/>
    public override bool Exists()
    {
        // The root is always there: it is the file system itself.
        if (IsRoot)
        {
            return true;
        }

        lock (Sync)
        {
            return Files.ContainsKey(Name);
        }
    }

    /// <inheritdoc/>
    public override bool CreateFile()
    {
        lock (Sync)
        {
            // One lookup and no allocation when the file is there: the slot starts empty and
            // is filled only for a file that is actually created.
            ref MemoryFile? slot =
                ref CollectionsMarshal.GetValueRefOrAddDefault(Files, Name, out bool exists);
            if (exists)
            {
                return false;
            }

            slot = new MemoryFile();
            return true;
        }
    }

    /// <inheritdoc/>
    public override void Delete()
    {
        // Deleting the root is not an error, it does nothing.
        if (IsRoot)
        {
            return;
        }

        lock (Sync)
        {
            _ = Files.Remove(Name);
        }
    }

    /// <inheritdoc/>
    public override void CreateDirectory()
    {
        // A directory in memory is only a name that no file is at yet, and there is nothing to
        // make; the reference does the same.
    }

    /// <inheritdoc/>
    public override IReadOnlyList<FilePath> NewDirectoryStream()
    {
        List<FilePath> entries = [];
        string prefix = Name.EndsWith('/') ? Name : Name + "/";
        lock (Sync)
        {
            foreach (string name in Files.Keys)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    entries.Add(new MemoryFilePath(name));
                }
            }
        }

        return entries;
    }

    /// <inheritdoc/>
    public override FilePath? GetParent()
    {
        int index = Name.LastIndexOf('/');
        return index < 0 ? null : new MemoryFilePath(Name[..index]);
    }

    /// <inheritdoc/>
    public override FilePath ToRealPath() => this;

    /// <inheritdoc/>
    public override bool IsDirectory() => IsRoot;

    /// <inheritdoc/>
    public override bool IsRegularFile() => !IsRoot && Exists();

    /// <inheritdoc/>
    /// <remarks>
    /// The reference answers yes here always, and says why: a relative path is not supported here,
    /// so there is nothing for it to be relative to. Keeping that answer matters, because the store
    /// asks before it decides whether a path may be written to.
    /// </remarks>
    public override bool IsAbsolute() => true;

    /// <inheritdoc/>
    public override long LastModified() => 0;

    /// <inheritdoc/>
    public override bool CanWrite() => true;

    /// <inheritdoc/>
    public override bool SetReadOnly() => true;

    /// <inheritdoc/>
    public override void MoveTo(FilePath newName, bool atomicReplace)
    {
        ArgumentNullException.ThrowIfNull(newName);
        lock (Sync)
        {
            if (!Files.TryGetValue(Name, out MemoryFile? file))
            {
                throw new FileNotFoundException("File not found: " + Name, Name);
            }

            // The reference's own error for a move onto a name that is taken, and moving onto
            // itself is not a move at all.
            if (!atomicReplace
                && !string.Equals(newName.Name, Name, StringComparison.Ordinal)
                && Files.ContainsKey(newName.Name))
            {
                throw DbException.Get(ErrorCode.FileRenameFailed2, Name, newName + " (exists)");
            }

            Files[newName.Name] = file;
            _ = Files.Remove(Name);
        }
    }

    /// <inheritdoc/>
    public override FileChannel Open(string mode)
    {
        lock (Sync)
        {
            // The reference opens a file that is not there as an empty one, which is why a fresh
            // database need not create its file before reading it.
            if (!Files.TryGetValue(Name, out MemoryFile? file))
            {
                file = new MemoryFile();
                Files[Name] = file;
            }

            return new MemoryFileChannel(file);
        }
    }

    /// <summary>
    /// Forget every in-memory file. The reference has no such call and neither should a database
    /// have one, so this is for tests that want a clean slate.
    /// </summary>
    internal static void Clear()
    {
        lock (Sync)
        {
            Files.Clear();
        }
    }

    /// <summary>
    /// The bytes of one in-memory file. Growable, and shared by every handle open on it.
    /// </summary>
    private sealed class MemoryFile
    {
        private byte[] buffer = new byte[64];

        public int Length { get; private set; }

        public void SetLength(long value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (value > buffer.Length)
            {
                Grow((int)value);
            }

            if (value < Length)
            {
                // A shorter file zeroes what it gave back, which a disk file does as well and which
                // is what a store relies on when it reads a page it has just cut.
                Array.Clear(buffer, (int)value, Length - (int)value);
            }

            Length = (int)value;
        }

        public int Read(long position, Span<byte> destination)
        {
            long available = Length - position;
            if (available <= 0)
            {
                return 0;
            }

            int count = (int)Math.Min(available, destination.Length);
            buffer.AsSpan((int)position, count).CopyTo(destination);
            return count;
        }

        public void Write(long position, ReadOnlySpan<byte> source)
        {
            long end = position + source.Length;
            if (end > int.MaxValue)
            {
                throw new IOException("In-memory file too large: " + end);
            }

            if (end > Length)
            {
                if (end > buffer.Length)
                {
                    Grow((int)end);
                }

                Length = (int)end;
            }

            source.CopyTo(buffer.AsSpan((int)position, source.Length));
        }

        private void Grow(int needed)
        {
            int size = buffer.Length;
            while (size < needed)
            {
                size *= 2;
            }

            Array.Resize(ref buffer, size);
        }
    }

    /// <summary>
    /// One handle onto an in-memory file: the bytes are shared, the position is this handle's own.
    /// </summary>
    private sealed class MemoryFileChannel : FileChannel
    {
        private readonly MemoryFile file;

        private long position;

        internal MemoryFileChannel(MemoryFile file) => this.file = file;

        /// <inheritdoc/>
        public override long Length => file.Length;

        /// <inheritdoc/>
        public override long Position
        {
            get => position;
            set => position = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
        }

        /// <inheritdoc/>
        public override Stream Stream => new FileChannelStream(this);

        /// <inheritdoc/>
        public override void SetLength(long value) => file.SetLength(value);

        /// <inheritdoc/>
        public override int Read(Span<byte> buffer)
        {
            int read = file.Read(position, buffer);
            position += read;
            return read;
        }

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            file.Write(position, buffer);
            position += buffer.Length;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Nothing to push anywhere: the bytes are already where a reader will find them.
        /// </remarks>
        public override void Flush(bool flushToDisk)
        {
        }
    }
}
