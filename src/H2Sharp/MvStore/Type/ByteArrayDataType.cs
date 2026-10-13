using System;

namespace H2Sharp.MvStore.Type;

/// <summary>
/// Raw byte strings, held as a length followed by the bytes.
/// </summary>
public sealed class ByteArrayDataType : BasicDataType<byte[]>
{
    /// <summary>
    /// The one instance.
    /// </summary>
    public static readonly ByteArrayDataType Instance = new();

    private ByteArrayDataType()
    {
    }

    /// <inheritdoc/>
    public override int GetMemory(byte[] data) => data.Length;

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, byte[] data)
    {
        _ = buff.PutVarInt(data.Length);
        _ = buff.Put(data);
    }

    /// <inheritdoc/>
    public override byte[] Read(Span<byte> buff, ref int position)
    {
        int size = DataUtils.ReadVarInt(buff, ref position);
        byte[] data = new byte[size];
        buff.Slice(position, size).CopyTo(data);
        position += size;
        return data;
    }

    /// <inheritdoc/>
    public override byte[][] CreateStorage(int size) => new byte[size][];

    /// <inheritdoc/>
    /// <remarks>
    /// Unsigned and element by element. The reference uses
    /// <c>Arrays.compareUnsigned</c>, which walks the bytes as unsigned.
    /// </remarks>
    public override int Compare(byte[]? one, byte[]? two)
    {
        if (one is null || two is null)
        {
            return (one?.Length ?? -1).CompareTo(two?.Length ?? -1);
        }

        return one.AsSpan().SequenceCompareTo(two);
    }
}
