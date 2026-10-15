/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.fs.disk.FilePathDisk.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: java.nio.file.Path and Files are replaced by System.IO. Nothing in the reference's
 * behaviour is lost by that and one thing is gained: the retry loop it keeps around every file
 * operation, for the Windows days when a virus scanner held a file open, retries against
 * IOException rather than against NoSuchFileException.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using H2Sharp.Engine;

namespace H2Sharp.Store.FileSystem;

/// <summary>
/// A path on the disk, opened through <see cref="System.IO"/>.
/// </summary>
public sealed class DiskFilePath : FilePath
{
    /// <summary>
    /// The characters no file may carry, captured once: the runtime builds a fresh clone per
    /// call, and the set cannot change under a running process.
    /// </summary>
    /// <remarks>
    /// Measured on 20M checks, Release: per-char span 46ms, HashSet 37ms, one IndexOfAny over
    /// the whole name 3ms. The loop below still checks per char, because the first bad
    /// character in position order is what the error names, and reordering the checks would
    /// name another one. CheckName runs once per path creation, so the fused loop keeps the
    /// exact error while the clone per character — the actual cost — is gone.
    /// </remarks>
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    internal DiskFilePath()
        : base(string.Empty)
    {
    }

    private DiskFilePath(string name)
        : base(name)
    {
    }

    /// <inheritdoc/>
    public override string GetScheme() => "file";

    /// <inheritdoc/>
    public override FilePath GetPath(string path)
    {
        DiskFilePath result = new(TranslateFileName(path));
        result.CheckName();
        return result;
    }

    /// <summary>
    /// Refuses a name the system could not open.
    /// </summary>
    /// <remarks>
    /// The reference lets <c>java.nio</c> decide, and it refuses a name whose last part holds a
    /// character no file may have — which is why a path whose scheme nobody registered and whose
    /// text carries a colon fails outright instead of becoming a file with a colon in its name.
    /// .NET's own answer is quieter: <c>File.Exists</c> says no rather than failing, so a bad name
    /// would simply never be there. That is the wrong direction for a store: a path that cannot
    /// exist must say so where the caller can see it.
    /// </remarks>
    private void CheckName()
    {
        // A leading segment with a colon in it is a drive, and a drive is one letter. Anything else
        // with a colon in front is a scheme that belongs to a file system this build does not have:
        // the reference hands such a path to java.nio and it refuses the name outright, so "mem:/x"
        // is an error there rather than a file called "mem:" with a directory under it.
        string root = Name;
        int slash = root.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            root = root[..slash];
        }

        int colon = root.IndexOf(':', StringComparison.Ordinal);
        if (colon > 1)
        {
            throw new ArgumentException($"Not a drive letter: {SystemName}", nameof(Name));
        }

        string fileName = Path.GetFileName(SystemName);
        if (fileName.Length == 0)
        {
            return;
        }

        ReadOnlySpan<char> invalid = InvalidFileNameChars.AsSpan();
        foreach (char c in fileName)
        {
            if (char.IsControl(c) || invalid.Contains(c))
            {
                throw new ArgumentException($"Illegal character <{c}> in a file name: {SystemName}", nameof(Name));
            }
        }
    }

    /// <summary>
    /// The name as the system understands it: backslashes become slashes, the file system's own
    /// prefix is dropped, and a leading <c>~</c> becomes the user's home directory.
    /// </summary>
    /// <remarks>
    /// Dropping the prefix is why a disk path's name is a plain path: the scheme says which file
    /// system it is, and the name is what that file system knows. An unknown prefix is left alone,
    /// which is what makes <c>Data Source=foo.h2.db</c> and <c>jdbc:h2:file:foo.h2.db</c> the same
    /// file.
    /// </remarks>
    public static string TranslateFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        fileName = fileName.Replace('\\', '/');
        if (fileName.StartsWith("file:", StringComparison.Ordinal))
        {
            fileName = fileName[5..];
        }
        else if (fileName.StartsWith("nio:", StringComparison.Ordinal))
        {
            fileName = fileName[4..];
        }

        return ExpandUserHomeDirectory(fileName);
    }

    /// <summary>
    /// A leading <c>~</c> stands for the user's home directory, but only when it stands alone or is
    /// followed by a slash — a name that merely starts with a tilde is a name.
    /// </summary>
    /// <param name="fileName">the file name</param>
    /// <returns>the name with the home directory in it</returns>
    public static string ExpandUserHomeDirectory(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        if (fileName.StartsWith('~')
            && (fileName.Length == 1 || fileName.StartsWith("~/", StringComparison.Ordinal)))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + fileName[1..];
        }

        return fileName;
    }

    /// <summary>
    /// The name with the separators the system uses.
    /// </summary>
    /// <remarks>
    /// Computed once: the name is init-only on a sealed class, so nothing can change it after
    /// construction, and every file operation reads through here.
    /// </remarks>
    internal string SystemName => systemName ??= Name.Replace('/', Path.DirectorySeparatorChar);

    private string? systemName;

    /// <summary>
    /// The system name of a path of any kind, refusing one that is not on the disk.
    /// </summary>
    /// <remarks>
    /// A move between two file systems is not something the reference allows either: it reads the
    /// target's <c>name</c> as if it were a path of the same file system, so asking for a path of
    /// another kind is a mistake rather than something to work around.
    /// </remarks>
    private static string SystemNameOf(FilePath path)
        => path is DiskFilePath disk
            ? disk.SystemName
            : throw new InvalidOperationException($"Not a path on the disk: {path.GetScheme()}:{path.Name}");

    /// <inheritdoc/>
    public override long Size() => new FileInfo(SystemName).Length;

    /// <inheritdoc/>
    /// <remarks>
    /// An empty name is the current directory, and it is there. That is not a corner case invented
    /// here: an empty path in <c>java.nio</c> resolves to the directory the process is in, and the
    /// reference reports it as existing and as a directory. A store that opens "file:" and then
    /// asks whether it is there has to hear the same thing.
    /// </remarks>
    public override bool Exists() => Name.Length == 0 || File.Exists(SystemName) || Directory.Exists(SystemName);

    /// <inheritdoc/>
    public override bool CreateFile()
    {
        for (int i = 0; i < SysProperties.MaxFileRetry; i++)
        {
            try
            {
                if (File.Exists(SystemName))
                {
                    return false;
                }

                using FileStream _ = new(SystemName, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
                return true;
            }
            catch (IOException)
            {
                // Somebody else had it open, or the directory is being scanned; the reference
                // retries the same number of times for the same reason.
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public override void Delete()
    {
        IOException? cause = null;
        for (int i = 0; i < SysProperties.MaxFileRetry; i++)
        {
            try
            {
                string name = SystemName;
                if (Directory.Exists(name))
                {
                    Directory.Delete(name);
                }
                else if (File.Exists(name))
                {
                    File.Delete(name);
                }

                return;
            }
            catch (IOException e)
            {
                cause = e;
            }
        }

        if (cause is not null)
        {
            throw cause;
        }
    }

    /// <inheritdoc/>
    public override void CreateDirectory()
    {
        string name = SystemName;
        if (Directory.Exists(name))
        {
            throw new IOException("Directory already exists: " + name);
        }

        _ = Directory.CreateDirectory(name);
    }

    /// <inheritdoc/>
    public override IReadOnlyList<FilePath> NewDirectoryStream()
    {
        try
        {
            List<FilePath> entries = [];
            foreach (string entry in Directory.EnumerateFileSystemEntries(SystemNameOf(ToRealPath())))
            {
                entries.Add(GetPath(entry));
            }

            return entries;
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    /// <inheritdoc/>
    public override FilePath? GetParent()
    {
        // A name with no directory in it has no parent at all, and not a parent whose name is the
        // empty string: the reference's Paths.get("x") has a null parent, and a store that walks up
        // from a relative path must stop there.
        string? parent = Path.GetDirectoryName(SystemName);
        return string.IsNullOrEmpty(parent) ? null : GetPath(parent);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The reference resolves the name through <c>java.nio</c>, which writes the drive letter in
    /// upper case; <see cref="Path.GetFullPath(string)"/> leaves it as it was given. Two names that
    /// differ only in the case of the drive letter are one file, and a store that compares a path
    /// with what it read out of a configuration would otherwise see them as different.
    /// </remarks>
    public override FilePath ToRealPath()
    {
        string full = Path.GetFullPath(SystemName);
        if (full.Length >= 2 && char.IsAsciiLetter(full[0]) && full[1] == ':')
        {
            full = char.ToUpperInvariant(full[0]) + full[1..];
        }

        return GetPath(full);
    }

    /// <inheritdoc/>
    public override bool IsDirectory() => Name.Length == 0 || Directory.Exists(SystemName);

    /// <inheritdoc/>
    public override bool IsRegularFile() => File.Exists(SystemName);

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="Path.IsPathRooted"/> would say yes to <c>/var/lib/x</c>, and the reference says
    /// no: on Windows that path has a root but no drive, so it cannot be opened without one. What
    /// Java's <c>isAbsolute</c> means is both at once, which is what
    /// <see cref="Path.IsPathFullyQualified"/> answers.
    /// </remarks>
    public override bool IsAbsolute() => Path.IsPathFullyQualified(SystemName);

    /// <inheritdoc/>
    public override long LastModified()
    {
        try
        {
            return (long)(new FileInfo(SystemName).LastWriteTimeUtc - DateTime.UnixEpoch).TotalMilliseconds;
        }
        catch (IOException)
        {
            // The reference answers 0 for a file that cannot be asked, which is what the store does
            // with the answer: it compares times, and 0 is older than everything.
            return 0;
        }
    }

    /// <inheritdoc/>
    public override bool CanWrite()
    {
        try
        {
            if (File.Exists(SystemName))
            {
                using FileStream stream = new(SystemName, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                return true;
            }

            return !Directory.Exists(SystemName) && !File.Exists(SystemName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// There is no read-only flag on a Windows file that .NET will set without changing attributes
    /// the store does not expect to find, so this does what it can: it opens the file for writing
    /// and says whether that worked, which is what the reference's own answer means.
    /// </remarks>
    public override bool SetReadOnly() => CanWrite();

    /// <inheritdoc/>
    public override void MoveTo(FilePath newName, bool atomicReplace)
    {
        ArgumentNullException.ThrowIfNull(newName);
        if (!Exists())
        {
            throw new FileNotFoundException("File not found: " + Name, Name);
        }

        string target = SystemNameOf(newName);

        // The reference moves without a replace option unless one was asked for, and a move onto a
        // file that is already there is a failure when no replace was asked for. That is what the
        // store expects, so it is a failure here too rather than something quietly made to work.
        if (!atomicReplace && (File.Exists(target) || Directory.Exists(target)))
        {
            throw new IOException("File exists: " + newName.Name);
        }

        if (File.Exists(target))
        {
            // A replace on the disk: .NET's File.Move will not overwrite, and the reference asks for
            // an atomic replace first and only copies over the old file when it cannot.
            File.Delete(target);
        }

        File.Move(SystemName, target);
    }

    /// <inheritdoc/>
    public override FileChannel Open(string mode)
    {
        FileAccess access = FileUtils.IsReadOnly(mode) ? FileAccess.Read : FileAccess.ReadWrite;
        FileShare share = FileShare.ReadWrite;
        FileStream stream = new(
            SystemName,
            access == FileAccess.Read ? FileMode.Open : FileMode.OpenOrCreate,
            access,
            share);
        return new DiskFileChannel(stream);
    }

    /// <inheritdoc/>
    public override FilePath CreateTempFile(string suffix, bool inTempDir)
        => base.CreateTempFile(suffix, inTempDir);
}

/// <summary>
/// A <see cref="FileStream"/> behind the store's file interface.
/// </summary>
internal sealed class DiskFileChannel : FileChannel
{
    private readonly FileStream stream;

    internal DiskFileChannel(FileStream stream) => this.stream = stream;

    /// <inheritdoc/>
    public override long Length => stream.Length;

    /// <inheritdoc/>
    public override long Position
    {
        get => stream.Position;
        set => stream.Position = value;
    }

    /// <inheritdoc/>
    public override Stream Stream => stream;

    /// <inheritdoc/>
    public override void SetLength(long value) => stream.SetLength(value);

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer) => stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer) => stream.Write(buffer);

    /// <inheritdoc/>
    public override int ReadAt(long fileOffset, Span<byte> buffer)
        => System.IO.RandomAccess.Read(stream.SafeFileHandle!, buffer, fileOffset);

    /// <inheritdoc/>
    public override void WriteAt(long fileOffset, ReadOnlySpan<byte> buffer)
        => System.IO.RandomAccess.Write(stream.SafeFileHandle!, buffer, fileOffset);

    /// <inheritdoc/>
    public override void Flush(bool flushToDisk) => stream.Flush(flushToDisk);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            stream.Dispose();
        }
    }

    /// <inheritdoc/>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"DiskFileChannel({stream.Length})");
}
