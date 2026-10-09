using System;
using System.Globalization;
using System.Text;
using H2Sharp.Util;

namespace H2Sharp.MvStore;

/// <summary>
/// A free space bit set: which blocks of a chunk are in use and which are not.
/// </summary>
/// <remarks>
/// The set is one bit per block, with the first <c>firstFreeBlock</c> bits set because those are the
/// header and never become free. Everything past the last set bit is free, and that free tail is not
/// stored anywhere: <see cref="GetLastFree"/> finds it by asking for the bit after the last used one.
/// <para>
/// Allocation walks the set looking for a run of clear bits long enough. The
/// <c>failureFlags</c> register remembers what the last few <em>predictions</em> found, which is how
/// <see cref="IsFragmented"/> knows that guessing is not working without keeping a history.
/// </para>
/// </remarks>
public sealed class FreeSpaceBitSet
{
    /// <summary>
    /// Whether <see cref="ToString"/> also prints a block-by-block breakdown. Off, and kept as a
    /// field rather than a constant so that the code behind it is compiled and not reported as
    /// unreachable.
    /// </summary>
    private static readonly bool DetailedInfo = false;

    /// <summary>
    /// The first usable block.
    /// </summary>
    private readonly int firstFreeBlock;

    /// <summary>
    /// The block size in bytes.
    /// </summary>
    private readonly int blockSize;

    /// <summary>
    /// The bit set.
    /// </summary>
    private readonly LongBitSet set = new();

    /// <summary>
    /// Left-shifting register, which holds outcomes of recent allocations. Only allocations done in
    /// "reuseSpace" mode are recorded here. For example, rightmost bit set to 1 means that last
    /// allocation failed to find a hole big enough, and next bit set to 0 means that previous
    /// allocation request have found one.
    /// </summary>
    private int failureFlags;

    /// <summary>
    /// Create a new free space map.
    /// </summary>
    /// <param name="firstFreeBlock">the first free block</param>
    /// <param name="blockSize">the block size</param>
    public FreeSpaceBitSet(int firstFreeBlock, int blockSize)
    {
        this.firstFreeBlock = firstFreeBlock;
        this.blockSize = blockSize;
        Clear();
    }

    /// <summary>
    /// Reset the list.
    /// </summary>
    public void Clear()
    {
        set.ClearAll();
        set.SetRange(0, firstFreeBlock);
    }

    /// <summary>
    /// Check whether one of the blocks is in use.
    /// </summary>
    /// <param name="pos">the position in bytes</param>
    /// <param name="length">the number of bytes</param>
    /// <returns>true if a block is in use</returns>
    public bool IsUsed(long pos, int length)
    {
        int start = GetBlock(pos);
        int blocks = GetBlockCount(length);
        for (int i = start; i < start + blocks; i++)
        {
            if (!set.Get(i))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Check whether one of the blocks is free.
    /// </summary>
    /// <param name="pos">the position in bytes</param>
    /// <param name="length">the number of bytes</param>
    /// <returns>true if a block is free</returns>
    public bool IsFree(long pos, int length)
    {
        int start = GetBlock(pos);
        int blocks = GetBlockCount(length);
        for (int i = start; i < start + blocks; i++)
        {
            if (set.Get(i))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Allocate a number of blocks and mark them as used.
    /// </summary>
    /// <param name="length">the number of bytes to allocate</param>
    /// <returns>the start position in bytes</returns>
    public long Allocate(int length) => Allocate(length, 0, 0);

    /// <summary>
    /// Allocate a number of blocks and mark them as used.
    /// </summary>
    /// <param name="length">the number of bytes to allocate</param>
    /// <param name="reservedLow">start block index of the reserved area (inclusive)</param>
    /// <param name="reservedHigh">
    /// end block index of the reserved area (exclusive), special value -1 means beginning of the
    /// infinite free area
    /// </param>
    /// <returns>the start position in bytes</returns>
    internal long Allocate(int length, long reservedLow, long reservedHigh)
        => GetPos(Allocate(GetBlockCount(length), (int)reservedLow, (int)reservedHigh, true));

    /// <summary>
    /// Calculate starting position of the prospective allocation.
    /// </summary>
    /// <param name="blocks">the number of blocks to allocate</param>
    /// <param name="reservedLow">start block index of the reserved area (inclusive)</param>
    /// <param name="reservedHigh">
    /// end block index of the reserved area (exclusive), special value -1 means beginning of the
    /// infinite free area
    /// </param>
    /// <returns>the starting block index</returns>
    internal long PredictAllocation(int blocks, long reservedLow, long reservedHigh)
        => Allocate(blocks, (int)reservedLow, (int)reservedHigh, false);

    /// <summary>
    /// Whether the last few guesses came back with less free space than asked for.
    /// </summary>
    /// <returns>true when the set looks fragmented</returns>
    internal bool IsFragmented()
        => System.Numerics.BitOperations.PopCount((uint)(failureFlags & 0x0F)) > 1;

    private int Allocate(int blocks, int reservedLow, int reservedHigh, bool allocate)
    {
        int freeBlocksTotal = 0;
        for (int i = 0; ;)
        {
            int start = set.NextClearBit(i);
            int end = set.NextSetBit(start + 1);
            int freeBlocks = end - start;
            if (end < 0 || freeBlocks >= blocks)
            {
                if ((reservedHigh < 0 || start < reservedHigh) && start + blocks > reservedLow)
                {
                    // overlap detected
                    if (reservedHigh >= 0)
                    {
                        freeBlocksTotal += freeBlocks;
                        i = (int)reservedHigh;
                        continue;
                    }
                }

                if (allocate)
                {
                    set.SetRange(start, start + blocks);
                }
                else
                {
                    failureFlags <<= 1;
                    if (end < 0 && freeBlocksTotal > (4 * blocks))
                    {
                        failureFlags |= 1;
                    }
                }

                return start;
            }

            freeBlocksTotal += freeBlocks;
            i = end;
        }
    }

    /// <summary>
    /// Mark the space as in use.
    /// </summary>
    /// <param name="pos">the position in bytes</param>
    /// <param name="length">the number of bytes</param>
    public void MarkUsed(long pos, int length)
    {
        int start = GetBlock(pos);
        int blocks = GetBlockCount(length);

        // this is not a check because we get called during file opening
        if (set.NextSetBit(start) != -1 && set.NextSetBit(start) < start + blocks)
        {
            throw DataUtils.NewMvStoreException(
                DataUtils.ErrorFileCorrupt,
                "Double mark: " + start.ToString("x", CultureInfo.InvariantCulture)
                + "/" + blocks.ToString("x", CultureInfo.InvariantCulture) + " " + this);
        }

        set.SetRange(start, start + blocks);
    }

    /// <summary>
    /// Mark the space as free.
    /// </summary>
    /// <param name="pos">the position in bytes</param>
    /// <param name="length">the number of bytes</param>
    public void Free(long pos, int length)
    {
        int start = GetBlock(pos);
        int blocks = GetBlockCount(length);
        set.ClearRange(start, start + blocks);
    }

    private long GetPos(int block) => (long)block * blockSize;

    private int GetBlock(long pos) => (int)(pos / blockSize);

    private int GetBlockCount(int length) => MathUtils.RoundUpInt(length, blockSize) / blockSize;

    /// <summary>
    /// Get the fill rate of the space in percent. The value 0 means the space is completely free, and
    /// 100 means it is completely full.
    /// </summary>
    /// <returns>the fill rate (0 - 100)</returns>
    internal int GetFillRate()
    {
        int usedBlocks = set.Cardinality() - firstFreeBlock;
        int totalBlocks = set.Length - firstFreeBlock;
        return totalBlocks == 0 ? 0 : (int)(((100L * usedBlocks) + totalBlocks - 1) / totalBlocks);
    }

    /// <summary>
    /// Get the position of the first free space.
    /// </summary>
    /// <returns>the position</returns>
    internal long GetFirstFree() => GetPos(set.NextClearBit(0));

    /// <summary>
    /// Get the position of the last (infinite) free space.
    /// </summary>
    /// <returns>the position</returns>
    internal long GetLastFree() => GetPos(GetAfterLastBlock());

    /// <summary>
    /// Get the index of the first block after last occupied one. It marks the beginning of the last
    /// (infinite) free space.
    /// </summary>
    /// <returns>block index</returns>
    internal int GetAfterLastBlock() => set.PreviousSetBit(set.Capacity - 1) + 1;

    /// <summary>
    /// Calculates relative "priority" for chunk to be moved.
    /// </summary>
    /// <remarks>
    /// The most desirable chunks to move are the ones sitting within a relatively short span of
    /// occupied blocks which is surrounded from both sides by relatively long free spans.
    /// </remarks>
    /// <param name="block">where chunk starts</param>
    /// <returns>priority, bigger number indicate that chunk need to be moved sooner</returns>
    internal int GetMovePriority(int block)
    {
        int prevEnd = set.PreviousClearBit(block);
        int freeSize;
        if (prevEnd < 0)
        {
            prevEnd = firstFreeBlock;
            freeSize = 0;
        }
        else
        {
            freeSize = prevEnd - set.PreviousSetBit(prevEnd);
        }

        int nextStart = set.NextClearBit(block);
        int nextEnd = set.NextSetBit(nextStart);
        if (nextEnd >= 0)
        {
            freeSize += nextEnd - nextStart;
        }

        return ((nextStart - prevEnd - 1) * 1000) / (freeSize + 1);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        StringBuilder buff = new();
        if (DetailedInfo)
        {
            int onCount = 0, offCount = 0;
            int on = 0;
            for (int i = 0; i < set.Length; i++)
            {
                if (set.Get(i))
                {
                    onCount++;
                    on++;
                }
                else
                {
                    offCount++;
                }

                if ((i & 1023) == 1023)
                {
                    _ = buff.Append(on.ToString("x3", CultureInfo.InvariantCulture)).Append(' ');
                    on = 0;
                }
            }

            _ = buff.Append('\n')
                .Append(" on ").Append(onCount).Append(" off ").Append(offCount)
                .Append(' ').Append((100 * onCount / (onCount + offCount)).ToString(CultureInfo.InvariantCulture))
                .Append("% used ");
        }

        _ = buff.Append('[');
        for (int i = 0; ;)
        {
            if (i > 0)
            {
                _ = buff.Append(", ");
            }

            int start = set.NextClearBit(i);
            _ = buff.Append(start.ToString("x", CultureInfo.InvariantCulture)).Append('-');
            int end = set.NextSetBit(start + 1);
            if (end < 0)
            {
                break;
            }

            _ = buff.Append((end - 1).ToString("x", CultureInfo.InvariantCulture));
            i = end + 1;
        }

        return _ = buff.Append(']').ToString();
    }
}
