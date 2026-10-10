using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace H2Sharp.MvStore;

/// <summary>
/// A file store with random access: chunks live at block positions and freed space is tracked,
/// so a new chunk reuses a hole instead of always appending.
/// </summary>
public abstract class RandomAccessStore : FileStore<Chunk>
{
    /// <summary>
    /// The free spaces between the chunks. The first block to use is block 2 (the first two
    /// blocks are the store header). Guarded by the save lock.
    /// </summary>
    protected readonly FreeSpaceBitSet freeSpace = new(2, BlockSize);

    /// <summary>
    /// Allocation mode: false allocates every new chunk at the end of the file, true as close
    /// to the beginning as possible.
    /// </summary>
    private volatile bool reuseSpace = true;

    private long reservedLow;
    private long reservedHigh;
    private bool stopIdleHousekeeping;

    /// <summary>
    /// A random-access file over the given configuration.
    /// </summary>
    protected RandomAccessStore(IDictionary<string, object?> config)
        : base(config)
    {
    }

    /// <inheritdoc/>
    protected override Chunk CreateChunk(int newChunkId) => new SFChunk(newChunkId);

    /// <inheritdoc/>
    public override Chunk CreateChunk(string s) => new SFChunk(s);

    /// <inheritdoc/>
    protected override Chunk CreateChunk(Dictionary<string, string> map) => new SFChunk(map);

    /// <inheritdoc/>
    protected override void MarkUsed(long pos, int length) => freeSpace.MarkUsed(pos, length);

    private bool IsFree(long pos, int length) => freeSpace.IsFree(pos, length);

    private long Allocate(int length, long reservedLow, long reservedHigh)
        => freeSpace.Allocate(length, reservedLow, reservedHigh);

    private long PredictAllocation(int blocks, long reservedLow, long reservedHigh)
        => freeSpace.PredictAllocation(blocks, reservedLow, reservedHigh);

    /// <inheritdoc/>
    public override bool ShouldSaveNow(int unsavedMemory, int autoCommitMemory)
        => unsavedMemory > autoCommitMemory;

    private bool IsFragmented() => freeSpace.IsFragmented();

    /// <inheritdoc/>
    public override bool IsSpaceReused() => reuseSpace;

    /// <inheritdoc/>
    public override void SetReuseSpace(bool reuseSpace) => this.reuseSpace = reuseSpace;

    /// <inheritdoc/>
    protected override void FreeChunkSpace(IEnumerable<Chunk> chunks)
    {
        foreach (Chunk chunk in chunks)
        {
            FreeChunkSpace(chunk);
        }

        Debug.Assert(ValidateFileLength(chunks.ToString()!));
    }

    private void FreeChunkSpace(Chunk chunk)
    {
        Debug.Assert(chunk != null && chunk.IsAllocated(), chunk!.ToString());
        long start = chunk.Block * BlockSize;
        int length = chunk.Len * BlockSize;
        Free(start, length);
    }

    /// <summary>
    /// Marks bytes as free.
    /// </summary>
    protected void Free(long pos, int length) => freeSpace.Free(pos, length);

    /// <inheritdoc/>
    public override int GetFillRate()
    {
        saveChunkLock.Lock();
        try
        {
            return freeSpace.GetFillRate();
        }
        finally
        {
            saveChunkLock.Unlock();
        }
    }

    /// <inheritdoc/>
    protected override bool ValidateFileLength(string msg)
    {
        Debug.Assert(saveChunkLock.IsHeldByCurrentThread());
        Debug.Assert(!GetMvStore().IsLockedByCurrentThread()
            || GetFileLengthInUse() == MeasureFileLengthInUse(),
            GetMvStore().IsLockedByCurrentThread()
            + " && " + GetFileLengthInUse() + " != " + MeasureFileLengthInUse() + " " + msg);
        return true;
    }

    private long MeasureFileLengthInUse()
    {
        Debug.Assert(saveChunkLock.IsHeldByCurrentThread());
        long size = 2;
        foreach (Chunk c in GetChunks().Values)
        {
            if (c.IsAllocated())
            {
                size = Math.Max(size, c.Block + c.Len);
            }
        }

        return size * BlockSize;
    }

    private long GetFirstFree() => freeSpace.GetFirstFree();

    private long GetFileLengthInUse() => freeSpace.GetLastFree();

    /// <inheritdoc/>
    protected override void ReadStoreHeader(bool recoveryMode)
    {
        Chunk? newest = null;
        bool assumeCleanShutdown = true;
        bool validStoreHeader = false;
        // find out which chunk and version are the newest
        // read the first two blocks
        byte[] fileHeaderBlocks = ReadFully((Chunk?)null, 0, 2 * BlockSize);
        byte[] buff = new byte[BlockSize];
        for (int i = 0; i <= BlockSize; i += BlockSize)
        {
            Array.Copy(fileHeaderBlocks, i, buff, 0, BlockSize);
            // the following can fail for various reasons
            try
            {
                Dictionary<string, string>? m = DataUtils.ParseChecksummedMap(buff);
                if (m == null)
                {
                    assumeCleanShutdown = false;
                    continue;
                }

                long version = DataUtils.ReadHexLong(Chunk.ToValues(m), HdrVersion, 0);
                // if both header blocks do agree on version
                // we'll continue on happy path - assume that previous shutdown was clean
                assumeCleanShutdown = assumeCleanShutdown && (newest == null || version == newest.Version);
                if (newest == null || version > newest.Version)
                {
                    validStoreHeader = true;
                    foreach (KeyValuePair<string, string> entry in m)
                    {
                        storeHeader[entry.Key] = entry.Value;
                    }

                    Dictionary<string, object?> values = Chunk.ToValues(m);
                    int chunkId = DataUtils.ReadHexInt(values, HdrChunk, 0);
                    long block = DataUtils.ReadHexLong(values, HdrBlock, 2);
                    Chunk? test = ReadChunkHeaderAndFooter(block, chunkId);
                    if (test != null)
                    {
                        newest = test;
                    }
                }
            }
            catch (Exception)
            {
                assumeCleanShutdown = false;
            }
        }

        if (!validStoreHeader)
        {
            throw DataUtils.NewMvStoreException(
                    DataUtils.ErrorFileCorrupt,
                    "Store header is corrupt: {0}", this);
        }

        ProcessCommonHeaderAttributes();

        assumeCleanShutdown = assumeCleanShutdown && newest != null && !recoveryMode;
        if (assumeCleanShutdown)
        {
            assumeCleanShutdown = DataUtils.ReadHexInt(storeHeader, HdrClean, 0) != 0;
        }

        long fileSize = Size();
        long blocksInStore = fileSize / BlockSize;

        var chunkComparator = Comparer<Chunk>.Create((one, two) =>
        {
            int result = two.Version.CompareTo(one.Version);
            if (result == 0)
            {
                // out of two copies of the same chunk we prefer the one
                // close to the beginning of file (presumably later version)
                result = one.Block.CompareTo(two.Block);
            }

            return result;
        });

        var validChunksByLocation = new Dictionary<long, Chunk>();
        if (assumeCleanShutdown)
        {
            // quickly check latest 20 chunks referenced in meta table
            var chunksToVerify = new PriorityQueue<Chunk, long>();
            try
            {
                SetLastChunk(newest);
                // load the chunk metadata: although meta's root page resides in the lastChunk,
                // traversing meta map might recursively load another chunk(s)
                foreach (Chunk c in GetChunksFromLayoutMap())
                {
                    // might be there already, due to meta traversal
                    // see readPage() ... getChunkIfFound()
                    chunksToVerify.Enqueue(c, c.Version);
                    if (chunksToVerify.Count == 20)
                    {
                        _ = chunksToVerify.TryDequeue(out _, out _);
                    }
                }

                Chunk? verified;
                while (assumeCleanShutdown && chunksToVerify.TryDequeue(out verified, out _) && verified != null)
                {
                    Chunk? test = ReadChunkHeaderAndFooter(verified.Block, verified.Id);
                    assumeCleanShutdown = test != null;
                    if (assumeCleanShutdown)
                    {
                        validChunksByLocation[test!.Block] = test;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                assumeCleanShutdown = false;
            }
        }
        else
        {
            Chunk? tailChunk = DiscoverChunk(blocksInStore);
            if (tailChunk != null)
            {
                blocksInStore = tailChunk.Block; // for a possible full scan later on
                validChunksByLocation[blocksInStore] = tailChunk;
                if (newest == null || tailChunk.Version > newest.Version)
                {
                    newest = tailChunk;
                }
            }

            if (newest != null)
            {
                // read the chunk header and footer,
                // and follow the chain of next chunks
                while (true)
                {
                    validChunksByLocation[newest.Block] = newest;
                    if (newest.Next == 0 || newest.Next >= blocksInStore)
                    {
                        // no (valid) next
                        break;
                    }

                    Chunk? test = ReadChunkHeaderAndFooter(newest.Next, (newest.Id + 1) & Chunk.MaxId);
                    if (test == null || test.Version <= newest.Version)
                    {
                        break;
                    }

                    newest = test;
                }
            }
        }

        if (!assumeCleanShutdown)
        {
            // now we know, that previous shutdown did not go well and file
            // is possibly corrupted but there is still hope for a quick
            // recovery
            bool quickRecovery = !recoveryMode &&
                    FindLastChunkWithCompleteValidChunkSet(chunkComparator, validChunksByLocation, false);
            if (!quickRecovery)
            {
                // scan whole file and try to fetch chunk header and/or footer out of every block
                // matching pairs with nothing in-between are considered as valid chunk
                long block = blocksInStore;
                Chunk? tailChunk;
                while ((tailChunk = DiscoverChunk(block)) != null)
                {
                    block = tailChunk.Block;
                    validChunksByLocation[block] = tailChunk;
                }

                if (!FindLastChunkWithCompleteValidChunkSet(chunkComparator, validChunksByLocation, true)
                        && HasPersistentData())
                {
                    throw DataUtils.NewMvStoreException(
                            DataUtils.ErrorFileCorrupt,
                            "File is corrupted - unable to recover a valid set of chunks");
                }
            }
        }

        Clear();
        // Build the free space list. Chunks which still hold live data are marked
        // first, so that a stale metadata entry of a chunk without any live data can
        // never claim space which is actually occupied by real data.
        var deadOnes = new List<Chunk>();
        foreach (Chunk c in GetChunks().Values)
        {
            if (c.IsLive())
            {
                if (c.IsAllocated())
                {
                    MarkUsed(c.Block * BlockSize, c.Len * BlockSize);
                }
            }
            else
            {
                deadOnes.Add(c);
            }
        }

        foreach (Chunk c in deadOnes)
        {
            if (c.IsAllocated())
            {
                long start = c.Block * BlockSize;
                int length = c.Len * BlockSize;
                if (!IsFree(start, length))
                {
                    // This chunk holds no live data any more and the space it claims
                    // has already been re-used, i.e. its metadata entry was orphaned
                    // in the layout map. Such an entry references no reachable page,
                    // so discarding it loses nothing, while keeping it would render
                    // the whole file unreadable.
                    DropOrphanedChunk(c);
                    continue;
                }

                MarkUsed(start, length);
            }

            RegisterDeadChunk(c);
        }

        Debug.Assert(ValidateFileLength("on open"));
    }

    /// <inheritdoc/>
    protected override void InitializeStoreHeader(long time) => WriteStoreHeader();

    /// <inheritdoc/>
    protected override void AllocateChunkSpace(Chunk chunk, WriteBuffer buff)
    {
        long reservedLow = this.reservedLow;
        long reservedHigh = this.reservedHigh > 0 ? this.reservedHigh : IsSpaceReused() ? 0 : GetAfterLastBlock();
        long filePos = Allocate(buff.Limit(), reservedLow, reservedHigh);
        // calculate and set the likely next position
        if (reservedLow > 0 || reservedHigh == reservedLow)
        {
            chunk.Next = PredictAllocation(chunk.Len, 0, 0);
        }
        else
        {
            // just after this chunk
            chunk.Next = 0;
        }

        chunk.SetBlock(filePos / BlockSize);
    }

    /// <inheritdoc/>
    protected override void WriteChunk(Chunk chunk, WriteBuffer buffer)
    {
        long filePos = chunk.Block * BlockSize;
        WriteFully(chunk, filePos, buffer.GetBuffer(), 0, buffer.Limit());

        // end of the used space is not necessarily the end of the file
        bool storeAtEndOfFile = filePos + buffer.Limit() >= Size();
        bool shouldWriteStoreHeader = ShouldWriteStoreHeader(chunk, storeAtEndOfFile);
        if (shouldWriteStoreHeader)
        {
            WriteStoreHeader();
        }

        if (!storeAtEndOfFile)
        {
            // may only shrink after the store header was written
            ShrinkStoreIfPossible(1);
        }
    }

    private bool ShouldWriteStoreHeader(Chunk c, bool storeAtEndOfFile)
    {
        // whether we need to write the store header
        bool writeStoreHeader = false;
        if (!storeAtEndOfFile)
        {
            Chunk? chunk = lastChunk;
            if (chunk == null)
            {
                writeStoreHeader = true;
            }
            else if (chunk.Next != c.Block)
            {
                // the last prediction did not match
                writeStoreHeader = true;
            }
            else
            {
                long headerVersion = DataUtils.ReadHexLong(storeHeader, HdrVersion, 0);
                if (chunk.Version - headerVersion > 20)
                {
                    // we write after at least every 20 versions
                    writeStoreHeader = true;
                }
                else
                {
                    for (int chunkId = DataUtils.ReadHexInt(storeHeader, HdrChunk, 0);
                            !writeStoreHeader && chunkId <= chunk.Id; ++chunkId)
                    {
                        // one of the chunks in between
                        // was removed
                        writeStoreHeader = !GetChunks().ContainsKey(chunkId);
                    }
                }
            }
        }

        if (storeHeader.Remove(HdrClean))
        {
            writeStoreHeader = true;
        }

        return writeStoreHeader;
    }

    /// <inheritdoc/>
    protected override void WriteCleanShutdownMark()
    {
        ShrinkStoreIfPossible(0);
        storeHeader[HdrClean] = 1;
        WriteStoreHeader();
    }

    /// <inheritdoc/>
    protected override void AdjustStoreToLastChunk()
    {
        storeHeader[HdrClean] = 1;
        WriteStoreHeader();
        ReadStoreHeader(false);
    }

    /// <inheritdoc/>
    protected override void CompactStore(int thresholdFillRate, long maxCompactTime, int maxWriteSize,
        MVStore mvStore)
    {
        SetRetentionTime(0);
        long stopAt = Stopwatch.GetTimestamp() + maxCompactTime * Stopwatch.Frequency / 1000;
        while (Compact(thresholdFillRate, maxWriteSize))
        {
            Sync();
            CompactMoveChunks(thresholdFillRate, maxWriteSize, mvStore);
            if (Stopwatch.GetTimestamp() - stopAt > 0L)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Compacts the store by moving all chunks next to each other, if there is free space
    /// between chunks. This might temporarily increase the file size. Changes are flushed to
    /// the file, and old chunks are overwritten.
    /// </summary>
    public void CompactMoveChunks(int targetFillRate, long moveSize, MVStore mvStore)
    {
        if (IsSpaceReused())
        {
            mvStore.ExecuteFilestoreOperation(() =>
            {
                DropUnusedChunks();
                saveChunkLock.Lock();
                try
                {
                    if (HasPersistentData() && GetFillRate() <= targetFillRate)
                    {
                        CompactMoveChunks(moveSize);
                    }
                }
                finally
                {
                    saveChunkLock.Unlock();
                }
            });
        }
    }

    private void CompactMoveChunks(long moveSize)
    {
        long start = GetFirstFree() / BlockSize;
        IEnumerable<Chunk>? chunksToMove = FindChunksToMove(start, moveSize);
        if (chunksToMove != null)
        {
            CompactMoveChunks(chunksToMove);
        }
    }

    private IEnumerable<Chunk>? FindChunksToMove(long startBlock, long moveSize)
    {
        long maxBlocksToMove = moveSize / BlockSize;
        IEnumerable<Chunk>? result = null;
        if (maxBlocksToMove > 0)
        {
            // instead of selection just closest to beginning of the file,
            // pick smaller chunk(s) which sit in between bigger holes.
            // The heap dequeues the least desirable chunk first, so priorities are negated.
            var queue = new PriorityQueue<Chunk, (int, long)>();
            long size = 0;
            foreach (Chunk chunk in GetChunks().Values)
            {
                if (chunk.IsAllocated() && chunk.Block > startBlock)
                {
                    chunk.CollectPriority = GetMovePriority(chunk);
                    queue.Enqueue(chunk, (-chunk.CollectPriority, -chunk.Block));
                    size += chunk.Len;
                    while (size > maxBlocksToMove)
                    {
                        if (!queue.TryDequeue(out Chunk? removed, out _))
                        {
                            break;
                        }

                        size -= removed.Len;
                    }
                }
            }

            if (queue.Count != 0)
            {
                var list = new List<Chunk>();
                while (queue.TryDequeue(out Chunk? chunk, out _))
                {
                    list.Add(chunk);
                }

                list.Sort(Chunk.PositionComparator.Instance);
                result = list;
            }
        }

        return result;
    }

    private int GetMovePriority(Chunk chunk) => GetMovePriority((int)chunk.Block);

    private void CompactMoveChunks(IEnumerable<Chunk> move)
    {
        Debug.Assert(saveChunkLock.IsHeldByCurrentThread());
        // this will ensure better recognition of the last chunk
        // in case of power failure, since we are going to move older chunks
        // to the end of the file
        WriteStoreHeader();
        Sync();

        using IEnumerator<Chunk> iterator = move.GetEnumerator();
        bool hasFirst = iterator.MoveNext();
        Debug.Assert(hasFirst);
        long leftmostBlock = iterator.Current.Block;
        long originalBlockCount = GetAfterLastBlock();
        // we need to ensure that chunks moved within the following loop
        // do not overlap with space just released by chunks moved before them,
        // hence the need to reserve this area [leftmostBlock, originalBlockCount)
        foreach (Chunk chunk in move)
        {
            MoveChunk(chunk, leftmostBlock, originalBlockCount);
        }

        // update the metadata (hopefully within the file)
        Store(leftmostBlock, originalBlockCount);
        Sync();

        Chunk chunkToMove = lastChunk!;
        Debug.Assert(chunkToMove != null);
        long postEvacuationBlockCount = GetAfterLastBlock();

        bool chunkToMoveIsAlreadyInside = chunkToMove.Block < leftmostBlock;
        bool movedToEOF = !chunkToMoveIsAlreadyInside;
        // move all chunks, which previously did not fit before reserved area
        // now we can re-use previously reserved area [leftmostBlock, originalBlockCount),
        // but need to reserve [originalBlockCount, postEvacuationBlockCount)
        foreach (Chunk c in move)
        {
            if (c.Block >= originalBlockCount &&
                    MoveChunk(c, originalBlockCount, postEvacuationBlockCount))
            {
                Debug.Assert(c.Block < originalBlockCount);
                movedToEOF = true;
            }
        }

        Debug.Assert(postEvacuationBlockCount >= GetAfterLastBlock());

        if (movedToEOF)
        {
            bool moved = MoveChunkInside(chunkToMove, originalBlockCount);

            // store a new chunk with updated metadata (hopefully within a file)
            Store(originalBlockCount, postEvacuationBlockCount);
            Sync();
            // if chunkToMove did not fit within originalBlockCount (move is
            // false), and since now previously reserved area
            // [originalBlockCount, postEvacuationBlockCount) also can be
            // used, lets try to move that chunk into this area, closer to
            // the beginning of the file
            long lastBoundary = moved || chunkToMoveIsAlreadyInside ?
                                    postEvacuationBlockCount : chunkToMove.Block;
            moved = !moved && MoveChunkInside(chunkToMove, lastBoundary);
            if (MoveChunkInside(lastChunk!, lastBoundary) || moved)
            {
                Store(lastBoundary, -1);
            }
        }

        ShrinkStoreIfPossible(0);
        Sync();
    }

    private void WriteStoreHeader()
    {
        var buff = new StringBuilder(112);
        if (HasPersistentData())
        {
            storeHeader[HdrBlock] = lastChunk!.Block;
            storeHeader[HdrChunk] = lastChunk.Id;
            storeHeader[HdrVersion] = lastChunk.Version;
        }

        DataUtils.AppendMap(buff, storeHeader);
        byte[] bytes = Encoding.Latin1.GetBytes(buff.ToString());
        int checksum = DataUtils.GetFletcher32(bytes, 0, bytes.Length);
        DataUtils.AppendMap(buff, HdrFletcher, checksum);
        _ = buff.Append('\n');
        bytes = Encoding.Latin1.GetBytes(buff.ToString());
        byte[] header = new byte[2 * BlockSize];
        Array.Copy(bytes, 0, header, 0, bytes.Length);
        Array.Copy(bytes, 0, header, BlockSize, bytes.Length);
        WriteFully(null, 0, header, 0, header.Length);
    }

    private void Store(long reservedLow, long reservedHigh)
    {
        this.reservedLow = reservedLow;
        this.reservedHigh = reservedHigh;
        saveChunkLock.Unlock();
        try
        {
            Store();
        }
        finally
        {
            saveChunkLock.Lock();
            this.reservedLow = 0;
            this.reservedHigh = 0;
        }
    }

    private bool MoveChunkInside(Chunk chunkToMove, long boundary)
    {
        bool res = chunkToMove.Block >= boundary &&
                PredictAllocation(chunkToMove.Len, boundary, -1) + chunkToMove.Len <= boundary &&
                MoveChunk(chunkToMove, boundary, -1);
        Debug.Assert(!res || chunkToMove.Block + chunkToMove.Len <= boundary);
        return res;
    }

    /// <summary>
    /// Moves a chunk into free area of the file, avoiding the reserved interval when choosing
    /// unallocated space for the new location.
    /// </summary>
    private bool MoveChunk(Chunk chunk, long reservedAreaLow, long reservedAreaHigh)
    {
        // ignore if already removed during the previous store operations
        // those are possible either as explicit commit calls
        // or from meta map updates at the end of this method
        if (!GetChunks().ContainsKey(chunk.Id))
        {
            return false;
        }

        long originalBlock = chunk.Block;
        long start = originalBlock * BlockSize;
        int length = chunk.Len * BlockSize;
        long pos = Allocate(length, reservedAreaLow, reservedAreaHigh);
        try
        {
            long block = pos / BlockSize;
            // in the absence of a reserved area,
            // block should always move closer to the beginning of the file
            Debug.Assert(reservedAreaHigh > 0 || block <= chunk.Block, block + " " + chunk);
            byte[] readBuff = ReadFully(chunk, start, length);
            WriteFully(null, pos, readBuff, 0, length);
            // can not set chunk's new block until it's fully written at new location,
            // because concurrent reader can pick it up prematurely,
            chunk.SetBlock(block);
            chunk.Next = 0;
            Free(start, length);
            SaveChunkMetadataChanges(chunk);
            return true;
        }
        catch (Exception)
        {
            chunk.SetBlock(originalBlock);
            Free(pos, length);
            throw;
        }
    }

    /// <inheritdoc/>
    protected override void ShrinkStoreIfPossible(int minPercent)
    {
        Debug.Assert(ValidateStoreSize());
        ShrinkIfPossible(minPercent);
    }

    private bool ValidateStoreSize()
    {
        Debug.Assert(saveChunkLock.IsHeldByCurrentThread());
        if (GetMvStore().IsLockedByCurrentThread())
        {
            long result = GetFileLengthInUse();
            Debug.Assert(result == MeasureFileLengthInUse(), result + " != " + MeasureFileLengthInUse());
        }

        return true;
    }

    private void ShrinkIfPossible(int minPercent)
    {
        if (IsReadOnly())
        {
            return;
        }

        long end = GetFileLengthInUse();
        long fileSize = Size();
        if (end >= fileSize)
        {
            return;
        }

        if (minPercent > 0 && fileSize - end < BlockSize)
        {
            return;
        }

        int savedPercent = (int)(100 - (end * 100 / fileSize));
        if (savedPercent < minPercent)
        {
            return;
        }

        Sync();
        Truncate(end);
    }

    /// <inheritdoc/>
    protected override void DoHousekeeping(MVStore mvStore)
    {
        bool idle = IsIdle();
        int rewritableChunksFillRate = GetRewritableChunksFillRate();
        if (idle && stopIdleHousekeeping)
        {
            return;
        }

        int autoCommitMemory = mvStore.GetAutoCommitMemory();
        int fileFillRate = GetFillRate();
        long chunksTotalSize = Size() * fileFillRate / 100;
        if (IsFragmented() && fileFillRate < GetAutoCompactFillRate())
        {
            _ = mvStore.TryExecuteUnderStoreLock(() =>
            {
                int moveSize = 2 * autoCommitMemory;
                if (idle)
                {
                    moveSize *= 4;
                }

                CompactMoveChunks(101, moveSize, mvStore);
                return true;
            });
        }

        int chunksFillRate = GetChunksFillRate();
        int adjustedUpFillRate = 50 + rewritableChunksFillRate / 2;
        int fillRateToCompare = idle ? rewritableChunksFillRate : adjustedUpFillRate;
        if (fillRateToCompare < GetTargetFillRate(idle))
        {
            int targetFillRate = idle ? adjustedUpFillRate : rewritableChunksFillRate;
            _ = mvStore.TryExecuteUnderStoreLock(() =>
            {
                int writeLimit = autoCommitMemory;
                if (!idle)
                {
                    writeLimit /= 4;
                }

                if (RewriteChunks(writeLimit, targetFillRate))
                {
                    DropUnusedChunks();
                }

                return true;
            });
        }

        stopIdleHousekeeping = false;
        if (idle)
        {
            int currentChunksFillRate = GetChunksFillRate();
            long currentTotalChunksSize = Size() * GetFillRate() / 100;
            stopIdleHousekeeping = currentTotalChunksSize > chunksTotalSize
                    || currentTotalChunksSize == chunksTotalSize && currentChunksFillRate <= chunksFillRate;
        }
    }

    private int GetTargetFillRate(bool idle)
    {
        int targetRate = GetAutoCompactFillRate();
        // use a lower fill rate if there were any file operations since the last time
        if (!idle)
        {
            targetRate = targetRate * targetRate / 100;
        }

        return targetRate;
    }

    /// <summary>
    /// Truncates the file to the given size.
    /// </summary>
    protected abstract void Truncate(long size);

    /// <inheritdoc/>
    public override void Clear() => freeSpace.Clear();

    /// <summary>
    /// Calculates relative "priority" for a chunk to be moved: bigger means move sooner.
    /// </summary>
    public int GetMovePriority(int block) => freeSpace.GetMovePriority(block);

    /// <summary>
    /// The index of the first block after the last occupied one: the beginning of the last
    /// (infinite) free space.
    /// </summary>
    private long GetAfterLastBlock()
    {
        Debug.Assert(saveChunkLock.IsHeldByCurrentThread());
        return freeSpace.GetAfterLastBlock();
    }

    /// <inheritdoc/>
    protected override IEnumerable<Chunk>? GetRewriteCandidates()
        => IsSpaceReused() ? null : Array.Empty<Chunk>();
}
