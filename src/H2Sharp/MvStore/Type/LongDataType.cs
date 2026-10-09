using System;

namespace H2Sharp.MvStore.Type;

/// <summary>
/// Wide whole numbers, held as the variable length form the store uses everywhere.
/// </summary>
public sealed class LongDataType : BasicDataType<long>
{
    /// <summary>
    /// The one instance.
    /// </summary>
    public static readonly LongDataType Instance = new();

    private LongDataType()
    {
    }

    /// <inheritdoc/>
    public override int GetMemory(long obj) => 8;

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, long data) => _ = buff.PutVarLong(data);

    /// <inheritdoc/>
    public override long Read(Span<byte> buff, ref int position) => DataUtils.ReadVarLong(buff, ref position);

    /// <inheritdoc/>
    public override long[] CreateStorage(int size) => size == 0 ? [] : new long[size];

    /// <inheritdoc/>
    public override int Compare(long one, long two) => one.CompareTo(two);

    /// <inheritdoc/>
    /// <remarks>
    /// As with <see cref="IntegerDataType"/>, the reference overrides the search to compare boxed
    /// longs directly. Plain <c>long</c>s here make that unnecessary.
    /// </remarks>
    public override int BinarySearch(long key, long[] storage, int size, int initialGuess)
        => base.BinarySearch(key, storage, size, initialGuess);
}
