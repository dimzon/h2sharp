using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using H2Sharp.Util;

namespace H2Sharp.MvStore;

/// <summary>
/// A chunk of data, containing one or multiple pages.
/// </summary>
/// <remarks>
/// Minimum chunk size is usually 4096 bytes, and it grows in those fixed increments (blocks). A
/// chunk's length and its position in the underlying file store are multiples of that increment, so
/// both are measured in blocks rather than bytes. There are at most 67 million (2^26) chunks, and
/// each chunk is at most 2 GB large.
/// <para>
/// The header and the footer are text, padded with spaces and terminated by a newline, and the
/// lengths of the spaces are fixed so that a reader can find the first page without parsing: the
/// header is at most <see cref="MaxHeaderLength"/> bytes and the footer exactly
/// <see cref="FooterLength"/>. The footer carries a Fletcher-32 checksum of the text before it,
/// which is the only thing standing between a truncated write and a chunk that reads as plausible.
/// </para>
/// <para>
/// This is the half that does not read: everything that goes to the store and everything that
/// describes a chunk is here, while the three members that pull pages back off disk arrive with
/// <c>FileStore</c>, which in turn waits for the page and map machinery.
/// </para>
/// </remarks>
public abstract class Chunk
{
    /// <summary>
    /// The maximum chunk id.
    /// </summary>
    public const int MaxId = (1 << 26) - 1;

    /// <summary>
    /// The maximum length of a chunk header, in bytes.
    /// </summary>
    /// <remarks>
    /// chunk:ffffffff,len:ffffffff,pages:ffffffff,pinCount:ffffffff,max:ffffffffffffffff,map:ffffffff,
    /// root:ffffffffffffffff,time:ffffffffffffffff,version:ffffffffffffffff,next:ffffffffffffffff,toc:ffffffff
    /// </remarks>
    internal const int MaxHeaderLength = 1024; // 199 really

    /// <summary>
    /// The length of the chunk footer. The longest footer is:
    /// chunk:ffffffff,len:ffffffff,version:ffffffffffffffff,fletcher:ffffffff
    /// </summary>
    internal const int FooterLength = 128; // it's really 70 now

    private const string AttrChunk = "chunk";
    private const string AttrBlock = "block";
    private const string AttrLen = "len";
    private const string AttrMap = "map";
    private const string AttrMax = "max";
    private const string AttrNext = "next";
    private const string AttrPages = "pages";
    private const string AttrRoot = "root";
    private const string AttrTime = "time";
    private const string AttrVersion = "version";
    private const string AttrLiveMax = "liveMax";
    private const string AttrLivePages = "livePages";
    private const string AttrUnused = "unused";
    private const string AttrUnusedAtVersion = "unusedAtVersion";
    private const string AttrPinCount = "pinCount";
    private const string AttrToc = "toc";
    private const string AttrOccupancy = "occupancy";
    private const string AttrFletcher = "fletcher";

    /// <summary>
    /// The chunk id.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// The start block number within the file.
    /// </summary>
    /// <remarks>
    /// The reference declares this <c>volatile</c> and depends on it: the page reader reads it before
    /// reading a page and compares afterwards, so that a chunk moved by another thread in between is
    /// noticed and the read is retried. C# does not allow <c>volatile</c> on a 64 bit field, so the
    /// field is private and every access goes through <see cref="Volatile"/>. A plain public field
    /// would have looked like the reference while quietly dropping the only thing that made the retry
    /// loop terminate, and nothing at the point of use would have said so.
    /// </remarks>
    private long block;

    /// <summary>
    /// The start block number within the file, read with the ordering the retry loop depends on.
    /// </summary>
    public long Block => Volatile.Read(ref block);

    /// <summary>
    /// Sets the start block number, so that a reader in its retry loop sees the move.
    /// </summary>
    /// <param name="value">the new block</param>
    public void SetBlock(long value) => Volatile.Write(ref block, value);

    /// <summary>
    /// The length in number of blocks.
    /// </summary>
    public int Len { get; set; }

    /// <summary>
    /// The total number of pages in this chunk.
    /// </summary>
    internal int PageCount { get; set; }

    /// <summary>
    /// The number of pages that are still alive in the latest version of the store.
    /// </summary>
    internal int PageCountLive { get; set; }

    /// <summary>
    /// Byte offset (from the beginning of the chunk) for the table of contents. The table holds one
    /// long per page, carrying the map id, the page offset, the page length and the page type: the
    /// same layout as a page position, with the map id where the chunk id would be.
    /// </summary>
    internal int TocPos { get; set; }

    /// <summary>
    /// Collection of "deleted" flags for all pages in the chunk.
    /// </summary>
    internal LongBitSet? Occupancy { get; set; }

    /// <summary>
    /// The sum of the max length of all pages.
    /// </summary>
    public long MaxLen { get; set; }

    /// <summary>
    /// The sum of the length of all pages that are still alive.
    /// </summary>
    public long MaxLenLive { get; set; }

    /// <summary>
    /// The garbage collection priority. Priority 0 means it needs to be collected, a high value means
    /// low priority.
    /// </summary>
    internal int CollectPriority { get; set; }

    /// <summary>
    /// The position of the root of the layout map.
    /// </summary>
    internal long LayoutRootPos { get; set; }

    /// <summary>
    /// The version stored in this chunk.
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// When this chunk was created, in milliseconds since the store was created.
    /// </summary>
    public long Time { get; set; }

    /// <summary>
    /// When this chunk was no longer needed, in milliseconds after the store was created. After this
    /// the chunk is kept alive a bit longer, in case it is referenced in older versions.
    /// </summary>
    public long Unused { get; set; }

    /// <summary>
    /// Version of the store at which the chunk became unused and therefore can be considered "dead".
    /// </summary>
    internal long UnusedAtVersion { get; set; }

    /// <summary>
    /// The last used map id.
    /// </summary>
    public int MapId { get; set; }

    /// <summary>
    /// The predicted position of the next chunk.
    /// </summary>
    public long Next { get; set; }

    /// <summary>
    /// Number of live pinned pages.
    /// </summary>
    private int PinCount { get; set; }

    /// <summary>
    /// This chunk's serialized content, before it goes to the file store. Holding it here lets the
    /// pages be released earlier, so that they can be collected.
    /// </summary>
    public volatile byte[]? Buffer;

    /// <summary>
    /// A chunk described by the text of its header.
    /// </summary>
    /// <param name="s">the header text</param>
    internal Chunk(string s)
        : this(ToValues(DataUtils.ParseMap(s)), true)
    {
    }

    /// <summary>
    /// A chunk described by a parsed header.
    /// </summary>
    /// <param name="values">the header fields</param>
    /// <param name="full">
    /// whether the fields describe a chunk that has been written. A chunk being opened starts with
    /// only its identity known and is filled in as it is read.
    /// </param>
    /// <remarks>
    /// The fields arrive boxed because the reading helpers in <see cref="DataUtils"/> accept a map
    /// of mixed values. Boxing here keeps one set of helpers instead of two, and the values are
    /// strings either way.
    /// </remarks>
    internal Chunk(Dictionary<string, object?> values, bool full)
        : this(DataUtils.ReadHexInt(values, AttrChunk, -1))
    {
        Dictionary<string, object?> map = values;
        SetBlock(DataUtils.ReadHexLong(map, AttrBlock, 0));
        Len = DataUtils.ReadHexInt(map, AttrLen, 0);
        Version = DataUtils.ReadHexLong(map, AttrVersion, Id);
        if (full)
        {
            PageCount = DataUtils.ReadHexInt(map, AttrPages, 0);
            PageCountLive = DataUtils.ReadHexInt(map, AttrLivePages, PageCount);
            MapId = DataUtils.ReadHexInt(map, AttrMap, 0);
            MaxLen = DataUtils.ReadHexLong(map, AttrMax, 0);
            MaxLenLive = DataUtils.ReadHexLong(map, AttrLiveMax, MaxLen);
            LayoutRootPos = DataUtils.ReadHexLong(map, AttrRoot, 0);
            Time = DataUtils.ReadHexLong(map, AttrTime, 0);
            Unused = DataUtils.ReadHexLong(map, AttrUnused, 0);
            UnusedAtVersion = DataUtils.ReadHexLong(map, AttrUnusedAtVersion, 0);
            Next = DataUtils.ReadHexLong(map, AttrNext, 0);
            PinCount = DataUtils.ReadHexInt(map, AttrPinCount, 0);
            TocPos = DataUtils.ReadHexInt(map, AttrToc, 0);
            byte[]? bytes = DataUtils.ParseHexBytes(map, AttrOccupancy);
            if (bytes is null)
            {
                Occupancy = new LongBitSet();
            }
            else
            {
                LongBitSet occupancy = LongBitSet.ValueOf(bytes);
                if (PageCount - PageCountLive != occupancy.Cardinality())
                {
                    throw DataUtils.NewMvStoreException(
                        DataUtils.ErrorFileCorrupt,
                        "Inconsistent occupancy info {0} - {1} != {2} {3}",
                        PageCount, PageCountLive, occupancy.Cardinality(), this);
                }

                Occupancy = occupancy;
            }
        }
    }

    /// <summary>
    /// A new empty chunk with the given id.
    /// </summary>
    /// <param name="id">the chunk id</param>
    internal Chunk(int id)
    {
        Id = id;
        if (id < 0 || id > MaxId)
        {
            throw DataUtils.NewMvStoreException(DataUtils.ErrorFileCorrupt, "Invalid chunk id {0}", id);
        }
    }

    /// <summary>
    /// Read the header at the current position, and leave the position at the first byte after it.
    /// </summary>
    /// <param name="buff">the bytes</param>
    /// <param name="position">where to read, moved past the newline</param>
    /// <returns>the header text</returns>
    /// <exception cref="MvStoreException">there is no newline to end the header</exception>
    internal static string ReadChunkHeader(Span<byte> buff, ref int position)
    {
        int pos = position;
        int take = Math.Min(buff.Length - pos, MaxHeaderLength);
        ReadOnlySpan<byte> data = buff.Slice(pos, take);
        for (int i = 0; i < take; i++)
        {
            if (data[i] == (byte)'\n')
            {
                // set the position to the start of the first page
                position = pos + i + 1;
                return Encoding.Latin1.GetString(data[..i]).Trim();
            }
        }

        throw DataUtils.NewMvStoreException(DataUtils.ErrorFileCorrupt, "Not a valid chunk header");
    }

    /// <summary>
    /// How many bytes to reserve for this chunk's header.
    /// </summary>
    /// <returns>the size</returns>
    internal int EstimateHeaderSize()
    {
        byte[] headerBytes = GetHeaderBytes();
        int headerLength = headerBytes.Length;

        // Initial chunk will look like (length-wise) something in between those two lines:
        // chunk:0,len:0,pages:0,max:0,map:0,root:0,time:0,version:0
        // chunk:ffffffff,len:0,pages:0,max:0,map:0,root:0,time:ffffffffffffffff,version:ffffffffffffffff
        //
        // When the header is fully formed it will grow, and here are the fields that do not exist in
        // the initial header or may grow from their initial values:
        // len:0[fffffff],pages:0[fffffff][,pinCount:ffffffff],max:0[fffffffffffffff],map:0[fffffff],
        // root:0[fffffffffffffff,next:ffffffffffffffff,toc:fffffffff]
        return headerLength + 104 + 1; // extra one for the terminator
    }

    /// <summary>
    /// Write the header, padded with spaces to the requested length and terminated by a newline.
    /// </summary>
    /// <param name="buff">where to write</param>
    /// <param name="maxLength">
    /// how many bytes the header may take, zero for "as long as it needs"
    /// </param>
    internal void WriteChunkHeader(WriteBuffer buff, int maxLength)
    {
        int terminatorPosition = buff.Position() + maxLength - 1;
        byte[] headerBytes = GetHeaderBytes();
        _ = buff.Put(headerBytes);
        while (buff.Position() < terminatorPosition)
        {
            _ = buff.Put((byte)' ');
        }

        if (maxLength != 0 && buff.Position() > terminatorPosition)
        {
            throw DataUtils.NewMvStoreException(
                DataUtils.ErrorInternal,
                "Chunk metadata too long {0} {1} {2}",
                terminatorPosition, buff.Position(), GetHeader());
        }

        _ = buff.Put((byte)'\n');
    }

    /// <summary>
    /// The metadata key a chunk's header lives under.
    /// </summary>
    /// <param name="chunkId">the chunk</param>
    /// <returns>the key</returns>
    internal static string GetMetaKey(int chunkId)
        => AttrChunk + "." + chunkId.ToString("x", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// How full the chunk is, in percent.
    /// </summary>
    /// <returns>0 when empty, 100 when every page is at its maximum</returns>
    internal int GetFillRate()
    {
        System.Diagnostics.Debug.Assert(MaxLenLive <= MaxLen, MaxLenLive + " > " + MaxLen);
        if (MaxLenLive <= 0)
        {
            return 0;
        }

        if (MaxLenLive == MaxLen)
        {
            return 100;
        }

        return 1 + (int)(98 * MaxLenLive / MaxLen);
    }

    /// <inheritdoc/>
    public override int GetHashCode() => Id;

    /// <inheritdoc/>
    public override bool Equals(object? o) => o is Chunk chunk && chunk.Id == Id;

    /// <summary>
    /// Every field of this chunk, as text.
    /// </summary>
    /// <returns>the text</returns>
    public string AsString()
    {
        StringBuilder buff = new(240);
        Dump(buff);
        return buff.ToString();
    }

    /// <summary>
    /// Append every field of this chunk.
    /// </summary>
    /// <param name="buff">where to append</param>
    protected virtual void Dump(StringBuilder buff)
    {
        DataUtils.AppendMap(buff, AttrChunk, Id);
        DataUtils.AppendMap(buff, AttrBlock, Block);
        DataUtils.AppendMap(buff, AttrLen, Len);
        DataUtils.AppendMap(buff, AttrPages, PageCount);
        if (PageCount != PageCountLive)
        {
            DataUtils.AppendMap(buff, AttrLivePages, PageCountLive);
        }

        DataUtils.AppendMap(buff, AttrMax, MaxLen);
        if (MaxLen != MaxLenLive)
        {
            DataUtils.AppendMap(buff, AttrLiveMax, MaxLenLive);
        }

        DataUtils.AppendMap(buff, AttrMap, MapId);
        if (Next != 0)
        {
            DataUtils.AppendMap(buff, AttrNext, Next);
        }

        DataUtils.AppendMap(buff, AttrRoot, LayoutRootPos);
        DataUtils.AppendMap(buff, AttrTime, Time);
        if (Unused != 0)
        {
            DataUtils.AppendMap(buff, AttrUnused, Unused);
        }

        if (UnusedAtVersion != 0)
        {
            DataUtils.AppendMap(buff, AttrUnusedAtVersion, UnusedAtVersion);
        }

        DataUtils.AppendMap(buff, AttrVersion, Version);
        if (PinCount > 0)
        {
            DataUtils.AppendMap(buff, AttrPinCount, PinCount);
        }

        if (TocPos > 0)
        {
            DataUtils.AppendMap(buff, AttrToc, TocPos);
        }

        if (Occupancy is not null && Occupancy.Length > 0)
        {
            DataUtils.AppendMap(buff, AttrOccupancy, Hex.BytesToHex(Occupancy.ToByteArray()));
        }
    }

    /// <summary>
    /// The header as text.
    /// </summary>
    /// <returns>the header</returns>
    public string GetHeader() => Encoding.Latin1.GetString(GetHeaderBytes());

    /// <summary>
    /// The header as bytes, in Latin-1.
    /// </summary>
    /// <returns>the bytes</returns>
    private byte[] GetHeaderBytes()
    {
        StringBuilder buff = new(240);
        DataUtils.AppendMap(buff, AttrChunk, Id);
        DataUtils.AppendMap(buff, AttrLen, Len);
        DataUtils.AppendMap(buff, AttrPages, PageCount);
        if (PinCount > 0)
        {
            DataUtils.AppendMap(buff, AttrPinCount, PinCount);
        }

        DataUtils.AppendMap(buff, AttrMax, MaxLen);
        DataUtils.AppendMap(buff, AttrMap, MapId);
        DataUtils.AppendMap(buff, AttrRoot, LayoutRootPos);
        DataUtils.AppendMap(buff, AttrTime, Time);
        DataUtils.AppendMap(buff, AttrVersion, Version);
        if (Next != 0)
        {
            DataUtils.AppendMap(buff, AttrNext, Next);
        }

        if (TocPos > 0)
        {
            DataUtils.AppendMap(buff, AttrToc, TocPos);
        }

        return Encoding.Latin1.GetBytes(buff.ToString());
    }

    /// <summary>
    /// The footer as bytes: the same three fields as the header, plus a checksum, padded to a fixed
    /// length.
    /// </summary>
    /// <returns>the bytes</returns>
    internal byte[] GetFooterBytes()
    {
        StringBuilder buff = new(FooterLength);
        DataUtils.AppendMap(buff, AttrChunk, Id);
        DataUtils.AppendMap(buff, AttrLen, Len);
        DataUtils.AppendMap(buff, AttrVersion, Version);
        byte[] bytes = Encoding.Latin1.GetBytes(buff.ToString());
        int checksum = DataUtils.GetFletcher32(bytes, 0, bytes.Length);
        DataUtils.AppendMap(buff, AttrFletcher, checksum);
        while (buff.Length < FooterLength - 1)
        {
            _ = buff.Append(' ');
        }

        _ = buff.Append('\n');
        return Encoding.Latin1.GetBytes(buff.ToString());
    }

    /// <summary>
    /// Whether the chunk has a place in the file.
    /// </summary>
    /// <returns>true when allocated</returns>
    internal bool IsAllocated() => Block != 0;

    /// <summary>
    /// Whether the chunk has been written and its buffer released.
    /// </summary>
    /// <remarks>
    /// The buffer may be gone after the chunk was saved or before serialization finished, and to
    /// tell those apart we ask whether space was allocated, which only happens after serialization.
    /// </remarks>
    /// <returns>true when saved</returns>
    internal bool IsSaved() => IsAllocated() && Buffer is null;

    /// <summary>
    /// Whether any page is still alive.
    /// </summary>
    /// <returns>true when live</returns>
    internal bool IsLive() => PageCountLive > 0;

    /// <summary>
    /// Whether the chunk can have more pages written into it.
    /// </summary>
    /// <returns>true when rewritable</returns>
    internal bool IsRewritable()
        => IsSaved() && IsLive() && PageCountLive < PageCount && IsEvacuatable();

    private bool IsEvacuatable() => PinCount == 0;

    /// <summary>
    /// Account for a page that was just written.
    /// </summary>
    /// <param name="pageLengthOnDisk">how many bytes it took</param>
    /// <param name="singleWriter">
    /// whether this is the only writer, in which case the page is pinned so that nothing else reuses
    /// the space before the write is finished
    /// </param>
    internal void AccountForWrittenPage(int pageLengthOnDisk, bool singleWriter)
    {
        MaxLen += pageLengthOnDisk;
        PageCount++;
        MaxLenLive += pageLengthOnDisk;
        PageCountLive++;
        if (singleWriter)
        {
            PinCount++;
        }

        System.Diagnostics.Debug.Assert(
            PageCount - PageCountLive == Occupancy!.Cardinality(),
            PageCount + " - " + PageCountLive + " <> " + Occupancy.Cardinality());
    }

    /// <summary>
    /// Account for a page that was removed, and say whether the chunk became dead.
    /// </summary>
    /// <param name="pageNo">which page</param>
    /// <param name="pageLength">how many bytes it took</param>
    /// <param name="pinned">whether the page was pinned</param>
    /// <param name="now">the current time</param>
    /// <param name="version">the current version</param>
    /// <returns>true when nothing is alive any more</returns>
    internal bool AccountForRemovedPage(int pageNo, int pageLength, bool pinned, long now, long version)
    {
        // legacy chunks do not have a table of contents, therefore pageNo is not valid, skip
        if (TocPos > 0)
        {
            System.Diagnostics.Debug.Assert(
                pageNo >= 0 && pageNo < PageCount, pageNo + " // " + PageCount);
            System.Diagnostics.Debug.Assert(!Occupancy!.Get(pageNo), pageNo + " " + this + " " + Occupancy);
            Occupancy.SetRange(pageNo, pageNo + 1);
        }

        MaxLenLive -= pageLength;
        PageCountLive--;
        if (pinned)
        {
            PinCount--;
        }

        if (UnusedAtVersion < version)
        {
            UnusedAtVersion = version;
        }

        System.Diagnostics.Debug.Assert(PinCount >= 0, ToString());
        System.Diagnostics.Debug.Assert(PageCountLive >= 0, ToString());
        System.Diagnostics.Debug.Assert(PinCount <= PageCountLive, ToString());
        System.Diagnostics.Debug.Assert(MaxLenLive >= 0, ToString());
        System.Diagnostics.Debug.Assert((PageCountLive == 0) == (MaxLenLive == 0), ToString());
        if (!IsLive())
        {
            Unused = now;
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override string ToString() => AsString() + (Buffer is null ? "" : ", buf");

    /// <summary>
    /// Reads a page of data, retrying when the chunk moves under the read.
    /// </summary>
    /// <remarks>
    /// The block is read before the page and compared afterwards: a chunk moved by another
    /// thread in between is noticed and the read repeats. When the chunk was just serialized
    /// and not saved yet, the page comes from its in-memory image instead of the file.
    /// The image is handed out as a view, not a copy: the reader deserializes it within its
    /// own call, while this buffer is still the chunk's.
    /// </remarks>
    internal RentedBuffer ReadBufferForPage(FileStore store, int offset, long pos)
    {
        Debug.Assert(IsAllocated() || Buffer != null);
        while (true)
        {
            long originalBlock = Block;
            try
            {
                long filePos = originalBlock * FileStore.BlockSize;
                long maxPos = filePos + (long)Len * FileStore.BlockSize;
                filePos += offset;
                if (filePos < 0)
                {
                    throw DataUtils.NewMvStoreException(
                        DataUtils.ErrorFileCorrupt,
                        "Negative position {0}; p={1}, c={2}", filePos, pos, this);
                }

                int length = DataUtils.GetPageMaxLength(pos);
                if (length == DataUtils.PageLarge)
                {
                    // read the first bytes to figure out actual length
                    using RentedBuffer probe = ReadFully(store, filePos, 128);
                    length = BinaryPrimitives.ReadInt32BigEndian(probe.Span[..4]);
                    // pageNo is deliberately not included into length to preserve compatibility
                    length += 4;
                }

                length = (int)Math.Min(maxPos - filePos, length);
                if (length < 0)
                {
                    throw DataUtils.NewMvStoreException(DataUtils.ErrorFileCorrupt,
                        "Illegal page length {0} reading at {1}; max pos {2} ", length, filePos, maxPos);
                }

                byte[]? buffer = Buffer;
                RentedBuffer buff = buffer == null
                    ? ReadFully(store, filePos, length)
                    : RentedBuffer.View(buffer.AsMemory(offset, length));

                if (originalBlock == Block)
                {
                    return buff;
                }
            }
            catch (MvStoreException)
            {
                if (originalBlock == Block)
                {
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// Reads the table of contents, retrying when the chunk moves under the read.
    /// </summary>
    internal long[] ReadToC(FileStore store)
    {
        Debug.Assert(IsAllocated() || Buffer != null);
        Debug.Assert(TocPos > 0);
        long[] toc = new long[PageCount];
        while (true)
        {
            long originalBlock = Block;
            try
            {
                byte[]? buffer = Buffer;
                if (buffer == null)
                {
                    int length = PageCount * 8;
                    long filePos = originalBlock * FileStore.BlockSize + TocPos;
                    using RentedBuffer rented = ReadFully(store, filePos, length);
                    for (int i = 0; i < toc.Length; i++)
                    {
                        toc[i] = BinaryPrimitives.ReadInt64BigEndian(rented.Span.Slice(i * 8, 8));
                    }
                }
                else
                {
                    for (int i = 0; i < toc.Length; i++)
                    {
                        toc[i] = BinaryPrimitives.ReadInt64BigEndian(buffer.AsSpan(TocPos + i * 8, 8));
                    }
                }

                if (originalBlock == Block)
                {
                    return toc;
                }
            }
            catch (MvStoreException)
            {
                if (originalBlock == Block)
                {
                    throw;
                }
            }
        }
    }

    private RentedBuffer ReadFully(FileStore store, long filePos, int length)
        => store.ReadFully(this, filePos, length);

    /// <summary>
    /// Orders chunks by where they sit in the file.
    /// </summary>
    /// <remarks>
    /// Ordering by anything else would put a chunk that is about to be written next to one that is
    /// being read, which is the opposite of what the store wants.
    /// </remarks>
    internal sealed class PositionComparator : IComparer<Chunk>
    {
        /// <summary>
        /// The one instance, as in the reference.
        /// </summary>
        internal static readonly PositionComparator Instance = new();

        private PositionComparator()
        {
        }

        /// <inheritdoc/>
        public int Compare(Chunk? x, Chunk? y)
        {
            if (x is null)
            {
                return y is null ? 0 : 1;
            }

            return y is null ? -1 : x.Block.CompareTo(y.Block);
        }
    }

    internal static Dictionary<string, object?> ToValues(IDictionary<string, string> map)
    {
        Dictionary<string, object?> values = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in map)
        {
            values[entry.Key] = entry.Value;
        }

        return values;
    }
}
