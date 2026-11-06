using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using H2Sharp.Util;

namespace H2Sharp.MvStore;

/// <summary>
/// An auto-resize buffer to write data into.
/// </summary>
/// <remarks>
/// The store serializes a page into this and then hands the bytes to the chunk, so the buffer has to
/// grow in the middle of writing and has to keep what was already written.
/// <para>
/// Growth is generous on purpose: at least <see cref="MinGrow"/>, and at least half of what the
/// buffer already holds. A buffer that grew by the smallest amount needed would reallocate on every
/// page, and a page is written one field at a time.
/// </para>
/// <para>
/// <see cref="Clear"/> keeps the buffer for reuse when it is not absurdly large, which is the whole
/// point of a separate <c>reuse</c> field: the steady state is that nothing is allocated at all.
/// </para>
/// <para>
/// Since SP-29 the buffer is native-backed once it grows past <see cref="NativeThreshold"/>: chunk
/// serialization then lives in native memory and the chunk image is handed to the file channel
/// straight from it, without the extra managed-array copy (D-039's image step). Small buffers stay
/// managed.
/// </para>
/// </remarks>
public sealed unsafe class WriteBuffer : IDisposable
{
    /// <summary>
    /// The maximum size of the buffer in order to be re-used after a clear operation.
    /// </summary>
    private const int MaxReuseCapacity = 4 * 1024 * 1024;

    /// <summary>
    /// The minimum number of bytes to grow a buffer at a time.
    /// </summary>
    internal const int MinGrow = 1024 * 1024;

    /// <summary>
    /// Buffers at least this large are allocated in native memory.
    /// </summary>
    internal const int NativeThreshold = 85 * 1024;

    /// <summary>
    /// The managed stub a cleared oversized buffer falls back to: below the LOH threshold,
    /// so the next Grow promotes it to native without a 1 MiB LOH object first.
    /// </summary>
    private const int ColdStartCapacity = 64 * 1024;

    /// <summary>
    /// The buffer that is used after a clear operation (managed form).
    /// </summary>
    private byte[]? reuse;

    /// <summary>
    /// The current managed buffer, when not native.
    /// </summary>
    private byte[]? managed;

    /// <summary>
    /// The current native buffer, when native.
    /// </summary>
    private void* native;

    /// <summary>
    /// The current capacity (managed length or native allocation size).
    /// </summary>
    private int capacity;

    /// <summary>
    /// Whether the current buffer is native.
    /// </summary>
    private bool isNative;

    /// <summary>
    /// A lightweight memory view over the native buffer, created lazily.
    /// </summary>
    private NativeView? view;

    private int position;

    private int limit;

    /// <summary>
    /// Live native allocations, for the off-heap gate tests.
    /// </summary>
    private static long liveAllocations;

    /// <summary>
    /// A buffer of the given size.
    /// </summary>
    /// <param name="initialSize">how many bytes to start with</param>
    public WriteBuffer(int initialSize)
    {
        if (initialSize >= NativeThreshold)
        {
            native = NativeMemory.Alloc((nuint)initialSize);
            isNative = true;
            Interlocked.Increment(ref liveAllocations);
        }
        else
        {
            reuse = new byte[initialSize];
            managed = reuse;
        }

        capacity = initialSize;
        limit = initialSize;
    }

    /// <summary>
    /// A buffer of the default size.
    /// </summary>
    public WriteBuffer()
        : this(MinGrow)
    {
    }

    /// <summary>
    /// How many write buffers are currently allocated in native memory.
    /// </summary>
    internal static long LiveAllocations => Interlocked.Read(ref liveAllocations);

    /// <summary>
    /// Write a variable size integer.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutVarInt(int x)
    {
        DataUtils.WriteVarInt(EnsureCapacity(5), ref position, x);
        return this;
    }

    /// <summary>
    /// Write a variable size long.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutVarLong(long x)
    {
        DataUtils.WriteVarLong(EnsureCapacity(10), ref position, x);
        return this;
    }

    /// <summary>
    /// Write the characters of a string in a format similar to UTF-8.
    /// </summary>
    /// <param name="s">the string</param>
    /// <param name="len">the number of characters to write</param>
    /// <returns>this</returns>
    public WriteBuffer PutStringData(string s, int len)
    {
        DataUtils.WriteStringData(EnsureCapacity(3 * len), ref position, s, len);
        return this;
    }

    /// <summary>
    /// Put a byte.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer Put(byte x)
    {
        _ = EnsureCapacity(1)[position++] = x;
        return this;
    }

    /// <summary>
    /// Put a character.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutChar(char x)
    {
        BinaryPrimitives.WriteUInt16BigEndian(EnsureCapacity(2).Slice(position, 2), (ushort)x);
        position += 2;
        return this;
    }

    /// <summary>
    /// Put a short.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutShort(short x)
    {
        BinaryPrimitives.WriteUInt16BigEndian(EnsureCapacity(2).Slice(position, 2), (ushort)x);
        position += 2;
        return this;
    }

    /// <summary>
    /// Put an integer.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutInt(int x)
    {
        BinaryPrimitives.WriteInt32BigEndian(EnsureCapacity(4).Slice(position, 4), x);
        position += 4;
        return this;
    }

    /// <summary>
    /// Put a long.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutLong(long x)
    {
        BinaryPrimitives.WriteInt64BigEndian(EnsureCapacity(8).Slice(position, 8), x);
        position += 8;
        return this;
    }

    /// <summary>
    /// Put a float.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutFloat(float x) => PutInt(BitConverter.SingleToInt32Bits(x));

    /// <summary>
    /// Put a double.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutDouble(double x) => PutLong(BitConverter.DoubleToInt64Bits(x));

    /// <summary>
    /// Put a byte array.
    /// </summary>
    /// <param name="bytes">the value</param>
    /// <returns>this</returns>
    public WriteBuffer Put(byte[] bytes) => Put(bytes, 0, bytes.Length);

    /// <summary>
    /// Put a part of a byte array.
    /// </summary>
    /// <param name="bytes">the value</param>
    /// <param name="offset">the source offset</param>
    /// <param name="length">the number of bytes</param>
    /// <returns>this</returns>
    public WriteBuffer Put(byte[] bytes, int offset, int length)
    {
        bytes.AsSpan(offset, length).CopyTo(EnsureCapacity(length).Slice(position, length));
        position += length;
        return this;
    }

    /// <summary>
    /// Put the contents of a span.
    /// </summary>
    /// <param name="src">the source</param>
    /// <returns>this</returns>
    public WriteBuffer Put(ReadOnlySpan<byte> src)
    {
        src.CopyTo(EnsureCapacity(src.Length).Slice(position, src.Length));
        position += src.Length;
        return this;
    }

    /// <summary>
    /// Set the limit, possibly growing the buffer.
    /// </summary>
    /// <param name="newLimit">the new limit</param>
    /// <returns>this</returns>
    public WriteBuffer Limit(int newLimit)
    {
        _ = EnsureCapacity(newLimit - position);
        limit = newLimit;
        return this;
    }

    /// <summary>
    /// Get the capacity.
    /// </summary>
    /// <returns>the capacity</returns>
    public int Capacity() => capacity;

    /// <summary>
    /// Set the position.
    /// </summary>
    /// <param name="newPosition">the new position</param>
    /// <returns>this</returns>
    public WriteBuffer Position(int newPosition)
    {
        position = newPosition;
        return this;
    }

    /// <summary>
    /// Get the limit.
    /// </summary>
    /// <returns>the limit</returns>
    public int Limit() => limit;

    /// <summary>
    /// Get the current position.
    /// </summary>
    /// <returns>the position</returns>
    public int Position() => position;

    /// <summary>
    /// Copy the data into the destination array.
    /// </summary>
    /// <param name="dst">the destination array</param>
    /// <returns>this</returns>
    public WriteBuffer Get(byte[] dst)
    {
        CurrentSpan.Slice(position, dst.Length).CopyTo(dst);
        position += dst.Length;
        return this;
    }

    /// <summary>
    /// Update an integer at the given index.
    /// </summary>
    /// <param name="index">the index</param>
    /// <param name="value">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutInt(int index, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(CurrentSpan.Slice(index, 4), value);
        return this;
    }

    /// <summary>
    /// Update a short at the given index.
    /// </summary>
    /// <param name="index">the index</param>
    /// <param name="value">the value</param>
    /// <returns>this</returns>
    public WriteBuffer PutShort(int index, short value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(CurrentSpan.Slice(index, 2), (ushort)value);
        return this;
    }

    /// <summary>
    /// Clear the buffer after use.
    /// </summary>
    /// <remarks>
    /// A buffer that grew past the reuse limit is dropped rather than kept, and one that grew within
    /// it becomes the one to keep. That is the reference's arrangement: keeping a multi-megabyte
    /// buffer alive for a store that mostly writes small pages is a leak with extra steps.
    /// </remarks>
    /// <returns>this</returns>
    public WriteBuffer Clear()
    {
        if (limit > MaxReuseCapacity)
        {
            DropStorage();
            if (managed is null)
            {
                // A small managed stub, not MinGrow: a 1 MiB array would be an LOH object,
                // and the next Grow goes to native anyway (round-4 audit, §1.2).
                managed = reuse ?? GC.AllocateUninitializedArray<byte>(ColdStartCapacity);
                isNative = false;
                capacity = managed.Length;
            }
        }
        else if (!ReferenceEquals(CurrentRef(), reuse))
        {
            reuse = CurrentRef();
        }

        limit = capacity;
        position = 0;
        return this;
    }

    /// <summary>
    /// The bytes written so far.
    /// </summary>
    /// <returns>what was written</returns>
    public byte[] ToArray() => CurrentSpan[..position].ToArray();

    /// <summary>
    /// A view over the written region, for writing the chunk straight to the file channel.
    /// </summary>
    /// <param name="length">how many bytes of the buffer to expose</param>
    /// <returns>the region</returns>
    internal ReadOnlyMemory<byte> GetRegion(int length)
        => isNative
            ? (view ??= new NativeView(native, capacity)).Memory.Slice(0, length)
            : managed!.AsMemory(0, length);

    /// <summary>
    /// A slice of the current backing memory.
    /// </summary>
    internal ReadOnlySpan<byte> Slice(int start, int length) => CurrentSpan.Slice(start, length);

    /// <summary>
    /// Releases native storage, if any. Idempotent.
    /// </summary>
    public void Dispose()
    {
        DropStorage();
    }

    /// <summary>
    /// The current backing span.
    /// </summary>
    private Span<byte> CurrentSpan => isNative ? new Span<byte>(native, capacity) : managed!;

    /// <summary>
    /// The current backing reference (managed form only meaningful when managed).
    /// </summary>
    private byte[]? CurrentRef() => isNative ? null : managed;

    private Span<byte> EnsureCapacity(int len)
    {
        if (limit - position < len)
        {
            Grow(len);
        }

        return CurrentSpan;
    }

    private void Grow(int additional)
    {
        int needed = additional - (limit - position);

        // grow at least MinGrow
        long grow = Math.Max(needed, MinGrow);

        // grow at least 50% of the current size
        grow = Math.Max(capacity / 2, grow);

        // the new capacity is at most int.MaxValue
        int newCapacity = (int)Math.Min(int.MaxValue, (long)capacity + grow);
        if (newCapacity < needed)
        {
            throw new OutOfMemoryException(
                "Capacity: " + newCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " needed: " + needed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Span<byte> oldSpan = CurrentSpan;
        void* newNative = null;
        byte[]? newManaged = null;
        bool targetNative = newCapacity >= NativeThreshold;
        if (targetNative)
        {
            try
            {
                newNative = NativeMemory.Alloc((nuint)newCapacity);
            }
            catch (OutOfMemoryException)
            {
                throw new OutOfMemoryException(
                    "Capacity: " + newCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            Interlocked.Increment(ref liveAllocations);
            oldSpan.Slice(0, position).CopyTo(new Span<byte>(newNative, newCapacity));
        }
        else
        {
            try
            {
                newManaged = new byte[newCapacity];
            }
            catch (OutOfMemoryException)
            {
                throw new OutOfMemoryException(
                    "Capacity: " + newCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            oldSpan.Slice(0, position).CopyTo(newManaged);
        }

        DropStorage();
        native = newNative;
        managed = newManaged;
        isNative = targetNative;
        capacity = newCapacity;
        limit = newCapacity;
        if (!targetNative && newCapacity <= MaxReuseCapacity)
        {
            reuse = newManaged;
        }
    }

    /// <summary>
    /// Frees the current native storage, if any.
    /// </summary>
    private void DropStorage()
    {
        if (isNative && native is not null)
        {
            NativeMemory.Free(native);
            native = null;
            isNative = false;
            Interlocked.Decrement(ref liveAllocations);
        }

        view = null;
    }

    /// <summary>
    /// A memory view over a foreign native buffer; does not own the memory.
    /// </summary>
    private sealed unsafe class NativeView : MemoryManager<byte>
    {
        private readonly void* ptr;

        private readonly int length;

        internal NativeView(void* ptr, int length)
        {
            this.ptr = ptr;
            this.length = length;
        }

        public override Span<byte> GetSpan() => new(ptr, length);

        public override MemoryHandle Pin(int elementIndex = 0) => new((byte*)ptr + elementIndex);

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
