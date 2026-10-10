namespace H2Sharp.MvStore.Tx;

/// <summary>
/// An immutable bit set with a version: flipping a bit answers a new set one version newer,
/// so the set and its version change atomically.
/// </summary>
internal sealed class VersionedBitSet
{
    internal readonly long[] Bits;

    private readonly long version;

    internal VersionedBitSet()
    {
        Bits = [];
        version = 0;
    }

    internal VersionedBitSet(VersionedBitSet other, int bitToFlip)
    {
        Bits = BitSetHelper.Flip(other.Bits, bitToFlip);
        version = other.version + 1;
    }

    internal bool Get(int bitIndex) => BitSetHelper.Get(Bits, bitIndex);

    internal int NextSetBit(int bitIndex) => BitSetHelper.NextSetBit(Bits, bitIndex);

    internal int NextClearBit(int bitIndex) => BitSetHelper.NextClearBit(Bits, bitIndex);

    internal int Length() => BitSetHelper.Length(Bits);

    internal long GetVersion() => version;
}
