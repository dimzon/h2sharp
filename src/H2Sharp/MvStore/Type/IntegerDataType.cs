using System;
using System.Globalization;

namespace H2Sharp.MvStore.Type;

/// <summary>
/// Whole numbers, held as the variable length form the store uses everywhere.
/// </summary>
public sealed class IntegerDataType : BasicDataType<int>
{
    /// <summary>
    /// The one instance.
    /// </summary>
    public static readonly IntegerDataType Instance = new();

    private IntegerDataType()
    {
    }

    /// <inheritdoc/>
    public override int GetMemory(int obj) => 4;

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, int data) => _ = buff.PutVarInt(data);

    /// <inheritdoc/>
    public override int Read(Span<byte> buff, ref int position) => DataUtils.ReadVarInt(buff, ref position);

    /// <inheritdoc/>
    public override int[] CreateStorage(int size) => size == 0 ? [] : new int[size];

    /// <inheritdoc/>
    public override int Compare(int one, int two) => one.CompareTo(two);

    /// <inheritdoc/>
    /// <remarks>
    /// The reference overrides the search so that it compares the boxed values with <c>&gt;</c> rather
    /// than through the comparer, because in a page of a stored map the comparison happens often enough
    /// that unboxing once per step is worth the duplicated loop. This port stores plain <c>int</c>s, so
    /// there is nothing to unbox and the inherited search does the same work in fewer lines.
    /// </remarks>
    public override int BinarySearch(int key, int[] storage, int size, int initialGuess)
        => base.BinarySearch(key, storage, size, initialGuess);
}
