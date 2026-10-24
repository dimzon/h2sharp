/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.store.fs.FileChannel's role (see below).
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the reference hands out java.nio.channels.FileChannel. There is no substitute to
 * hand out: FileStream cannot be derived from, and it is not an interface a file in memory could
 * also be. So this is the store's own random-access file, with the four things the store asks of
 * a channel and nothing else.
 *
 * What is gone, deliberately:
 * - locking (FileLock): the store takes its lock in a separate .lock.db file, so the channel lock
 *   is never the one that matters;
 * - mapping (FileChannel.map): it is on the disk path alone and belongs with the store that asks
 *   for it, not with every file system;
 * - the "s" and "d" of the open modes ("rws", "rwd"): Java could ask the operating system to sync
 *   on write or on metadata. Here a flush is a flush, and who wants it says so by calling Flush.
 */
using System;
using System.IO;

namespace H2Sharp.Store.FileSystem;

/// <summary>
/// A file opened for random access: a position, a length, and bytes read or written at that
/// position or at an absolute one.
/// </summary>
/// <remarks>
/// The reference's <c>FileChannel</c> is the same things wrapped in <c>java.nio</c>: positional
/// reads and writes are atomic by nature there. The positional pair below is that contract;
/// the cursor pair exists for the framing layers, which walk a file through a stream.
/// </remarks>
public abstract class FileChannel : IDisposable
{
    /// <summary>
    /// How long the file is.
    /// </summary>
    public abstract long Length { get; }

    /// <summary>
    /// Where the next read or write starts.
    /// </summary>
    public abstract long Position { get; set; }

    /// <summary>
    /// The file as a stream, for the callers that want a stream and not a channel.
    /// </summary>
    /// <remarks>
    /// The store's own paths do not need this; the framing layers, which write whole blocks through
    /// a stream, do.
    /// </remarks>
    public abstract Stream Stream { get; }

    /// <summary>
    /// Shorten or lengthen the file.
    /// </summary>
    /// <param name="value">the new length</param>
    /// <remarks>
    /// Lengthening fills with zeros, as the reference's truncate does: a store grows a file before
    /// it writes into the new space.
    /// </remarks>
    public abstract void SetLength(long value);

    /// <summary>
    /// Read into the buffer at the current position, and move the position on by what was read.
    /// </summary>
    /// <param name="buffer">where to read into</param>
    /// <returns>how many bytes were read; zero at the end of the file</returns>
    public abstract int Read(Span<byte> buffer);

    /// <summary>
    /// Write the buffer at the current position, and move the position on by its length.
    /// </summary>
    /// <remarks>
    /// All of it or an exception: short writes are not representable here (unlike Java NIO,
    /// where write returns a count), so an implementation that cannot take the whole buffer
    /// must throw rather than take part. <see cref="FileUtils.WriteFully"/> stands on this.
    /// </remarks>
    /// <param name="buffer">what to write</param>
    public abstract void Write(ReadOnlySpan<byte> buffer);

    /// <summary>
    /// Read into the buffer at an absolute position, leaving the channel's own position alone.
    /// </summary>
    /// <remarks>
    /// Concurrent positional reads need no lock between them, which is what makes them
    /// cheaper than a save-seek-use-restore around the cursor pair: two interleaved reads
    /// seek each other away, and only a lock fixes that.
    /// </remarks>
    /// <param name="fileOffset">where to read from</param>
    /// <param name="buffer">where to read into</param>
    /// <returns>how many bytes were read; zero at the end of the file</returns>
    public abstract int ReadAt(long fileOffset, Span<byte> buffer);

    /// <summary>
    /// Write the buffer at an absolute position, leaving the channel's own position alone.
    /// </summary>
    /// <param name="fileOffset">where to write</param>
    /// <param name="buffer">what to write</param>
    public abstract void WriteAt(long fileOffset, ReadOnlySpan<byte> buffer);

    /// <summary>
    /// Try to take an operating-system lock on the whole channel without waiting.
    /// </summary>
    /// <remarks>
    /// The reference's <c>FileChannel.tryLock()</c>: an advisory lock over the whole file,
    /// taken by every process that opens the database (the MVStore data file, and the page
    /// store around it), so that a second writer fails instead of corrupting it. .NET has the
    /// same primitive in <see cref="FileStream.Lock"/>, and it interoperates with
    /// <c>java.nio</c> range locks. The base answers no, because a channel that is not a file
    /// on disk has nothing to lock; the in-memory and split channels answer for themselves.
    /// </remarks>
    /// <returns>true if the lock was taken</returns>
    public virtual bool TryLock() => false;

    /// <summary>
    /// Release the lock taken by <see cref="TryLock"/>.
    /// </summary>
    public virtual void ReleaseLock()
    {
    }

    /// <summary>
    /// Push what was written towards the disk.
    /// </summary>
    /// <param name="flushToDisk">
    /// whether to go all the way to the disk rather than to the operating system's cache
    /// </param>
    public abstract void Flush(bool flushToDisk);

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Release what the channel holds.
    /// </summary>
    /// <param name="disposing">whether this is a real disposal rather than a finalizer pass</param>
    protected virtual void Dispose(bool disposing)
    {
    }
}

/// <summary>
/// A stream over one open file channel, for the callers that want a stream and not a channel.
/// </summary>
/// <remarks>
/// One stream for every file system, so that there is a single answer to where a read stops and
/// where a write ends. Closing the stream closes the handle and not the file: the file is what the
/// database opens next, and on the disk it is the same.
/// </remarks>
internal sealed class FileChannelStream : Stream
{
    private readonly FileChannel channel;

    private readonly bool ownsChannel;

    private bool closed;

    /// <summary>
    /// A stream over a channel.
    /// </summary>
    /// <param name="channel">the open file</param>
    /// <param name="ownsChannel">
    /// whether closing the stream closes the file too. It does for a stream handed out by
    /// <see cref="FilePath.NewInputStream"/> and <see cref="FilePath.NewOutputStream"/>, which are
    /// the whole of the handle the caller was given; it does not for a stream reached through a
    /// channel, where the caller holds the channel and closes it itself.
    /// </param>
    internal FileChannelStream(FileChannel channel, bool ownsChannel = false)
    {
        this.channel = channel;
        this.ownsChannel = ownsChannel;
    }

    /// <inheritdoc/>
    public override bool CanRead => !closed;

    /// <inheritdoc/>
    public override bool CanSeek => !closed;

    /// <inheritdoc/>
    public override bool CanWrite => !closed;

    /// <inheritdoc/>
    public override long Length => channel.Length;

    /// <inheritdoc/>
    public override long Position
    {
        get => channel.Position;
        set => channel.Position = value;
    }

    /// <inheritdoc/>
    public override void Flush() => channel.Flush(flushToDisk: false);

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer) => channel.Read(buffer);

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer) => channel.Write(buffer);

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => channel.Position + offset,
            SeekOrigin.End => channel.Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        if (target < 0)
        {
            throw new IOException("Negative position: " + target);
        }

        channel.Position = target;
        return target;
    }

    /// <inheritdoc/>
    public override void SetLength(long value) => channel.SetLength(value);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        closed = true;
        if (disposing && ownsChannel)
        {
            channel.Dispose();
        }

        base.Dispose(disposing);
    }
}
