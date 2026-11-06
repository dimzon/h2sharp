/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.mvstore.db.LobStorageMap.
 * SP-28: LOB blocks live in native memory. The map value is this wrapper; the bytes on
 * disk keep the same tagged-array form ObjectDataType used, so the store format is
 * unchanged by the move off the managed heap. */
using System;
using System.Runtime.InteropServices;
using System.Threading;
using H2Sharp.MvStore.Type;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// A LOB block resident in native memory. Freed explicitly: on block removal
/// (<see cref="LobStorageMap"/>) and when the storage closes.
/// </summary>
internal sealed unsafe class NativeLobBlock
{
    private readonly void* ptr;

    private int length;

    private bool freed;

    /// <summary>
    /// Live native LOB blocks, for the off-heap gate tests.
    /// </summary>
    private static long liveAllocations;

    private NativeLobBlock(int length)
    {
        this.length = length;
        ptr = NativeMemory.Alloc((nuint)length);
        Interlocked.Increment(ref liveAllocations);
    }

    /// <summary>
    /// How many LOB blocks are currently allocated.
    /// </summary>
    internal static long LiveAllocations => Interlocked.Read(ref liveAllocations);

    /// <summary>
    /// Copies bytes into a fresh native block.
    /// </summary>
    internal static NativeLobBlock FromBytes(byte[] data)
    {
        NativeLobBlock block = new(data.Length);
        data.CopyTo(new Span<byte>(block.ptr, block.length));
        return block;
    }

    /// <summary>
    /// Copies bytes into a fresh native block.
    /// </summary>
    internal static NativeLobBlock FromSpan(ReadOnlySpan<byte> data)
    {
        NativeLobBlock block = new(data.Length);
        data.CopyTo(new Span<byte>(block.ptr, block.length));
        return block;
    }

    /// <summary>
    /// The block length in bytes.
    /// </summary>
    internal int Length => length;

    /// <summary>
    /// The block bytes.
    /// </summary>
    internal ReadOnlySpan<byte> Bytes => new(ptr, length);

    /// <summary>
    /// Releases the native memory. Idempotent.
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
}

/// <summary>
/// The map data type for <see cref="NativeLobBlock"/> values. The on-disk form is the
/// same tagged-array encoding ObjectDataType uses for byte arrays: short arrays carry
/// <c>TagByteArray015 + len</c>, long ones <c>TypeArray</c> with the sbyte class id
/// (1) and a varint length.
/// </summary>
internal sealed class LobBlockDataType : BasicDataType<object>
{
    /// <summary>
    /// The single instance.
    /// </summary>
    internal static readonly LobBlockDataType Instance = new();

    private LobBlockDataType()
    {
    }

    /// <inheritdoc/>
    public override object[] CreateStorage(int size) => new object[size];

    /// <inheritdoc/>
    public override int GetMemory(object obj)
        => obj is NativeLobBlock block ? (64 + block.Length) * 2 : (64 + ((byte[])obj).Length) * 2;

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, object obj)
    {
        ReadOnlySpan<byte> data = obj is NativeLobBlock block ? block.Bytes : (byte[])obj;
        int len = data.Length;
        if (len <= 15)
        {
            _ = buff.Put((byte)(ObjectDataType.TagByteArray015 + len));
        }
        else
        {
            _ = buff.Put((byte)ObjectDataType.TypeArray).Put((byte)1).PutVarInt(len);
        }

        _ = buff.Put(data);
    }

    /// <inheritdoc/>
    public override object Read(Span<byte> buff, ref int position)
    {
        int tag = buff[position++];
        if (tag != ObjectDataType.TypeArray)
        {
            byte[] raw = ReadBytes(buff, ref position, tag - ObjectDataType.TagByteArray015);
            return NativeLobBlock.FromBytes(raw);
        }

        _ = (sbyte)buff[position++]; // class id: sbyte (1), same as ObjectDataType's arrays
        int len = DataUtils.ReadVarInt(buff, ref position);
        byte[] bytes = ReadBytes(buff, ref position, len);
        return NativeLobBlock.FromBytes(bytes);
    }

    private static byte[] ReadBytes(Span<byte> buff, ref int position, int len)
    {
        byte[] result = new byte[len];
        buff.Slice(position, len).CopyTo(result);
        position += len;
        return result;
    }
}
