/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.fs.split.FileSplit.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.IO;

namespace H2Sharp.Store.FileSystem;

/// <summary>
/// One file as far as a caller is concerned, made of several parts on disk.
/// </summary>
/// <remarks>
/// A position in this file is a position in the whole; which part it lands in is
/// <c>position / maxLength</c>, and where inside that part is the rest. Writing past the end creates
/// the parts it needs, and shortening the file removes the parts that fall past the new end, from
/// the last one backwards, so that an interrupted shortening leaves a shorter file rather than a
/// wrong one.
/// </remarks>
internal sealed class SplitFileChannel : FileChannel
{
    private readonly SplitFilePath filePath;

    private readonly string mode;

    private readonly long maxLength;

    private FileChannel[] parts;

    /// <summary>
    /// What the position, the length and the list of parts are held under: one file is opened once
    /// and read from more than one place, so those three move together.
    /// </summary>
    private readonly object sync = new();

    private long length;

    private long position;

    internal SplitFileChannel(
        SplitFilePath filePath,
        string mode,
        FileChannel[] parts,
        long length,
        long maxLength)
    {
        this.filePath = filePath;
        this.mode = mode;
        this.parts = parts;
        this.length = length;
        this.maxLength = maxLength;
    }

    /// <inheritdoc/>
    public override long Length
    {
        get
        {
            lock (sync)
            {
                return length;
            }
        }
    }

    /// <inheritdoc/>
    public override long Position
    {
        get
        {
            lock (sync)
            {
                return position;
            }
        }

        set
        {
            lock (sync)
            {
                position = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
            }
        }
    }

    /// <inheritdoc/>
    public override Stream Stream => new FileChannelStream(this);

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        lock (sync)
        {
            long available = length - position;
            if (available <= 0)
            {
                return 0;
            }

            int count = (int)Math.Min(buffer.Length, available);
            int done = 0;
            while (done < count)
            {
                FileChannel part = PartAt(position);

                // Where in the part this position lands, and the part is asked to be there before
                // every read: a position in a whole file is not a position in any one of its parts.
                part.Position = position % maxLength;
                int chunk = (int)Math.Min(count - done, maxLength - (position % maxLength));
                chunk = part.Read(buffer.Slice(done, chunk));
                if (chunk <= 0)
                {
                    break;
                }

                done += chunk;
                position += chunk;
            }

            return done;
        }
    }

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        lock (sync)
        {
            if (position >= length && position > maxLength)
            {
                // Writing into a part that is not there yet: every part between the last one and
                // this one has to exist, and the gaps are written as a single byte each so that the
                // file has no holes in it.
                long target = position;
                long x = length - (length % maxLength) + maxLength;
                while (x < target)
                {
                    if (x > length)
                    {
                        position = x - 1;
                        Write([0]);
                    }

                    x += maxLength;
                }

                position = target;
            }

            int done = 0;
            while (done < buffer.Length)
            {
                FileChannel part = PartAt(position);

                // The position is set before every piece: the same reason as on the reading side.
                part.Position = position % maxLength;
                int chunk = (int)Math.Min(buffer.Length - done, maxLength - (position % maxLength));
                part.Write(buffer.Slice(done, chunk));
                done += chunk;
                position += chunk;
                length = Math.Max(length, position);
            }
        }
    }

    /// <inheritdoc/>
    public override int ReadAt(long fileOffset, Span<byte> buffer)
    {
        if (fileOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileOffset));
        }

        lock (sync)
        {
            long available = length - fileOffset;
            if (available <= 0)
            {
                return 0;
            }

            int count = (int)Math.Min(buffer.Length, available);
            int done = 0;
            while (done < count)
            {
                FileChannel part = PartAt(fileOffset);
                int chunk = (int)Math.Min(count - done, maxLength - (fileOffset % maxLength));
                int read = part.ReadAt(fileOffset % maxLength, buffer.Slice(done, chunk));
                if (read <= 0)
                {
                    break;
                }

                done += read;
                fileOffset += read;
            }

            return done;
        }
    }

    /// <inheritdoc/>
    public override void WriteAt(long fileOffset, ReadOnlySpan<byte> buffer)
    {
        if (fileOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileOffset));
        }

        lock (sync)
        {
            if (fileOffset >= length && fileOffset > maxLength)
            {
                // Writing into a part that is not there yet: every part between the last one
                // and this one has to exist, and the gaps are written as a single byte each so
                // that the file has no holes in it.
                long target = fileOffset;
                long x = length - (length % maxLength) + maxLength;
                while (x < target)
                {
                    if (x > length)
                    {
                        WriteAt(x - 1, [0]);
                    }

                    x += maxLength;
                }

                fileOffset = target;
            }

            int done = 0;
            while (done < buffer.Length)
            {
                FileChannel part = PartAt(fileOffset);
                int chunk = (int)Math.Min(buffer.Length - done, maxLength - (fileOffset % maxLength));
                part.WriteAt(fileOffset % maxLength, buffer.Slice(done, chunk));
                done += chunk;
                fileOffset += chunk;
                length = Math.Max(length, fileOffset);
            }
        }
    }

    /// <inheritdoc/>
    public override void SetLength(long newLength)
    {
        lock (sync)
        {
            if (newLength >= length)
            {
                return;
            }

            int newPartCount = 1 + (int)(newLength / maxLength);
            if (newPartCount < parts.Length)
            {
                // The parts past the new end go, last first, so that a shortening that fails half
                // way leaves a shorter file and not a wrong one.
                FileChannel[] kept = new FileChannel[newPartCount];
                for (int i = parts.Length - 1; i >= newPartCount; i--)
                {
                    parts[i].SetLength(0);
                    parts[i].Dispose();
                    filePath.GetBase(i).Delete();
                }

                Array.Copy(parts, kept, newPartCount);
                parts = kept;
            }

            long lastLength = newLength - (maxLength * (newPartCount - 1));
            parts[parts.Length - 1].SetLength(lastLength);
            length = newLength;
        }
    }

    /// <inheritdoc/>
    public override void Flush(bool flushToDisk)
    {
        lock (sync)
        {
            foreach (FileChannel part in parts)
            {
                part.Flush(flushToDisk);
            }
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (sync)
            {
                foreach (FileChannel part in parts)
                {
                    part.Dispose();
                }
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The reference's FileSplit takes the lock on the first part and nothing else.
    /// </remarks>
    public override bool TryLock()
    {
        lock (sync)
        {
            return parts.Length == 0 || parts[0].TryLock();
        }
    }

    /// <inheritdoc/>
    public override void ReleaseLock()
    {
        lock (sync)
        {
            if (parts.Length > 0)
            {
                parts[0].ReleaseLock();
            }
        }
    }

    /// <summary>
    /// The part a position falls in, opened if it is not open yet.
    /// </summary>
    /// <param name="at">the position in the whole file</param>
    /// <returns>the part</returns>
    private FileChannel PartAt(long at)
    {
        int id = (int)(at / maxLength);
        while (id >= parts.Length)
        {
            int i = parts.Length;
            FileChannel[] grown = new FileChannel[i + 1];
            Array.Copy(parts, grown, i);
            grown[i] = filePath.GetBase(i).Open(mode);
            parts = grown;
        }

        return parts[id];
    }
}
