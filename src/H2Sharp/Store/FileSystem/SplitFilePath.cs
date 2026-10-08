/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.fs.split.FilePathSplit.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the reference hands out a stream that concatenates the parts, and a channel that knows
 * about them. Here the channel is the only one of the two, and the stream is a view of it: one
 * mechanism instead of two that have to agree about where a part ends.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using H2Sharp.Engine;
using H2Sharp.Message;

namespace H2Sharp.Store.FileSystem;

/// <summary>
/// A file that may be split into several smaller files, which is how a database larger than one
/// file can be kept on a file system that will not take a file that size.
/// </summary>
public sealed class SplitFilePath : FilePathWrapper
{
    /// <summary>
    /// What a part of a split file is called after the file it belongs to.
    /// </summary>
    private const string PartSuffix = ".part";

    private SplitFilePath(string name, FilePath? wrapped)
        : base(name, wrapped)
    {
    }

    /// <summary>
    /// The file system itself, with no file behind it, which is what the registry holds.
    /// </summary>
    internal SplitFilePath()
        : this("split:", null)
    {
    }

    /// <inheritdoc/>
    protected override FilePathWrapper Create(string path, FilePath? basePath)
        => new SplitFilePath(path, basePath);

    /// <inheritdoc/>
    public override string GetScheme() => "split";

    /// <inheritdoc/>
    protected override string GetPrefix() => GetScheme() + ":" + Parse(Name)[0] + ":";

    /// <inheritdoc/>
    protected override FilePath UnwrapFrom(string fileName) => FilePath.Get(Parse(fileName)[1]);

    /// <summary>
    /// The name split into the size of a part and the name of the file itself.
    /// </summary>
    /// <remarks>
    /// The size is a shift: <c>split:26:db.mv.db</c> means a part may be 64 MB. The shift may be
    /// left out, and then the property's own value is used.
    /// </remarks>
    /// <param name="fileName">the name of a split path</param>
    /// <returns>the size and the name of the file</returns>
    /// <exception cref="H2SharpException">if the name is not a split path at all</exception>
    private string[] Parse(string fileName)
    {
        if (!fileName.StartsWith(GetScheme(), StringComparison.Ordinal))
        {
            throw DbException.GetInternalError(fileName + " doesn't start with " + GetScheme());
        }

        fileName = fileName[(GetScheme().Length + 1)..];
        string size;
        if (fileName.Length > 0 && char.IsAsciiDigit(fileName[0]))
        {
            int index = fileName.IndexOf(':', StringComparison.Ordinal);
            if (index < 0)
            {
                // The reference takes the rest of the name as the size, which leaves nothing for the
                // file, and then fails on the part it cannot open. Saying so here is the same
                // failure with a name in it.
                throw DbException.GetInternalError(fileName + " has no file name after the size");
            }

            size = fileName[..index];
            fileName = fileName[(index + 1)..];
        }
        else
        {
            size = SysProperties.SplitFileSizeShift.ToString(CultureInfo.InvariantCulture);
        }

        return [size, fileName];
    }

    /// <summary>
    /// How long a part may be.
    /// </summary>
    /// <returns>the limit</returns>
    private long DefaultMaxLength() => 1L << int.Parse(Parse(Name)[0], CultureInfo.InvariantCulture);

    /// <summary>
    /// The path of one part of this file.
    /// </summary>
    /// <param name="id">which part, counting from zero</param>
    /// <returns>the path of that part</returns>
    internal FilePath GetBase(int id)
        => FilePath.Get(id > 0
            ? GetBase()!.Name + "." + id.ToString(CultureInfo.InvariantCulture) + PartSuffix
            : GetBase()!.Name);

    /// <inheritdoc/>
    public override bool SetReadOnly()
    {
        bool result = false;
        for (int i = 0; ; i++)
        {
            FilePath part = GetBase(i);
            if (!part.Exists())
            {
                break;
            }

            result = part.SetReadOnly();
        }

        return result;
    }

    /// <inheritdoc/>
    public override void Delete()
    {
        for (int i = 0; ; i++)
        {
            FilePath part = GetBase(i);
            if (!part.Exists())
            {
                break;
            }

            part.Delete();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The newest of the parts, since the last part is the one that was written to.
    /// </remarks>
    public override long LastModified()
    {
        long lastModified = 0;
        for (int i = 0; ; i++)
        {
            FilePath part = GetBase(i);
            if (!part.Exists())
            {
                break;
            }

            lastModified = Math.Max(lastModified, part.LastModified());
        }

        return lastModified;
    }

    /// <inheritdoc/>
    public override long Size()
    {
        long length = 0;
        for (int i = 0; ; i++)
        {
            FilePath part = GetBase(i);
            if (!part.Exists())
            {
                break;
            }

            length += part.Size();
        }

        return length;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<FilePath> NewDirectoryStream()
    {
        // The parts are not files of their own as far as the caller is concerned: a directory lists
        // "db.mv.db", not "db.mv.db.3.part".
        List<FilePath> entries = [];
        foreach (FilePath entry in GetBase()!.NewDirectoryStream())
        {
            if (!entry.GetName().EndsWith(PartSuffix, StringComparison.Ordinal))
            {
                FilePathWrapper? wrapped = Wrap(entry);
                if (wrapped is not null)
                {
                    entries.Add(wrapped);
                }
            }
        }

        return entries;
    }

    /// <inheritdoc/>
    public override void MoveTo(FilePath newName, bool atomicReplace)
    {
        ArgumentNullException.ThrowIfNull(newName);
        if (newName is not SplitFilePath other)
        {
            throw new ArgumentException($"Not a split path: {newName.Name}", nameof(newName));
        }

        for (int i = 0; ; i++)
        {
            FilePath part = GetBase(i);
            if (part.Exists())
            {
                part.MoveTo(other.GetBase(i), atomicReplace);
            }
            else if (other.GetBase(i).Exists())
            {
                // There are more parts under the new name than under this one: the file is becoming
                // shorter, so what is left over goes.
                other.GetBase(i).Delete();
            }
            else
            {
                break;
            }
        }
    }

    /// <inheritdoc/>
    public override FileChannel Open(string mode)
    {
        List<FileChannel> parts = [GetBase(0).Open(mode)];
        for (int i = 1; ; i++)
        {
            FilePath part = GetBase(i);
            if (!part.Exists())
            {
                break;
            }

            parts.Add(part.Open(mode));
        }

        FileChannel[] array = [.. parts];
        long maxLength = array[0].Length;
        long length = maxLength;
        if (array.Length == 1)
        {
            // One part, which may be shorter than a part is allowed to be: the file is not split
            // yet, and the limit is the one the name asks for.
            long defaultMaxLength = DefaultMaxLength();
            if (maxLength < defaultMaxLength)
            {
                maxLength = defaultMaxLength;
            }
        }
        else
        {
            if (maxLength == 0)
            {
                ThrowOnBadLength(0, array, maxLength);
            }

            for (int i = 1; i < array.Length - 1; i++)
            {
                long l = array[i].Length;
                length += l;
                if (l != maxLength)
                {
                    ThrowOnBadLength(i, array, maxLength);
                }
            }

            long last = array[array.Length - 1].Length;
            length += last;
            if (last > maxLength)
            {
                ThrowOnBadLength(array.Length - 1, array, maxLength);
            }
        }

        return new SplitFileChannel(this, mode, array, length, maxLength);
    }

    /// <summary>
    /// The parts of a split file do not all have to be the same size, and the middle ones may not
    /// be a different size. A file that is like that was not written by one writer, and opening it
    /// as if it were would read the wrong bytes without noticing.
    /// </summary>
    private void ThrowOnBadLength(int id, FileChannel[] array, long maxLength)
    {
        // What the part really was has to be read before anything is closed: afterwards its length
        // is no longer answerable and the message would name a number nobody can check.
        string message =
            "Expected file length: " + maxLength.ToString(CultureInfo.InvariantCulture)
            + " got: " + array[id].Length.ToString(CultureInfo.InvariantCulture)
            + " for " + GetBase(id).Name;

        foreach (FileChannel channel in array)
        {
            channel.Dispose();
        }

        throw new IOException(message);
    }
}
