/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * An unmanaged buffer holding a chunk's final image. Chunk images are several megabytes for
 * large chunks; keeping them as byte[] put them on the large object heap. The image is
 * written to the file channel straight from native memory and freed when the write is done
 * (approved deviation, D-039; the transient pooled WriteBuffer still assembles the chunk).
 *
 * Images are pooled per FileStore (SP-27): the per-chunk Alloc/Free churn is replaced by
 * reuse within the pool cap, and Free() releases the native memory when the image does not
 * fit the pool or the store closes.
 */
using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;

namespace H2Sharp.MvStore;

internal sealed unsafe class NativeChunkImage : MemoryManager<byte>
{
    private readonly void* ptr;

    private int length;

    private bool freed;

    /// <summary>
    /// Live native allocations, for the off-heap gate tests: bounded by the pool cap plus
    /// what is currently being written.
    /// </summary>
    private static long liveAllocations;

    private NativeChunkImage(int length)
    {
        this.length = length;
        ptr = NativeMemory.Alloc((nuint)length);
        Interlocked.Increment(ref liveAllocations);
    }

    /// <summary>
    /// How many chunk images are currently allocated (pooled or in flight).
    /// </summary>
    internal static long LiveAllocations => Interlocked.Read(ref liveAllocations);

    /// <summary>
    /// The allocated capacity, which may exceed the length of the image in use.
    /// </summary>
    internal int Capacity => length;

    /// <summary>
    /// Allocates an unmanaged image of the given length.
    /// </summary>
    /// <param name="length">the length</param>
    /// <returns>the image</returns>
    internal static NativeChunkImage Create(int length) => new(length);

    /// <summary>
    /// Releases the native memory. Idempotent; called when the image leaves the pool.
    /// </summary>
    internal void Free()
    {
        if (!freed)
        {
            NativeMemory.Free(ptr);
            freed = true;
            Interlocked.Decrement(ref liveAllocations);
        }
    }

    /// <inheritdoc/>
    public override Span<byte> GetSpan() => new(ptr, length);

    /// <inheritdoc/>
    public override MemoryHandle Pin(int elementIndex = 0) => new((byte*)ptr + elementIndex);

    /// <inheritdoc/>
    public override void Unpin()
    {
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing) => Free();
}
