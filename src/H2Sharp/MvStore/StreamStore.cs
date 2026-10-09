using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using H2Sharp.Util;

namespace H2Sharp.MvStore;

/// <summary>
/// A facility to store streams in a map. Streams are split into blocks, which are stored in a map.
/// Very small streams are inlined in the stream id.
/// </summary>
/// <remarks>
/// The key of the map is a long, incremented for each stored block. The default initial value is 0.
/// Before storing a block the stream store checks whether there is already a block with the next key
/// and, if necessary, searches for the next free entry by binary search over 0 to
/// <see cref="long.MaxValue"/>.
/// <para>
/// The format of the binary id is: an empty id is zero bytes of data. In-place data is encoded as 0,
/// the size (a variable size int), then the data. A stored block is encoded as 1, the length of the
/// block (a variable size int), then the key (a variable size long). Several ids may be concatenated
/// to concatenate the data. If the id is large it is stored itself, encoded as 2, the total length
/// (a variable size long), and the key of the block that holds the id (a variable size long).
/// </para>
/// <para>
/// The reason a large id gets stored rather than written out is worth keeping in mind when reading
/// the id: what looks like a self-reference is the store asking for its own block, and a reader that
/// does not expect tag 2 anywhere in the middle of an id will walk off the end.
/// </para>
/// </remarks>
public sealed class StreamStore
{
    private readonly IDictionary<long, byte[]> map;

    private readonly int minBlockSize;

    private readonly int maxBlockSize;

    private long nextKey;

    private byte[]? nextBuffer;

    private readonly Action<int>? onStoreCallback;

    /// <summary>
    /// Create a stream store instance.
    /// </summary>
    /// <param name="map">the map to store blocks of data</param>
    public StreamStore(IDictionary<long, byte[]> map)
        : this(map, 256, 256 * 1024, null)
    {
    }

    /// <summary>
    /// Create a stream store instance with block size bounds.
    /// </summary>
    /// <param name="map">the map to store blocks of data</param>
    /// <param name="minBlockSize">below this a stream is kept inline</param>
    /// <param name="maxBlockSize">above this a stream is split into blocks</param>
    public StreamStore(IDictionary<long, byte[]> map, int minBlockSize, int maxBlockSize)
        : this(map, minBlockSize, maxBlockSize, null)
    {
    }

    /// <summary>
    /// Create a stream store instance that reports how much it stores.
    /// </summary>
    /// <param name="map">the map to store blocks of data</param>
    /// <param name="onStoreCallback">called with the size of each stored block</param>
    public StreamStore(IDictionary<long, byte[]> map, Action<int>? onStoreCallback)
        : this(map, 256, 256 * 1024, onStoreCallback)
    {
    }

    /// <summary>
    /// Create a stream store instance.
    /// </summary>
    /// <param name="map">the map to store blocks of data</param>
    /// <param name="minBlockSize">below this a stream is kept inline</param>
    /// <param name="maxBlockSize">above this a stream is split into blocks</param>
    /// <param name="onStoreCallback">called with the size of each stored block</param>
    public StreamStore(IDictionary<long, byte[]> map, int minBlockSize, int maxBlockSize, Action<int>? onStoreCallback)
    {
        this.map = map;
        this.minBlockSize = minBlockSize;
        this.maxBlockSize = maxBlockSize;
        this.onStoreCallback = onStoreCallback;
    }

    /// <summary>
    /// The map the blocks live in.
    /// </summary>
    /// <returns>the map</returns>
    public IDictionary<long, byte[]> GetMap() => map;

    /// <summary>
    /// Sets the key the next block will use.
    /// </summary>
    /// <param name="nextKey">the key</param>
    public void SetNextKey(long nextKey) => Volatile.Write(ref this.nextKey, nextKey);

    /// <summary>
    /// The key the next block will use.
    /// </summary>
    /// <returns>the key</returns>
    public long GetNextKey() => Volatile.Read(ref nextKey);

    /// <summary>
    /// The size below which a stream is kept inline.
    /// </summary>
    /// <returns>the size</returns>
    public int GetMinBlockSize() => minBlockSize;

    /// <summary>
    /// The size above which a stream is split into blocks.
    /// </summary>
    /// <returns>the size</returns>
    public long GetMaxBlockSize() => maxBlockSize;

    /// <summary>
    /// Store the stream, and return the id. The stream is not closed.
    /// </summary>
    /// <param name="source">the stream</param>
    /// <returns>the id, possibly empty</returns>
    public byte[] Put(Stream source)
    {
        MemoryStream id = new();
        int level = 0;
        try
        {
            while (!Put(id, source, level))
            {
                if (id.Length > maxBlockSize / 2)
                {
                    id = PutIndirectId(id);
                    level++;
                }
            }
        }
        catch (Exception)
        {
            Remove(id.ToArray());
            throw;
        }

        if (id.Length > minBlockSize * 2)
        {
            id = PutIndirectId(id);
        }

        return id.ToArray();
    }

    private bool Put(MemoryStream id, Stream source, int level)
    {
        if (level > 0)
        {
            MemoryStream id2 = new();
            while (true)
            {
                bool done = Put(id2, source, level - 1);
                if (id2.Length > maxBlockSize / 2)
                {
                    id2 = PutIndirectId(id2);
                    id2.WriteTo(id);
                    return done;
                }

                if (done)
                {
                    id2.WriteTo(id);
                    return true;
                }
            }
        }

        byte[] readBuffer = Interlocked.Exchange(ref nextBuffer, null) ?? new byte[maxBlockSize];
        byte[] buff = ReadInto(source, readBuffer);
        if (buff != readBuffer)
        {
            // re-use the buffer if the result was shorter
            Volatile.Write(ref nextBuffer, readBuffer);
        }

        int len = buff.Length;
        if (len == 0)
        {
            return true;
        }

        bool eof = len < maxBlockSize;
        if (len < minBlockSize)
        {
            // in-place: 0, len (int), data
            id.WriteByte(0);
            DataUtils.WriteVarInt(id, len);
            id.Write(buff, 0, len);
        }
        else
        {
            // block: 1, len (int), blockId (long)
            id.WriteByte(1);
            DataUtils.WriteVarInt(id, len);
            DataUtils.WriteVarLong(id, WriteBlock(buff));
        }

        return eof;
    }

    /// <summary>
    /// Fill the target, or return a shorter copy of it if the stream ends first.
    /// </summary>
    private static byte[] ReadInto(Stream source, byte[] target)
    {
        int copied = 0;
        int remaining = target.Length;
        while (remaining > 0)
        {
            try
            {
                int len = source.Read(target, copied, remaining);
                if (len < 0 || len == 0)
                {
                    // A .NET stream at its end may answer zero rather than minus one, and this loop
                    // would otherwise never move. The reference reads from a byte array stream,
                    // which only ever says minus one, so this is the difference between the two and
                    // not a difference in what a stream means.
                    return copied == target.Length ? target : target[..copied];
                }

                copied += len;
                remaining -= len;
            }
            catch (Exception e) when (e is not IOException)
            {
                throw new IOException(e.Message, e);
            }
        }

        return target;
    }

    private MemoryStream PutIndirectId(MemoryStream id)
    {
        byte[] data = id.ToArray();
        MemoryStream result = new();

        // indirect: 2, total len (long), blockId (long)
        result.WriteByte(2);
        DataUtils.WriteVarLong(result, Length(data));
        DataUtils.WriteVarLong(result, WriteBlock(data));
        return result;
    }

    private long WriteBlock(byte[] data)
    {
        long key = GetAndIncrementNextKey();
        map[key] = data;
        onStoreCallback?.Invoke(data.Length);
        return key;
    }

    /// <summary>
    /// The next key, skipping over anything already stored under it.
    /// </summary>
    /// <remarks>
    /// The binary search is over the whole key space rather than over the keys that happen to be in
    /// the map, so a store that was reopened does not have to know how much of the space it has
    /// already used.
    /// </remarks>
    private long GetAndIncrementNextKey()
    {
        long key = Interlocked.Increment(ref nextKey) - 1;
        if (!map.ContainsKey(key))
        {
            return key;
        }

        lock (this)
        {
            long low = key, high = long.MaxValue;
            while (low < high)
            {
                long x = (long)((ulong)(low + high) >> 1);
                if (map.ContainsKey(x))
                {
                    low = x + 1;
                }
                else
                {
                    high = x;
                }
            }

            key = low;
            Volatile.Write(ref nextKey, key + 1);
            return key;
        }
    }

    /// <summary>
    /// The largest key this id refers to, or -1 when it refers to none.
    /// </summary>
    /// <param name="id">the id</param>
    /// <returns>the key, or -1</returns>
    public long GetMaxBlockKey(byte[] id)
    {
        long maxKey = -1;
        int position = 0;
        while (position < id.Length)
        {
            switch (id[position++])
            {
                case 0:
                    // in-place: 0, len (int), data
                    int len = DataUtils.ReadVarInt(id, ref position);
                    position += len;
                    break;

                case 1:
                    // block: 1, len (int), blockId (long)
                    _ = DataUtils.ReadVarInt(id, ref position);
                    maxKey = Math.Max(maxKey, DataUtils.ReadVarLong(id, ref position));
                    break;

                case 2:
                    // indirect: 2, total len (long), blockId (long)
                    _ = DataUtils.ReadVarLong(id, ref position);
                    long k = DataUtils.ReadVarLong(id, ref position);
                    maxKey = k;
                    long m = GetMaxBlockKey(map[k]);
                    if (m >= 0)
                    {
                        maxKey = Math.Max(maxKey, m);
                    }

                    break;

                default:
                    throw DataUtils.NewIllegalArgumentException("Unsupported id {0}", Hex.BytesToHex(id));
            }
        }

        return maxKey;
    }

    /// <summary>
    /// Drop every block this id refers to.
    /// </summary>
    /// <param name="id">the id</param>
    public void Remove(byte[] id)
    {
        int position = 0;
        while (position < id.Length)
        {
            switch (id[position++])
            {
                case 0:
                    // in-place: 0, len (int), data
                    int len = DataUtils.ReadVarInt(id, ref position);
                    position += len;
                    break;

                case 1:
                    // block: 1, len (int), blockId (long)
                    _ = DataUtils.ReadVarInt(id, ref position);
                    map.Remove(DataUtils.ReadVarLong(id, ref position));
                    break;

                case 2:
                    // indirect: 2, total len (long), blockId (long)
                    _ = DataUtils.ReadVarLong(id, ref position);
                    long k = DataUtils.ReadVarLong(id, ref position);

                    // recurse
                    Remove(map[k]);
                    map.Remove(k);
                    break;

                default:
                    throw DataUtils.NewIllegalArgumentException("Unsupported id {0}", Hex.BytesToHex(id));
            }
        }
    }

    /// <summary>
    /// An id as text, for a message about a broken one.
    /// </summary>
    /// <param name="id">the id</param>
    /// <returns>the text</returns>
    public static string Describe(byte[] id)
    {
        StringBuilder buff = new();
        int position = 0;
        long length = 0;
        while (position < id.Length)
        {
            long block;
            int len;
            switch (id[position++])
            {
                case 0:
                    // in-place: 0, len (int), data
                    len = DataUtils.ReadVarInt(id, ref position);
                    position += len;
                    _ = buff.Append("data len=").Append(len.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    length += len;
                    break;

                case 1:
                    // block: 1, len (int), blockId (long)
                    len = DataUtils.ReadVarInt(id, ref position);
                    length += len;
                    block = DataUtils.ReadVarLong(id, ref position);
                    _ = buff.Append("block ")
                        .Append(block.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Append(" len=")
                        .Append(len.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;

                case 2:
                    // indirect: 2, total len (long), blockId (long)
                    len = DataUtils.ReadVarInt(id, ref position);
                    length += DataUtils.ReadVarLong(id, ref position);
                    block = DataUtils.ReadVarLong(id, ref position);
                    _ = buff.Append("indirect block ")
                        .Append(block.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Append(" len=")
                        .Append(len.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;

                default:
                    _ = buff.Append("error");
                    break;
            }

            _ = buff.Append(", ");
        }

        return _ = buff.Append("length=").Append(length.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToString();
    }

    /// <summary>
    /// How many bytes of data an id stands for.
    /// </summary>
    /// <param name="id">the id</param>
    /// <returns>the length</returns>
    public long Length(byte[] id)
    {
        long length = 0;
        int position = 0;
        while (position < id.Length)
        {
            switch (id[position++])
            {
                case 0:
                    // in-place: 0, len (int), data
                    int len = DataUtils.ReadVarInt(id, ref position);
                    position += len;
                    length += len;
                    break;

                case 1:
                    // block: 1, len (int), blockId (long)
                    length += DataUtils.ReadVarInt(id, ref position);
                    _ = DataUtils.ReadVarLong(id, ref position);
                    break;

                case 2:
                    // indirect: 2, total len (long), blockId (long)
                    length += DataUtils.ReadVarLong(id, ref position);
                    _ = DataUtils.ReadVarLong(id, ref position);
                    break;

                default:
                    throw DataUtils.NewIllegalArgumentException("Unsupported id {0}", Hex.BytesToHex(id));
            }
        }

        return length;
    }

    /// <summary>
    /// Whether the whole of the data sits inside the id.
    /// </summary>
    /// <param name="id">the id</param>
    /// <returns>true when in place</returns>
    public bool IsInPlace(byte[] id)
    {
        int position = 0;
        while (position < id.Length)
        {
            if (id[position++] != 0)
            {
                return false;
            }

            int len = DataUtils.ReadVarInt(id, ref position);
            position += len;
        }

        return true;
    }

    /// <summary>
    /// A stream over the data an id stands for.
    /// </summary>
    /// <param name="id">the id</param>
    /// <returns>the stream</returns>
    public System.IO.Stream Get(byte[] id) => new IdStream(this, id);

    /// <summary>
    /// One stored block.
    /// </summary>
    /// <param name="key">the key</param>
    /// <returns>the bytes</returns>
    /// <exception cref="MvStoreException">there is no such block</exception>
    internal byte[] GetBlock(long key)
    {
        if (!map.TryGetValue(key, out byte[]? data))
        {
            throw DataUtils.NewMvStoreException(DataUtils.ErrorBlockNotFound, "Block {0} not found", key);
        }

        return data;
    }

    /// <summary>
    /// Reads the blocks an id names, in order.
    /// </summary>
    private sealed class IdStream : System.IO.Stream
    {
        private readonly StreamStore store;

        private byte[] idBuffer;

        private int position;

        private int limit;

        private MemoryStream? buffer;

        private long skip;

        private readonly long length;

        private long pos;

        internal IdStream(StreamStore store, byte[] id)
        {
            this.store = store;
            length = store.Length(id);
            idBuffer = id;
            limit = id.Length;
        }

        /// <summary>
        /// A read only stream that is neither seekable nor writeable, which is what an id stream is.
        /// The reference extends InputStream and inherits those answers; System.IO.Stream asks.
        /// </summary>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override long Length => length;

        /// <inheritdoc/>
        public override long Position
        {
            get => pos;
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Flush()
        {
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <summary>
        /// The single byte read and the skip are not declared here. The reference overrides them on
        /// InputStream, but the ones on <see cref="System.IO.Stream"/> are not virtual and cannot be,
        /// and its own do exactly what the reference does: the single byte read goes through
        /// <see cref="Read(byte[], int, int)"/>, and a skip reads and discards. That member is the one
        /// the reference overrides too, and keeping the paths through it means a caller holding a
        /// <see cref="System.IO.Stream"/> gets the same bytes as one holding the concrete type.
        /// </summary>
        public override int Read(byte[] b, int off, int len)
        {
            if (len <= 0)
            {
                return 0;
            }

            while (true)
            {
                if (buffer is null)
                {
                    try
                    {
                        buffer = NextBuffer();
                    }
                    catch (MvStoreException e)
                    {
                        throw new IOException(
                            DataUtils.FormatMessage(
                                DataUtils.ErrorBlockNotFound,
                                "Block not found in id {0}",
                                Hex.BytesToHex(idBuffer)),
                            e);
                    }

                    if (buffer is null)
                    {
                        return -1;
                    }
                }

                int result = buffer.Read(b, off, len);
                if (result > 0)
                {
                    pos += result;
                    return result;
                }

                buffer = null;
            }
        }

        private MemoryStream? NextBuffer()
        {
            while (position < limit)
            {
                switch (idBuffer[position++])
                {
                    case 0:
                        {
                            int len = DataUtils.ReadVarInt(idBuffer, ref position);
                            if (skip >= len)
                            {
                                skip -= len;
                                position += len;
                                continue;
                            }

                            int p = (int)(position + skip);
                            int l = (int)(len - skip);
                            position = p + l;
                            return new MemoryStream(idBuffer, p, l, false);
                        }

                    case 1:
                        {
                            int len = DataUtils.ReadVarInt(idBuffer, ref position);
                            long key = DataUtils.ReadVarLong(idBuffer, ref position);
                            if (skip >= len)
                            {
                                skip -= len;
                                continue;
                            }

                            byte[] data = store.GetBlock(key);
                            int s = (int)skip;
                            skip = 0;
                            return new MemoryStream(data, s, data.Length - s, false);
                        }

                    case 2:
                        {
                            long len = DataUtils.ReadVarLong(idBuffer, ref position);
                            long key = DataUtils.ReadVarLong(idBuffer, ref position);
                            if (skip >= len)
                            {
                                skip -= len;
                                continue;
                            }

                            byte[] k = store.GetBlock(key);
                            byte[] grown = new byte[k.Length + (limit - position)];
                            k.CopyTo(grown, 0);
                            Array.Copy(idBuffer, position, grown, k.Length, limit - position);
                            idBuffer = grown;
                            limit = grown.Length;

                            // From the start, not from after the block. The block that was spliced in
                            // is itself the next thing to parse, and skipping it is what makes a read
                            // of an indirect id come back empty instead of returning its data.
                            position = 0;
                            return NextBuffer();
                        }

                    default:
                        throw DataUtils.NewIllegalArgumentException(
                            "Unsupported id {0}",
                            Hex.BytesToHex(idBuffer));
                }
            }

            return null;
        }
    }
}
