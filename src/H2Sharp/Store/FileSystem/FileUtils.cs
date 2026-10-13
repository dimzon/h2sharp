/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.fs.FileUtils.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: only the parts the store, the LOBs and the compression need. The reference's helpers
 * for reading a whole file into a string, for scanning a classpath, for the retrying and the
 * checksum of a chunked file belong to its own file systems and are not here yet; what is here is
 * the mode of an open, the unwrapping of a wrapper, and the two names the store asks about.
 */
using System;
using System.IO;

namespace H2Sharp.Store.FileSystem;

/// <summary>
/// What the store needs to know about paths and files, on top of what <see cref="FilePath"/>
/// already is.
/// </summary>
public static class FileUtils
{
    /// <summary>
    /// Whether an open mode asks for reading only.
    /// </summary>
    /// <param name="mode">
    /// the mode: <c>r</c>, or <c>rw</c> with an <c>s</c> or <c>d</c> after it
    /// </param>
    /// <returns>true for a mode that does not write</returns>
    /// <remarks>
    /// The reference turns the mode into a set of <c>OpenOption</c>s and refuses anything it does
    /// not know. Here the mode is read for one thing only, which is whether the file is written to;
    /// what the trailing letters meant — sync on write, sync on metadata — is a question for the
    /// flush, not for the open.
    /// </remarks>
    public static bool IsReadOnly(string mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        return !mode.StartsWith("rw", StringComparison.Ordinal);
    }

    /// <summary>
    /// The path behind whatever wrappers this one has.
    /// </summary>
    /// <param name="path">the path</param>
    /// <returns>the path that actually opens something</returns>
    public static FilePath Unwrap(FilePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Unwrap();
    }

    /// <summary>
    /// The path for the given text, whichever file system its scheme names.
    /// </summary>
    /// <param name="path">the path text</param>
    /// <returns>the path</returns>
    public static FilePath NewFilePath(string path) => FilePath.Get(path);

    /// <summary>
    /// Read a whole file.
    /// </summary>
    /// <param name="path">the file</param>
    /// <returns>what is in it</returns>
    public static byte[] ReadAllBytes(FilePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using FileChannel channel = path.Open("r");
        if (channel.Length == 0)
        {
            return [];
        }

        byte[] bytes = new byte[channel.Length];
        channel.Position = 0;
        int at = 0;
        while (at < bytes.Length)
        {
            int read = channel.Read(bytes.AsSpan(at));
            if (read <= 0)
            {
                // The file shrank underneath the length it reported.
                return bytes[..at];
            }

            at += read;
        }

        return bytes;
    }

    /// <summary>
    /// Write a whole file, creating it and cutting what was there.
    /// </summary>
    /// <param name="path">the file</param>
    /// <param name="bytes">what to write</param>
    public static void WriteAllBytes(FilePath path, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        using FileChannel channel = path.Open("rw");
        channel.SetLength(0);
        channel.Position = 0;
        channel.Write(bytes);
        channel.Flush(flushToDisk: false);
    }

    /// <summary>
    /// Where temporary files go.
    /// </summary>
    /// <remarks>
    /// The reference answers the system property <c>java.io.tmpdir</c>, and falls back to the
    /// working directory. Here it is the framework's own answer, which is the same thing: the
    /// environment's temporary directory.
    /// </remarks>
    public static string TempDirectory => Path.GetTempPath();

    /// <summary>
    /// Open a file for random access.
    /// </summary>
    /// <param name="fileName">the file</param>
    /// <param name="mode">the access mode</param>
    /// <returns>the open file</returns>
    public static FileChannel Open(string fileName, string mode) => FilePath.Get(fileName).Open(mode);

    /// <summary>
    /// Whether something is at the given path.
    /// </summary>
    /// <param name="fileName">the path</param>
    /// <returns>true if it is there</returns>
    public static bool Exists(string fileName) => FilePath.Get(fileName).Exists();

    /// <summary>
    /// Whether the file may be written to.
    /// </summary>
    /// <param name="fileName">the path</param>
    /// <returns>true if it may</returns>
    public static bool CanWrite(string fileName) => FilePath.Get(fileName).CanWrite();

    /// <summary>
    /// The directory a file is in.
    /// </summary>
    /// <param name="fileName">the file</param>
    /// <returns>the directory, or null when the name has none</returns>
    public static FilePath? GetParent(string fileName) => FilePath.Get(fileName).GetParent();

    /// <summary>
    /// Create a directory and whatever is above it, and do nothing if it is already there.
    /// </summary>
    /// <remarks>
    /// The reference's own <c>createDirectories</c> creates the whole chain, unlike its
    /// <c>FilePath.createDirectory</c>, which requires the parent to exist; the difference matters
    /// because the store calls this one before opening a file in a directory that may be missing.
    /// </remarks>
    /// <param name="directory">the directory, or null for the current one</param>
    public static void CreateDirectories(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        DiskFilePath path = (DiskFilePath)FilePath.Get(directory);
        if (!path.Exists())
        {
            _ = System.IO.Directory.CreateDirectory(path.SystemName);
        }
    }

    /// <summary>
    /// Read exactly as many bytes as asked for, and fail if they are not there.
    /// </summary>
    /// <remarks>
    /// "Exactly" is the point: the store reads whole blocks and treats a short read as damage, while
    /// a plain <c>Read</c> that returns what it found would hand it half a page and call that a
    /// page.
    /// </remarks>
    /// <param name="channel">the open file</param>
    /// <param name="destination">where to read</param>
    /// <exception cref="IOException">if the file ends first</exception>
    public static void ReadFully(FileChannel channel, Span<byte> destination)
    {
        int at = 0;
        while (at < destination.Length)
        {
            int read = channel.Read(destination[at..]);
            if (read <= 0)
            {
                throw new EndOfStreamException("Read " + destination.Length + " bytes at " + at);
            }

            at += read;
        }
    }

    /// <summary>
    /// Write exactly as many bytes as given, and fail if they did not all go.
    /// </summary>
    /// <remarks>
    /// One call is the whole method: unlike Java NIO, <see cref="FileChannel.Write"/> takes all
    /// or throws, so there is no short write to loop over. The old loop advanced by the full
    /// length after every call, which could neither repeat nor detect a short write — it only
    /// implied a handling that the void contract cannot express. A channel that cannot take all
    /// must throw rather than take part; that is what keeps "exactly as many bytes" true.
    /// </remarks>
    /// <param name="channel">the open file</param>
    /// <param name="source">what to write</param>
    /// <exception cref="IOException">if it did not all go</exception>
    public static void WriteFully(FileChannel channel, ReadOnlySpan<byte> source)
    {
        channel.Write(source);
    }
}
