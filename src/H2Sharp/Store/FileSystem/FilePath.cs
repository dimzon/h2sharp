/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.fs.FilePath.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using H2Sharp.Engine;
using H2Sharp.Util;

namespace H2Sharp.Store.FileSystem;

/// <summary>
/// A path to a file, and the file system it belongs to.
/// </summary>
/// <remarks>
/// <para>
/// The reference calls this "similar to <c>java.nio.file.Path</c>, but simpler", and that is what
/// it is here too: the store speaks in strings, and the scheme at the front of the string decides
/// which file system opens it. A path with no scheme, or one with only a drive letter, goes to the
/// disk.
/// </para>
/// <para>
/// The registry is the reference's: every file system registers under its scheme, the disk one is
/// the default, and a scheme nobody registered falls back to the disk rather than failing — which is
/// what makes <c>Data Source=foo.h2.db</c> and <c>jdbc:h2:file:foo.h2.db</c> both work.
/// </para>
/// <para>
/// The Java-only file systems of the reference are not here: <c>niomapped</c>, <c>niomem</c>,
/// <c>async</c> and <c>retry</c> exist because of <c>java.nio</c> channels and of the interrupt
/// model, and .NET has neither of those problems to solve. What is left is the disk, memory, and —
/// further on — the split file that the LOBs of a disk database live in.
/// </para>
/// </remarks>
public abstract class FilePath
{
    private static readonly ConcurrentDictionary<string, FilePath> Providers = new(StringComparer.Ordinal);

    private static FilePath? defaultProvider;

    private static string? tempRandom;

    private static long tempSequence;

    static FilePath()
    {
        DiskFilePath disk = new();
        _ = Providers.TryAdd(disk.GetScheme(), disk);

        // The reference registers the plain name of the disk file system as "nio" too, because its
        // own URLs are written that way.
        _ = Providers.TryAdd("nio", disk);
        defaultProvider = disk;

        // The reference finds its other file systems by reflection over a list of class names, and
        // quietly skips the ones that are not in the build. Here they are listed, and the ones that
        // are not ported yet are simply absent: the compressing file and the encrypted ones. Note
        // that "mem" is not among them and is not a scheme of ours either: "mem:" is how an
        // in-memory database is written, and that belongs to the database. The file system that
        // keeps files in memory answers to "memFS", as it does in the reference.
        _ = Providers.TryAdd("memFS", new MemoryFilePath());

        // The splitting file system is its own, because the path of one part is not the path of the
        // file: a database that is too large for one file is written as "db.mv.db", "db.mv.db.1.part"
        // and so on, and the store sees one file where the disk has several.
        SplitFilePath split = new();
        _ = Providers.TryAdd(split.GetScheme(), split);
    }

    /// <summary>
    /// The complete path, which may be absolute or relative, depending on the file system.
    /// </summary>
    public string Name { get; protected init; }

    /// <summary>
    /// The file system this path belongs to.
    /// </summary>
    protected FilePath(string name) => Name = name;

    /// <summary>
    /// The path for the given text. A Windows-style backslash is a forward slash here.
    /// </summary>
    /// <param name="path">the path</param>
    /// <returns>the path object</returns>
    public static FilePath Get(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        path = path.Replace('\\', '/');
        int index = path.IndexOf(':', StringComparison.Ordinal);

        // Below two characters there is no scheme: either there is none at all, or what is there
        // is a drive letter, which the disk file system knows how to read.
        if (index < 2)
        {
            return Default.GetPath(path);
        }

        string scheme = path[..index];
        if (!Providers.TryGetValue(scheme, out FilePath? provider))
        {
            // The reference falls back to the disk rather than refusing, so that a path the file
            // system does not know still opens on disk.
            provider = Default;
        }

        return provider.GetPath(path);
    }

    /// <summary>
    /// Register a file system under its scheme.
    /// </summary>
    /// <param name="provider">the file system</param>
    public static void Register(FilePath provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Providers[provider.GetScheme()] = provider;
    }

    /// <summary>
    /// Remove a file system.
    /// </summary>
    /// <param name="provider">the file system</param>
    public static void Unregister(FilePath provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _ = Providers.TryRemove(provider.GetScheme(), out _);
    }

    /// <summary>
    /// The disk file system, which is what a path without a scheme goes to.
    /// </summary>
    internal static FilePath Default =>
        defaultProvider ?? throw new InvalidOperationException("The default file system is not registered");

    /// <summary>
    /// The scheme of this file system, which is the prefix of the paths it opens.
    /// </summary>
    /// <returns>the scheme</returns>
    public abstract string GetScheme();

    /// <summary>
    /// This path as an object of this file system, whatever scheme the text carries.
    /// </summary>
    /// <param name="path">the path text</param>
    /// <returns>the path object</returns>
    public abstract FilePath GetPath(string path);

    /// <summary>
    /// The name of the file or directory: what is after the last slash, or after the colon of the
    /// scheme when there is no slash.
    /// </summary>
    /// <returns>the last element of the path</returns>
    public string GetName()
    {
        int index = Math.Max(Name.IndexOf(':', StringComparison.Ordinal), Name.LastIndexOf('/'));
        return index < 0 ? Name : Name[(index + 1)..];
    }

    /// <summary>
    /// The path without the wrapper prefixes, when this file system wraps another one.
    /// </summary>
    /// <returns>the path this one stands for</returns>
    public virtual FilePath Unwrap() => this;

    /// <summary>
    /// How long the file is.
    /// </summary>
    /// <returns>the length in bytes</returns>
    public abstract long Size();

    /// <summary>
    /// Whether something exists at this path.
    /// </summary>
    /// <returns>true if it exists</returns>
    public abstract bool Exists();

    /// <summary>
    /// Open the file for random access.
    /// </summary>
    /// <param name="mode">
    /// the access mode: <c>r</c> to read, and <c>rw</c>, <c>rws</c> or <c>rwd</c> to read and write.
    /// The two trailing letters of the reference's modes ask the operating system to sync on write
    /// and on metadata; here they are accepted and mean <c>rw</c>, and a caller that wants the sync
    /// asks for it with <see cref="FileChannel.Flush"/>.
    /// </param>
    /// <returns>the open file</returns>
    public abstract FileChannel Open(string mode);

    /// <summary>
    /// A stream to read the file through.
    /// </summary>
    /// <returns>the stream, positioned at the beginning</returns>
    public Stream NewInputStream()
        => new FileChannelStream(Open("r"), ownsChannel: true);

    /// <summary>
    /// A stream to write the file through, either adding to what is there or starting over.
    /// </summary>
    /// <param name="append">true to keep the contents and write after them</param>
    /// <returns>the stream</returns>
    /// <remarks>
    /// The stream owns the file it was handed: closing it closes the handle, which is what a caller
    /// that was given a stream rather than a channel expects.
    /// </remarks>
    public Stream NewOutputStream(bool append)
    {
        FileChannel channel = Open(append ? "rws" : "rw");
        if (append)
        {
            channel.Position = channel.Length;
        }
        else
        {
            channel.Position = 0;
            channel.SetLength(0);
        }

        return new FileChannelStream(channel, ownsChannel: true);
    }

    /// <summary>
    /// Create a new empty file.
    /// </summary>
    /// <returns>true if it was created, false if it was already there</returns>
    public abstract bool CreateFile();

    /// <summary>
    /// Delete a file or a directory, the latter only when it is empty.
    /// </summary>
    public abstract void Delete();

    /// <summary>
    /// Create a directory. The reference requires the parent to be there already.
    /// </summary>
    public abstract void CreateDirectory();

    /// <summary>
    /// What is in this directory.
    /// </summary>
    /// <returns>the entries, as paths of the same file system</returns>
    public abstract IReadOnlyList<FilePath> NewDirectoryStream();

    /// <summary>
    /// The directory this path is in.
    /// </summary>
    /// <returns>the parent, or <see langword="null"/> when there is none</returns>
    public abstract FilePath? GetParent();

    /// <summary>
    /// This path with every symbolic link and every "." and ".." resolved.
    /// </summary>
    /// <returns>the normalized path</returns>
    public abstract FilePath ToRealPath();

    /// <summary>
    /// Whether this is a directory.
    /// </summary>
    /// <returns>true if it is a directory</returns>
    public abstract bool IsDirectory();

    /// <summary>
    /// Whether this is a plain file.
    /// </summary>
    /// <returns>true if it is a regular file</returns>
    public abstract bool IsRegularFile();

    /// <summary>
    /// Whether the name is a whole path rather than a relative one.
    /// </summary>
    /// <returns>true if it is absolute</returns>
    public abstract bool IsAbsolute();

    /// <summary>
    /// When the file was last written to, in milliseconds since the epoch.
    /// </summary>
    /// <returns>the time</returns>
    public abstract long LastModified();

    /// <summary>
    /// Whether the file may be written to.
    /// </summary>
    /// <returns>true if it may</returns>
    public abstract bool CanWrite();

    /// <summary>
    /// Make the file read-only, where the file system can do it.
    /// </summary>
    /// <returns>true if it did</returns>
    public abstract bool SetReadOnly();

    /// <summary>
    /// Rename or move.
    /// </summary>
    /// <param name="newName">where it should go</param>
    /// <param name="atomicReplace">
    /// whether the move may replace what is already at the new name
    /// </param>
    public abstract void MoveTo(FilePath newName, bool atomicReplace);

    /// <summary>
    /// Create a new file whose name starts with this path, which is how a caller asks for a name
    /// nobody is using yet.
    /// </summary>
    /// <param name="suffix">what the name ends with</param>
    /// <param name="inTempDir">
    /// whether to put it in the directory of temporary files; the reference does the same thing
    /// either way and keeps the argument for the caller's sake
    /// </param>
    /// <returns>the new path</returns>
    public virtual FilePath CreateTempFile(string suffix, bool inTempDir)
    {
        while (true)
        {
            FilePath path = GetPath(Name + NextTempFileNamePart(false) + suffix);
            if (path.Exists() || !path.CreateFile())
            {
                // The random part could in principle collide; ask for another one.
                _ = NextTempFileNamePart(true);
                continue;
            }

            path.Open("rw").Dispose();
            return path;
        }
    }

    /// <summary>
    /// The middle part of a temporary file's name: a random number, a dot, and a counter.
    /// </summary>
    /// <param name="newRandom">whether to draw a new random part</param>
    /// <returns>the part</returns>
    private static string NextTempFileNamePart(bool newRandom)
    {
        lock (Providers)
        {
            if (newRandom || tempRandom is null)
            {
                tempRandom = MathUtils.RandomInt(int.MaxValue) + ".";
            }

            return tempRandom + tempSequence++;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
