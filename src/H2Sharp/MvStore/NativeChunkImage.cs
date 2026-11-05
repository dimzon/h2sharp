/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * An unmanaged buffer holding a chunk's final image. Chunk images are several megabytes for
 * large chunks; keeping them as byte[] put them on the large object heap. The image is
 * written to the file channel straight from native memory and freed when the write is done
 * (approved deviation, D-039; the transient pooled WriteBuffer still assembles the chunk).
 */
using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace H2Sharp.MvStore;

internal sealed unsafe class NativeChunkImage : MemoryManager<byte>
{
    private readonly void* ptr;

    private readonly int length;

    private NativeChunkImage(int length)
    {
        this.length = length;
        ptr = NativeMemory.Alloc((nuint)length);
    }

    /// <summary>
    /// Allocates an unmanaged image of the given length.
    /// </summary>
    /// <param name="length">the length</param>
    /// <returns>the image</returns>
    internal static NativeChunkImage Create(int length) => new(length);

    /// <inheritdoc/>
    public override Span<byte> GetSpan() => new(ptr, length);

    /// <inheritdoc/>
    public override MemoryHandle Pin(int elementIndex = 0) => new((byte*)ptr + elementIndex);

    /// <inheritdoc/>
    public override void Unpin()
    {
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (ptr is not null)
        {
            NativeMemory.Free(ptr);
        }
    }
}
