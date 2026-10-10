using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using H2Sharp.Compression;
using H2Sharp.Engine;
using H2Sharp.MvStore.Type;
using H2Sharp.Store.FileSystem;

namespace H2Sharp.MvStore;

/// <summary>
/// File-level tools: dump, info, compact, rollback, repair. Only what the store itself needs
/// lived here first (the generic map builder behind compaction and the atomic replace behind
/// it); the rest of the reference's tool is now ported too, except its command-line main.
/// </summary>
public static class MVStoreTool
{
    /// <summary>
    /// How many file headers a dump prints per file: the header is stored twice.
    /// </summary>
    public const int MaxNbFileHeadersPerFile = 2;
    /// <summary>
    /// A builder that reads anything persisted as raw bytes, for copying maps blind.
    /// </summary>
    internal static MVMap<object?, object?>.Builder<object?, object?> GetGenericMapBuilder()
    {
        var builder = new MVMap<object?, object?>.Builder<object?, object?>();
        builder.KeyType(GenericDataType.Instance);
        builder.ValueType(GenericDataType.Instance);
        return builder;
    }

    /// <summary>
    /// Moves a file over another, atomically where the file system allows it.
    /// </summary>
    /// <param name="sourceName">the old fully qualified file name</param>
    /// <param name="destinationName">the new fully qualified file name</param>
    public static void MoveAtomicReplace(string sourceName, string destinationName)
        => FilePath.Get(sourceName).MoveTo(FilePath.Get(destinationName), true);

    /// <summary>
    /// Reads the contents of the file and writes them to the console.
    /// </summary>
    public static void Dump(string fileName, bool details) => Dump(fileName, Console.Out, details);

    /// <summary>
    /// Reads the contents of the file and writes them human-readably.
    /// </summary>
    /// <param name="fileName">the file name</param>
    /// <param name="writer">where to write</param>
    /// <param name="details">whether to print page details</param>
    public static void Dump(string fileName, TextWriter writer, bool details)
    {
        if (!FilePath.Get(fileName).Exists())
        {
            writer.WriteLine("File not found: " + fileName);
            return;
        }

        long size = FilePath.Get(fileName).Size();
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "File {0}, {1} bytes, {2} MB", fileName, size, size / 1024 / 1024));
        int blockSize = FileStore.BlockSize;
        var mapSizesTotal = new SortedDictionary<int, long>();
        long pageSizeTotal = 0;
        try
        {
            using FileChannel file = FilePath.Get(fileName).Open("r");
            long fileSize = file.Length;
            int len = fileSize.ToString("x", CultureInfo.InvariantCulture).Length;
            byte[] buffer = new byte[4096];
            long pageCount = 0;
            int readFileHeaderCount = 0;
            for (long pos = 0; pos < fileSize;)
            {
                // A broken tail block (partial write on power loss) is skipped, not fatal:
                // ours signals end-of-stream where the reference wraps it, so both are caught.
                try
                {
                    DataUtils.ReadFully(file, pos, buffer);
                }
                catch (Exception e) when (e is EndOfStreamException
                    or MvStoreException
                    or IOException)
                {
                    pos += blockSize;
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "ERROR illegal position {0}", pos));
                    continue;
                }

                int headerType = buffer[0];
                if (headerType == 'H' && readFileHeaderCount < MaxNbFileHeadersPerFile)
                {
                    string header = Encoding.Latin1.GetString(buffer).Trim();
                    writer.WriteLine(Hex(pos, len) + " fileHeader " + header);
                    pos += blockSize;
                    readFileHeaderCount++;
                    continue;
                }

                if (headerType != 'c')
                {
                    pos += blockSize;
                    continue;
                }

                Chunk c;
                try
                {
                    int headerPos = 0;
                    c = new SFChunk(Chunk.ReadChunkHeader(buffer, ref headerPos));
                    c.SetBlock(pos / blockSize);
                }
                catch (Exception)
                {
                    // Chunks are not always contiguous (due to chunk compaction/move/drop and space re-use)
                    // Blocks following a chunk can therefore contain something else than a valid chunk header
                    // In that case, let's move to the next block
                    pos += blockSize;
                    continue;
                }

                if (c.Len <= 0)
                {
                    // not a chunk
                    pos += blockSize;
                    continue;
                }

                int length = c.Len * FileStore.BlockSize;
                writer.WriteLine();
                writer.WriteLine(Hex(pos, len) + " chunkHeader " + c);
                byte[] chunk = new byte[length];
                DataUtils.ReadFully(file, pos, chunk);
                int headerEnd = 0;
                _ = Chunk.ReadChunkHeader(chunk, ref headerEnd);
                int p = headerEnd;
                pos += length;
                int remaining = c.PageCount;
                pageCount += c.PageCount;
                var mapSizes = new SortedDictionary<int, int>();
                int pageSizeSum = 0;
                while (remaining > 0)
                {
                    if (p < 0 || p + 4 > chunk.Length)
                    {
                        // too far
                        writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "ERROR illegal position {0}", p));
                        break;
                    }

                    int start = p;
                    int pageSize = BinaryPrimitives.ReadInt32BigEndian(chunk.AsSpan(p, 4));
                    p += 4;
                    // check value (ignored)
                    p += 2;
                    /*int pageNo =*/
                    _ = DataUtils.ReadVarInt(chunk, ref p);
                    int mapId = DataUtils.ReadVarInt(chunk, ref p);
                    int entries = DataUtils.ReadVarInt(chunk, ref p);
                    int type = chunk[p++];
                    bool compressed = (type & DataUtils.PageCompressed) != 0;
                    bool node = (type & DataUtils.PageTypeNode) != 0;
                    if (details)
                    {
                        writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "+{0} {1}, map {2}, {3} entries, {4} bytes, maxLen {5}",
                                Hex(start, len),
                                (node ? "node" : "leaf") + (compressed ? " compressed" : ""),
                                mapId.ToString("x", CultureInfo.InvariantCulture),
                                node ? entries + 1 : entries,
                                pageSize,
                                DataUtils.GetPageMaxLength(DataUtils.ComposePagePos(0, 0, pageSize, 0))
                                    .ToString("x", CultureInfo.InvariantCulture)));
                    }

                    // q is the buffer cursor: it walks the page sequentially while p already
                    // points at the next page, exactly like the reference's buffer position.
                    int q = p;
                    p = start + pageSize;
                    mapSizes.TryGetValue(mapId, out int mapSize);
                    mapSizes[mapId] = mapSize + pageSize;
                    mapSizesTotal.TryGetValue(mapId, out long total);
                    mapSizesTotal[mapId] = total + pageSize;
                    pageSizeSum += pageSize;
                    pageSizeTotal += pageSize;
                    remaining--;
                    long[]? children = null;
                    long[]? counts = null;
                    if (node)
                    {
                        children = new long[entries + 1];
                        for (int i = 0; i <= entries; i++)
                        {
                            children[i] = BinaryPrimitives.ReadInt64BigEndian(chunk.AsSpan(q, 8));
                            q += 8;
                        }

                        counts = new long[entries + 1];
                        for (int i = 0; i <= entries; i++)
                        {
                            long s = DataUtils.ReadVarLong(chunk, ref q);
                            counts[i] = s;
                        }
                    }

                    string[] keys = new string[entries];
                    if (mapId == 0 && details)
                    {
                        byte[] data;
                        int dataPos;
                        if (compressed)
                        {
                            bool fast = (type & DataUtils.PageCompressedHigh) != DataUtils.PageCompressedHigh;
                            Compressor compressor = GetCompressor(fast);
                            int lenAdd = DataUtils.ReadVarInt(chunk, ref q);
                            int compLen = pageSize + start - q;
                            byte[] comp = new byte[compLen];
                            Array.Copy(chunk, q, comp, 0, compLen);
                            q += compLen;
                            int l = compLen + lenAdd;
                            data = new byte[l];
                            compressor.Expand(comp, 0, compLen, data, 0, l);
                            dataPos = 0;
                        }
                        else
                        {
                            data = chunk;
                            dataPos = q;
                        }

                        for (int i = 0; i < entries; i++)
                        {
                            string k = StringDataType.Instance.Read(data, ref dataPos);
                            keys[i] = k;
                        }

                        if (node)
                        {
                            // meta map node
                            for (int i = 0; i < entries; i++)
                            {
                                long cp = children![i];
                                writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                        "    {0} children < {1} @ chunk {2} +{3}",
                                        counts![i],
                                        keys[i],
                                        DataUtils.GetPageChunkId(cp).ToString("x", CultureInfo.InvariantCulture),
                                        Hex(DataUtils.GetPageOffset(cp), len)));
                            }

                            long lastCp = children![entries];
                            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                    "    {0} children >= {1} @ chunk {2} +{3}",
                                    counts![entries],
                                    "null",
                                    DataUtils.GetPageChunkId(lastCp).ToString("x", CultureInfo.InvariantCulture),
                                    Hex(DataUtils.GetPageOffset(lastCp), len)));
                        }
                        else
                        {
                            // meta map leaf
                            string[] values = new string[entries];
                            for (int i = 0; i < entries; i++)
                            {
                                string v = StringDataType.Instance.Read(data, ref dataPos);
                                values[i] = v;
                            }

                            for (int i = 0; i < entries; i++)
                            {
                                writer.WriteLine("    " + keys[i] + " = " + values[i]);
                            }
                        }
                    }
                    else
                    {
                        if (node && details)
                        {
                            for (int i = 0; i <= entries; i++)
                            {
                                long cp = children![i];
                                writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                        "    {0} children @ chunk {1} +{2}",
                                        counts![i],
                                        DataUtils.GetPageChunkId(cp).ToString("x", CultureInfo.InvariantCulture),
                                        Hex(DataUtils.GetPageOffset(cp), len)));
                            }
                        }
                    }
                }

                pageSizeSum = Math.Max(1, pageSizeSum);
                foreach (KeyValuePair<int, int> mapSize in mapSizes)
                {
                    int percent = 100 * mapSize.Value / pageSizeSum;
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "map {0}: {1} bytes, {2}%", mapSize.Key.ToString("x", CultureInfo.InvariantCulture),
                        mapSize.Value, percent));
                }

                int footerPos = chunk.Length - Chunk.FooterLength;
                if (footerPos < 0 || footerPos > chunk.Length - Chunk.FooterLength)
                {
                    // too far
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "ERROR illegal footer position {0}", footerPos));
                }
                else
                {
                    writer.WriteLine("+" + Hex(footerPos, len) + " chunkFooter "
                        + Encoding.Latin1.GetString(chunk, footerPos, Chunk.FooterLength).Trim());
                }
            }

            writer.WriteLine();
            writer.WriteLine(Hex(fileSize, len) + " eof");
            writer.WriteLine();
            pageCount = Math.Max(1, pageCount);
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "page size total: {0} bytes, page count: {1}, average page size: {2} bytes",
                    pageSizeTotal, pageCount, pageSizeTotal / pageCount));
            pageSizeTotal = Math.Max(1, pageSizeTotal);
            foreach (KeyValuePair<int, long> total in mapSizesTotal)
            {
                int percent = (int)(100 * total.Value / pageSizeTotal);
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "map {0}: {1} bytes, {2}%", total.Key.ToString("x", CultureInfo.InvariantCulture),
                    total.Value, percent));
            }
        }
        catch (IOException e)
        {
            writer.WriteLine("ERROR: " + e);
        }

        // ignore
        writer.Flush();
    }

    private static Compressor GetCompressor(bool fast)
        => fast ? (Compressor)new CompressLZF() : new CompressDeflate();

    private static string Hex(long value, int len)
        => value.ToString("x", CultureInfo.InvariantCulture).PadLeft(len, '0');

    /// <summary>
    /// Reads the summary information of the file and writes it to the console.
    /// </summary>
    /// <returns>null when successful, otherwise the error message</returns>
    public static string? Info(string fileName) => Info(fileName, Console.Out);

    /// <summary>
    /// Reads the summary information of the file.
    /// </summary>
    /// <returns>null when successful, otherwise the error message</returns>
    public static string? Info(string fileName, TextWriter writer)
    {
        if (!FilePath.Get(fileName).Exists())
        {
            writer.WriteLine("File not found: " + fileName);
            return "File not found: " + fileName;
        }

        long fileLength = FilePath.Get(fileName).Size();
        try
        {
            using MVStore store = new MVStore.Builder().
                    FileName(fileName).RecoveryMode().
                    ReadOnly().Open();
            IDictionary<string, string> layout = store.GetLayoutMap()!;
            IDictionary<string, object?> header = store.GetStoreHeader();
            long fileCreated = DataUtils.ReadHexLong(header, "created", 0L);
            var chunks = new SortedDictionary<int, Chunk>();
            long chunkLength = 0;
            long maxLength = 0;
            long maxLengthLive = 0;
            long maxLengthNotEmpty = 0;
            foreach (KeyValuePair<string, string> e in layout)
            {
                string k = e.Key;
                if (k.StartsWith(DataUtils.LayoutChunk, StringComparison.Ordinal))
                {
                    Chunk c = store.GetFileStore()!.CreateChunk(e.Value);
                    chunks[c.Id] = c;
                    chunkLength += (long)c.Len * FileStore.BlockSize;
                    maxLength += c.MaxLen;
                    maxLengthLive += c.MaxLenLive;
                    if (c.MaxLenLive > 0)
                    {
                        maxLengthNotEmpty += c.MaxLen;
                    }
                }
            }

            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "Created: {0}", FormatTimestamp(fileCreated, fileCreated)));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "Last modified: {0}",
                FormatTimestamp(FilePath.Get(fileName).LastModified(), fileCreated)));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "File length: {0}", fileLength));
            writer.WriteLine("The last chunk is not listed");
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "Chunk length: {0}", chunkLength));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "Chunk count: {0}", chunks.Count));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "Used space: {0}%", GetPercent(chunkLength, fileLength)));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "Chunk fill rate: {0}%", maxLength == 0 ? 100 : GetPercent(maxLengthLive, maxLength)));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "Chunk fill rate excluding empty chunks: {0}%",
                maxLengthNotEmpty == 0 ? 100 : GetPercent(maxLengthLive, maxLengthNotEmpty)));
            foreach (KeyValuePair<int, Chunk> e in chunks)
            {
                Chunk c = e.Value;
                long created = fileCreated + c.Time;
                writer.Write(string.Format(CultureInfo.InvariantCulture,
                        "  Chunk {0}: {1}, {2}% used, {3} blocks",
                        c.Id, FormatTimestamp(created, fileCreated),
                        GetPercent(c.MaxLenLive, c.MaxLen),
                        c.Len));
                if (c.MaxLenLive == 0)
                {
                    writer.Write(string.Format(CultureInfo.InvariantCulture, ", unused: {0}",
                            FormatTimestamp(fileCreated + c.Unused, fileCreated)));
                }

                writer.WriteLine();
            }

            writer.WriteLine();
        }
        catch (Exception e)
        {
            writer.WriteLine("ERROR: " + e);
            return e.Message;
        }

        writer.Flush();
        return null;
    }

    private static string FormatTimestamp(long t, long start)
    {
        string x = DateTimeOffset.FromUnixTimeMilliseconds(t).UtcDateTime
            .ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        string s = x.Substring(0, 19);
        s += " (+" + ((t - start) / 1000) + " s)";
        return s;
    }

    private static int GetPercent(long value, long max)
    {
        if (value == 0)
        {
            return 0;
        }
        else if (value == max)
        {
            return 100;
        }

        return (int)(1 + 98 * value / Math.Max(1, max));
    }

    /// <summary>
    /// Compacts the store into itself.
    /// </summary>
    public static void Compact(string fileName, bool compress)
        => MVStore.Compact(fileName, compress, null);

    /// <summary>
    /// Deletes leftover temporary files from an interrupted compact.
    /// </summary>
    public static void CompactCleanUp(string fileName)
    {
        string tempName = fileName + Constants.SuffixMvStoreTempFile;
        if (FilePath.Get(tempName).Exists())
        {
            FilePath.Get(tempName).Delete();
        }

        string newName = fileName + Constants.SuffixMvStoreNewFile;
        if (FilePath.Get(newName).Exists())
        {
            if (FilePath.Get(fileName).Exists())
            {
                FilePath.Get(newName).Delete();
            }
            else
            {
                FilePath.Get(newName).MoveTo(FilePath.Get(fileName), false);
            }
        }
    }

    /// <summary>
    /// Repairs a store by rolling back to the newest good version.
    /// </summary>
    public static void Repair(string fileName)
    {
        TextWriter pw = Console.Out;
        long version = long.MaxValue;
        var ignore = TextWriter.Null;
        while (version >= 0)
        {
            pw.WriteLine(version == long.MaxValue ? "Trying latest version"
                    : ("Trying version " + version));
            pw.Flush();
            version = Rollback(fileName, version, ignore);
            try
            {
                string? error = Info(fileName + ".temp", ignore);
                if (error is null)
                {
                    FilePath.Get(fileName).MoveTo(FilePath.Get(fileName + ".back"), true);
                    FilePath.Get(fileName + ".temp").MoveTo(FilePath.Get(fileName), true);
                    pw.WriteLine("Success");
                    break;
                }

                pw.WriteLine("    ... failed: " + error);
            }
            catch (Exception e)
            {
                pw.WriteLine("Fail: " + e.Message);
                pw.Flush();
            }

            version--;
        }

        pw.Flush();
    }

    /// <summary>
    /// Rolls back to a version into a file called *.temp.
    /// </summary>
    /// <returns>the version rolled back to (-1 if no version)</returns>
    public static long Rollback(string fileName, long targetVersion, TextWriter writer)
    {
        long newestVersion = -1;
        if (!FilePath.Get(fileName).Exists())
        {
            writer.WriteLine("File not found: " + fileName);
            return newestVersion;
        }

        FileChannel? file = null;
        FileChannel? target = null;
        int blockSize = FileStore.BlockSize;
        try
        {
            file = FilePath.Get(fileName).Open("r");
            FilePath.Get(fileName + ".temp").Delete();
            target = FilePath.Get(fileName + ".temp").Open("rw");
            long fileSize = file.Length;
            byte[] buffer = new byte[4096];
            SFChunk? newestChunk = null;
            for (long pos = 0; pos < fileSize;)
            {
                DataUtils.ReadFully(file, pos, buffer);
                int headerType = buffer[0];
                if (headerType == 'H')
                {
                    DataUtils.WriteFully(target, pos, buffer);
                    pos += blockSize;
                    continue;
                }

                if (headerType != 'c')
                {
                    pos += blockSize;
                    continue;
                }

                SFChunk c;
                try
                {
                    int headerPos = 0;
                    c = new SFChunk(Chunk.ReadChunkHeader(buffer, ref headerPos));
                }
                catch (MvStoreException)
                {
                    pos += blockSize;
                    continue;
                }

                if (c.Len <= 0)
                {
                    // not a chunk
                    pos += blockSize;
                    continue;
                }

                int length = c.Len * FileStore.BlockSize;
                byte[] chunk = new byte[length];
                DataUtils.ReadFully(file, pos, chunk);
                if (c.Version > targetVersion)
                {
                    // newer than the requested version
                    pos += length;
                    continue;
                }

                DataUtils.WriteFully(target, pos, chunk);
                if (newestChunk is null || c.Version > newestChunk.Version)
                {
                    newestChunk = c;
                    newestVersion = c.Version;
                }

                pos += length;
            }

            int newestLength = newestChunk!.Len * FileStore.BlockSize;
            byte[] newestBytes = new byte[newestLength];
            DataUtils.ReadFully(file, newestChunk.Block * FileStore.BlockSize, newestBytes);
            DataUtils.WriteFully(target, fileSize, newestBytes);
        }
        catch (IOException e)
        {
            writer.WriteLine("ERROR: " + e);
        }
        finally
        {
            if (file is not null)
            {
                try
                {
                    file.Dispose();
                }
                catch (IOException)
                {
                    // ignore
                }
            }

            if (target is not null)
            {
                try
                {
                    target.Dispose();
                }
                catch (IOException)
                {
                    // ignore
                }
            }
        }

        writer.Flush();
        return newestVersion;
    }

    /// <summary>
    /// A data type that reads anything persisted as raw bytes, without understanding it.
    /// </summary>
    private sealed class GenericDataType : BasicDataType<object?>
    {
        internal static readonly GenericDataType Instance = new();

        private GenericDataType()
        {
        }

        public override bool IsMemoryEstimationAllowed() => false;

        public override int GetMemory(object? obj) => obj is byte[] bytes ? bytes.Length * 8 : 0;

        public override object?[] CreateStorage(int size) => new object?[size];

        public override void Write(WriteBuffer buff, object? obj)
        {
            if (obj is byte[] bytes)
            {
                buff.Put(bytes);
            }
        }

        public override object? Read(Span<byte> buff, ref int position)
        {
            int len = buff.Length - position;
            if (len == 0)
            {
                return null;
            }

            byte[] data = new byte[len];
            buff.Slice(position, len).CopyTo(data);
            position += len;
            return data;
        }
    }
}
