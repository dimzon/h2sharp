using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.MvStore;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The chunk header and footer against the reference.
/// </summary>
/// <remarks>
/// The header and the footer are the only part of a chunk a reader can find without knowing anything
/// else about it: the header ends at the first newline, the footer is a fixed number of bytes from
/// the end, and the spaces in between are what make that true. So both are compared byte for byte,
/// padding included.
/// <para>
/// The footer carries a checksum of the text that came before it, and that is checked here rather
/// than left to the day something is corrupt: a footer that reads as plausible but does not match is
/// exactly the failure this format exists to prevent.
/// </para>
/// <para>
/// A chunk describes itself twice, in the header and in the longer dump behind
/// <c>asString</c>, and the two are not the same text. Both are compared, because a port that made
/// them agree would stop being able to read files this port did not write.
/// </para>
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class ChunkOracleTests
{
    [Fact]
    public void ChunkHeaderAgreesWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Program(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"],
                packageName: "org.h2.mvstore")
            .ToList();

        List<string> csharp = [.. Lines()];

        Compare(java, csharp);
    }

    /// <summary>The reference program, in its own package so that it can build a chunk.</summary>
    private static string Program() =>
        """
        {
        class R {
        static final class T extends org.h2.mvstore.Chunk<T> {
          T(int id) {
            super(id);
          }

          T(String s) {
            super(s);
          }

          @Override
          protected java.nio.ByteBuffer readFully(org.h2.mvstore.FileStore<T> fileStore, long pos, int length) {
            throw new UnsupportedOperationException("not used");
          }
        }

        static T newChunk(int id) {
          return new T(id);
        }

        static T fromHeader(String s) {
          return new T(s);
        }

        static String bytes(byte[] b) {
          return org.h2.util.StringUtils.convertBytesToHex(b);
        }

        static String len(byte[] b) {
          return String.valueOf(b.length);
        }

        static String header(T c) {
          return c.getHeader() + "|" + R.bytes(c.getHeader().getBytes(
              java.nio.charset.StandardCharsets.ISO_8859_1));
        }

        static String footer(T c) {
          return R.bytes(c.getFooterBytes());
        }
        }

        R.T c = R.newChunk(7);
        System.out.println("empty|" + R.header(c) + "|" + R.footer(c) + "|" + c.estimateHeaderSize()
            + "|" + c.getFillRate() + "|" + c.isAllocated() + "|" + c.isSaved() + "|" + c.isLive()
            + "|" + c.isRewritable());
        System.out.println("asstr|" + c.asString());
        System.out.println("tostr|" + c);
        System.out.println("meta|" + org.h2.mvstore.Chunk.getMetaKey(7) + "|"
            + org.h2.mvstore.Chunk.getMetaKey(255));

        // Fill it in the way the store would, then write the header at two widths.
        c.len = 4;
        c.block = 12;
        c.maxLen = 900;
        c.mapId = 3;
        c.version = 5;
        c.time = 77;
        System.out.println("filled|" + R.header(c) + "|" + R.footer(c) + "|" + c.getFillRate()
            + "|" + c.estimateHeaderSize());
        System.out.println("filledasstr|" + c.asString());

        for (int width : new int[] { 0, 40, 200, 300 }) {
          try {
            org.h2.mvstore.WriteBuffer b = new org.h2.mvstore.WriteBuffer(8);
            c.writeChunkHeader(b, width);
            byte[] all = new byte[b.position()];
            b.position(0);
            b.get(all);
            System.out.println("hdr" + width + "|" + R.bytes(all));
          } catch (RuntimeException e) {
            System.out.println("hdr" + width + "|THROWS");
          }
        }

        try {
          c.writeChunkHeader(new org.h2.mvstore.WriteBuffer(8), 10);
          System.out.println("toolong|no");
        } catch (RuntimeException e) {
          System.out.println("toolong|THROWS");
        }

        // Read a header back out of the bytes the writer produced.
        org.h2.mvstore.WriteBuffer b = new org.h2.mvstore.WriteBuffer(8);
        c.writeChunkHeader(b, 0);
        byte[] all = new byte[b.position()];
        b.position(0);
        b.get(all);
        java.nio.ByteBuffer buff = java.nio.ByteBuffer.wrap(all);
        String read = org.h2.mvstore.Chunk.readChunkHeader(buff);
        System.out.println("reread|" + read + "|" + buff.position());
        R.T back = R.fromHeader(read);
        System.out.println("round|" + back.id + "|" + back.len + "|" + back.mapId + "|" + back.version
            + "|" + back.time + "|" + back.maxLen + "|" + back.asString());

        // A header with no newline in it is not a header.
        try {
          org.h2.mvstore.Chunk.readChunkHeader(java.nio.ByteBuffer.wrap(
              "chunk:1,len:1".getBytes(java.nio.charset.StandardCharsets.ISO_8859_1)));
          System.out.println("nonewline|no");
        } catch (RuntimeException e) {
          System.out.println("nonewline|THROWS");
        }

        // An id outside the range is refused.
        try {
          R.newChunk(-1);
          System.out.println("badid|no");
        } catch (RuntimeException e) {
          System.out.println("badid|THROWS");
        }
        try {
          R.newChunk((1 << 26));
          System.out.println("bigid|no");
        } catch (RuntimeException e) {
          System.out.println("bigid|THROWS");
        }

        System.out.println("maxid|" + org.h2.mvstore.Chunk.MAX_ID);
        }
        """;

    /// <summary>
    /// The reference builds chunks through anonymous subclasses of an abstract class; this is the
    /// same thing without an anonymous type.
    /// </summary>
    private sealed class TestChunk
        : Chunk
    {
        /// <summary>A chunk with the given id, as the store creates it.</summary>
        internal TestChunk(int id)
            : base(id)
        {
        }

        /// <summary>A chunk described by the text of its header, as the store reads it back.</summary>
        internal TestChunk(string header)
            : base(header)
        {
        }
    }

    private static IEnumerable<string> Lines()
    {
        Chunk c = new TestChunk(7);
        yield return string.Join(
            "|",
            "empty",
            Header(c),
            Footer(c),
            c.EstimateHeaderSize().ToString(CultureInfo.InvariantCulture),
            c.GetFillRate().ToString(CultureInfo.InvariantCulture),
            J(c.IsAllocated()),
            J(c.IsSaved()),
            J(c.IsLive()),
            J(c.IsRewritable()));
        yield return "asstr|" + c.AsString();
        yield return "tostr|" + c;
        yield return "meta|" + Chunk.GetMetaKey(7) + "|" + Chunk.GetMetaKey(255);

        c.Len = 4;
        c.SetBlock(12);
        c.MaxLen = 900;
        c.MapId = 3;
        c.Version = 5;
        c.Time = 77;
        yield return string.Join(
            "|",
            "filled",
            Header(c),
            Footer(c),
            c.GetFillRate().ToString(CultureInfo.InvariantCulture),
            c.EstimateHeaderSize().ToString(CultureInfo.InvariantCulture));
        yield return "filledasstr|" + c.AsString();

        foreach (int width in (int[])[0, 40, 200, 300])
        {
            yield return "hdr" + width.ToString(CultureInfo.InvariantCulture) + "|" + Try(() =>
            {
                WriteBuffer b = new(8);
                c.WriteChunkHeader(b, width);
                byte[] all = b.ToArray();
                b.Position(0);
                byte[] again = new byte[all.Length];
                b.Get(again);
                return Hex.BytesToHex(again);
            }, "THROWS");
        }

        yield return "toolong|" + Try(() =>
        {
            c.WriteChunkHeader(new WriteBuffer(8), 10);
            return "no";
        }, "THROWS");

        WriteBuffer rb = new(8);
        c.WriteChunkHeader(rb, 0);
        byte[] header = rb.ToArray();
        int at = 0;
        string read = Chunk.ReadChunkHeader(header, ref at);
        yield return "reread|" + read + "|" + at.ToString(CultureInfo.InvariantCulture);
        Chunk back = new TestChunk(read);
        yield return string.Join(
            "|",
            "round",
            back.Id.ToString(CultureInfo.InvariantCulture),
            back.Len.ToString(CultureInfo.InvariantCulture),
            back.MapId.ToString(CultureInfo.InvariantCulture),
            back.Version.ToString(CultureInfo.InvariantCulture),
            back.Time.ToString(CultureInfo.InvariantCulture),
            back.MaxLen.ToString(CultureInfo.InvariantCulture),
            back.AsString());

        byte[] noNewline = Encoding.Latin1.GetBytes("chunk:1,len:1");
        int atNoNewline = 0;
        yield return "nonewline|" + Try(() => Chunk.ReadChunkHeader(noNewline, ref atNoNewline), "THROWS");

        yield return "badid|" + Try(() => { _ = new TestChunk(-1); return "no"; }, "THROWS");
        yield return "bigid|" + Try(() => { _ = new TestChunk(1 << 26); return "no"; }, "THROWS");
        yield return "maxid|" + Chunk.MaxId.ToString(CultureInfo.InvariantCulture);
    }

    private static string Header(Chunk c)
        => c.GetHeader() + "|" + Hex.BytesToHex(Encoding.Latin1.GetBytes(c.GetHeader()));

    private static string Footer(Chunk c) => Hex.BytesToHex(c.GetFooterBytes());

    private static string Try(Func<string> body, string onThrow)
    {
        try
        {
            return body();
        }
        catch (Exception)
        {
            return onThrow;
        }
    }

    private static string J(bool value) => value ? "true" : "false";

    private static void Compare(List<string> java, List<string> csharp)
    {
        if (java.Count != csharp.Count)
        {
            int common = Math.Min(java.Count, csharp.Count);
            int first = -1;
            for (int i = 0; i < common; i++)
            {
                if (java[i] != csharp[i])
                {
                    first = i;
                    break;
                }
            }

            Assert.Fail(
                "строк: java=" + java.Count.ToString(CultureInfo.InvariantCulture)
                + " csharp=" + csharp.Count.ToString(CultureInfo.InvariantCulture)
                + "\n  первое различие: " + first.ToString(CultureInfo.InvariantCulture)
                + "\n    java:   " + (first >= 0 ? java[first] : "-")
                + "\n    csharp: " + (first >= 0 ? csharp[first] : "-")
                + "\n  последняя java:   " + (java.Count > 0 ? java[^1] : "-")
                + "\n  последняя csharp: " + (csharp.Count > 0 ? csharp[^1] : "-"));
        }

        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }
}
