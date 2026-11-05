/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Port of the reference's LobStorageMap (branch lob-storage-native). The reference keeps
 * LOBs in dedicated maps inside the MVStore and streams them through a stream store; this
 * port uses two maps: lobBlocks (long -> byte[] in 256 KiB blocks) and lobMeta
 * (long -> BlobMeta). A LOB that fits in the record is never created here — the callers
 * decide by GetMaxLengthInplaceLob.
 *
 * Deviation D-041: no reference counting / deferred cleanup executor; removal is
 * synchronous, and copyLob only retargets the table id.
 */
using System;
using System.Buffers;
using System.IO;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Store;
using H2Sharp.Values;
using H2Sharp.Values.Lobs;

namespace H2Sharp.MvStore.Db;

public sealed partial class LobStorageMap : LobStorageInterface
{
    /// <summary>
    /// The block size of a stored LOB.
    /// </summary>
    internal const int BlockSize = 256 * 1024;

    private readonly Database database;

    private readonly MVMap<long, object> blocks;

    private readonly MVMap<long, object> meta;

    private long nextLobId;

    /// <summary>
    /// Creates a LOB storage over the database's MVStore.
    /// </summary>
    /// <param name="database">the database</param>
    public LobStorageMap(Database database)
    {
        this.database = database;
        MVStore store = database.GetMvStore();
        blocks = store.OpenMap<long, object>("lobBlocks");
        meta = store.OpenMap<long, object>("lobMeta");
        foreach (long key in meta.KeySet())
        {
            nextLobId = Math.Max(nextLobId, key + 1);
        }
    }

    private long GenerateLobId() => nextLobId++;

    /// <inheritdoc/>
    public ValueClob CreateClob(TextReader reader, long maxLength)
    {
        string text = maxLength > 0 ? ReadLimited(reader, maxLength) : reader.ReadToEnd();
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        long lobId = StoreBytes(bytes, text.Length);
        return new ValueClob(new LobDataStore(this, lobId, bytes.Length), bytes.Length, text.Length);
    }

    /// <inheritdoc/>
    public ValueBlob CreateBlob(Stream input, long maxLength)
    {
        byte[] bytes = ReadLimited(input, maxLength);
        long lobId = StoreBytes(bytes);
        return new ValueBlob(new LobDataStore(this, lobId, bytes.Length), bytes.Length);
    }

    private static string ReadLimited(TextReader reader, long maxLength)
    {
        char[] buffer = ArrayPool<char>.Shared.Rent((int)Math.Min(maxLength, 1 << 20));
        try
        {
            var sb = new StringBuilder();
            int total = 0;
            int read;
            while (total < maxLength && (read = reader.Read(buffer, 0, (int)Math.Min(buffer.Length, maxLength - total))) > 0)
            {
                sb.Append(buffer, 0, read);
                total += read;
            }

            return sb.ToString();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static byte[] ReadLimited(Stream input, long maxLength)
    {
        using var memory = new MemoryStream();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, maxLength > 0 ? maxLength - total : buffer.Length))) > 0)
            {
                memory.Write(buffer, 0, read);
                total += read;
                if (maxLength > 0 && total >= maxLength)
                {
                    break;
                }
            }

            return memory.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private long StoreBytes(byte[] bytes, long charLength = 0)
    {
        long lobId = GenerateLobId();
        long index = 0;
        for (int offset = 0; offset < bytes.Length; offset += BlockSize)
        {
            int len = Math.Min(BlockSize, bytes.Length - offset);
            byte[] block = new byte[len];
            Array.Copy(bytes, offset, block, 0, len);
            blocks.Put((lobId << 20) | index, block);
            index++;
        }

        meta.Put(lobId, new BlobMeta(new byte[0], tableId: 0, bytes.Length, 0, charLength));
        return lobId;
    }

    /// <summary>
    /// The meta record of a stored LOB.
    /// </summary>
    /// <param name="lobId">the LOB id</param>
    /// <returns>the meta, or null when there is none</returns>
    internal BlobMeta? GetLobMeta(long lobId) => meta.Get(lobId) as BlobMeta;

    /// <inheritdoc/>
    public ValueLob CopyLob(ValueLob old, int tableId)
    {
        if (old.GetLobData() is LobDataStore store)
        {
            if (meta.Get(store.LobId) is BlobMeta existing)
            {
                meta.Put(store.LobId, new BlobMeta(existing.StreamStoreId, tableId, existing.ByteCount, existing.Hash));
                store.TableId = tableId;
            }
        }

        return old;
    }

    /// <inheritdoc/>
    public Stream GetInputStream(long lobId, long byteCount)
        => new LobInputStream(this, lobId, byteCount);

    /// <inheritdoc/>
    public Stream GetInputStream(long lobId, int tableId, long byteCount)
        => GetInputStream(lobId, byteCount);

    /// <inheritdoc/>
    public void RemoveLob(ValueLob lob)
    {
        if (lob.GetLobData() is LobDataStore store)
        {
            DeleteLob(store.LobId);
        }
    }

    /// <inheritdoc/>
    public void RemoveAllForTable(int tableId)
    {
        foreach (long key in meta.KeySet())
        {
            if (meta.Get(key) is BlobMeta blobMeta && blobMeta.TableId == tableId)
            {
                DeleteLob(key);
            }
        }
    }

    private void DeleteLob(long lobId)
    {
        if (meta.Remove(lobId) is BlobMeta removed)
        {
            long blockCount = (removed.ByteCount + BlockSize - 1) / BlockSize;
            for (long i = 0; i < blockCount; i++)
            {
                blocks.Remove((lobId << 20) | i);
            }
        }
    }

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <summary>
    /// Reads a stored LOB block by block.
    /// </summary>
    private sealed class LobInputStream : Stream
    {
        private readonly LobStorageMap storage;

        private readonly long lobId;

        private readonly long byteCount;

        private long position;

        private byte[]? currentBlock;

        private int blockOffset;

        internal LobInputStream(LobStorageMap storage, long lobId, long byteCount)
        {
            this.storage = storage;
            this.lobId = lobId;
            this.byteCount = byteCount;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => byteCount;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (position >= byteCount)
            {
                return 0;
            }

            int total = 0;
            while (total < count && position < byteCount)
            {
                if (currentBlock is null || blockOffset >= currentBlock.Length)
                {
                    long blockIndex = position / BlockSize;
                    currentBlock = (byte[]?)storage.blocks.Get((lobId << 20) | blockIndex);
                    blockOffset = 0;
                    if (currentBlock is null)
                    {
                        break;
                    }
                }

                int take = (int)Math.Min(count - total, currentBlock.Length - blockOffset);
                Array.Copy(currentBlock, blockOffset, buffer, offset + total, take);
                blockOffset += take;
                position += take;
                total += take;
            }

            return total;
        }

        public override int ReadByte()
        {
            if (position >= byteCount)
            {
                return -1;
            }

            byte[] one = new byte[1];
            return Read(one, 0, 1) == 0 ? -1 : one[0];
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
