using System;
using System.Buffers;

namespace H2Sharp.MvStore;

/// <summary>
/// A byte buffer for one read, rented from the shared pool — or a view that owns nothing.
/// </summary>
/// <remarks>
/// A page read that allocated its buffer would garbage a page per miss; a view into a chunk
/// image must never be returned anywhere. The two travel in one type so that the reader
/// cannot mistake one for the other: disposing a view does nothing, disposing a rental
/// hands it back. The buffer is used and disposed within one call, while the page it feeds
/// materializes everything it reads.
/// </remarks>
public readonly struct RentedBuffer : IDisposable
{
    private readonly Memory<byte> memory;

    private readonly byte[]? rented;

    private RentedBuffer(Memory<byte> memory, byte[]? rented)
    {
        this.memory = memory;
        this.rented = rented;
    }

    /// <summary>
    /// A view over memory owned elsewhere. Disposing does nothing.
    /// </summary>
    /// <param name="memory">the memory</param>
    /// <returns>the view</returns>
    public static RentedBuffer View(Memory<byte> memory) => new(memory, null);

    /// <summary>
    /// A buffer of the given length, rented from the shared pool. Disposing hands it back.
    /// </summary>
    /// <remarks>
    /// Uninitialized on purpose: every path fills it completely before reading it, so
    /// zeroing first would be memset for nothing.
    /// </remarks>
    /// <param name="length">how many bytes are needed</param>
    /// <returns>the rental</returns>
    public static RentedBuffer Rent(int length)
    {
        byte[] array = ArrayPool<byte>.Shared.Rent(length);
        return new(new Memory<byte>(array, 0, length), array);
    }

    /// <summary>
    /// The bytes: the rental sliced to its length, or the view.
    /// </summary>
    public Memory<byte> Memory => memory;

    /// <summary>
    /// The bytes as a span.
    /// </summary>
    public Span<byte> Span => memory.Span;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (rented is not null)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
