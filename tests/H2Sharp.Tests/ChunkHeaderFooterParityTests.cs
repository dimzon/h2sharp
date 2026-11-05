/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The chunk header and footer were rewritten from StringBuilder -> string -> GetBytes to
 * direct span formatting; the output must be byte-for-byte identical to the previous
 * format, which is replicated here with the still-existing DataUtils.AppendMap.
 */
using System;
using System.Text;
using H2Sharp.MvStore;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ChunkHeaderFooterParityTests
{
    private static SFChunk MakeChunk(Action<SFChunk>? configure = null)
    {
        SFChunk chunk = new(0x1234);
        chunk.Len = 0x1F;
        chunk.PageCount = 0x2F;
        chunk.MaxLen = 0x123456789ABL;
        chunk.MapId = 0x3F;
        chunk.LayoutRootPos = 0x1122334455667788L;
        chunk.Time = 0x55667788;
        chunk.Version = unchecked((long)0x99AABBCCDDEEFF00UL);
        configure?.Invoke(chunk);
        return chunk;
    }

    private static string LegacyHeader(SFChunk chunk)
    {
        StringBuilder buff = new(240);
        DataUtils.AppendMap(buff, "chunk", chunk.Id);
        DataUtils.AppendMap(buff, "len", chunk.Len);
        DataUtils.AppendMap(buff, "pages", chunk.PageCount);
        if (chunk.PinCountForTest > 0)
        {
            DataUtils.AppendMap(buff, "pinCount", chunk.PinCountForTest);
        }

        DataUtils.AppendMap(buff, "max", chunk.MaxLen);
        DataUtils.AppendMap(buff, "map", chunk.MapId);
        DataUtils.AppendMap(buff, "root", chunk.LayoutRootPos);
        DataUtils.AppendMap(buff, "time", chunk.Time);
        DataUtils.AppendMap(buff, "version", chunk.Version);
        if (chunk.Next != 0)
        {
            DataUtils.AppendMap(buff, "next", chunk.Next);
        }

        if (chunk.TocPos > 0)
        {
            DataUtils.AppendMap(buff, "toc", chunk.TocPos);
        }

        return buff.ToString();
    }

    private static byte[] LegacyFooter(SFChunk chunk)
    {
        StringBuilder buff = new(128);
        DataUtils.AppendMap(buff, "chunk", chunk.Id);
        DataUtils.AppendMap(buff, "len", chunk.Len);
        DataUtils.AppendMap(buff, "version", chunk.Version);
        byte[] bytes = Encoding.Latin1.GetBytes(buff.ToString());
        int checksum = DataUtils.GetFletcher32(bytes);
        DataUtils.AppendMap(buff, "fletcher", checksum);
        while (buff.Length < 128 - 1)
        {
            _ = buff.Append(' ');
        }

        _ = buff.Append('\n');
        return Encoding.Latin1.GetBytes(buff.ToString());
    }

    [Fact]
    public void HeaderMatchesLegacyFormat()
    {
        Span<byte> header = stackalloc byte[256];
        foreach (SFChunk chunk in new[]
        {
            MakeChunk(),
            MakeChunk(c => c.Next = -1),
            MakeChunk(c => c.Next = 0xABCDEF),
            MakeChunk(c => { c.PageCount = 0x7FFFFFFF; c.PinCountForTest = 0x7FFFFFFF; }),
            MakeChunk(c => c.TocPos = 0xABCDEF),
            MakeChunk(c => { c.MapId = -1; c.LayoutRootPos = -1; c.Time = -1; c.Version = -1; }),
        })
        {
            int n = chunk.WriteHeader(header);
            byte[] expectedBytes = Encoding.Latin1.GetBytes(LegacyHeader(chunk));
            byte[] actualBytes = header[..n].ToArray();
            if (!expectedBytes.AsSpan().SequenceEqual(actualBytes))
            {
                Assert.Fail(
                    "\nlegacy=" + Encoding.Latin1.GetString(expectedBytes) +
                    "\nactual =" + Encoding.Latin1.GetString(actualBytes));
            }
        }
    }

    [Fact]
    public void FooterMatchesLegacyFormat()
    {
        foreach (SFChunk chunk in new[]
        {
            MakeChunk(),
            MakeChunk(c => c.Version = -1),
            MakeChunk(c => c.Len = 0x7FFFFFFF),
        })
        {
            byte[] expected = LegacyFooter(chunk);
            byte[] actual = chunk.GetFooterBytes();
            if (!expected.AsSpan().SequenceEqual(actual))
            {
                Assert.Fail(
                    "\nlegacy=" + Encoding.Latin1.GetString(expected) +
                    "\nactual =" + Encoding.Latin1.GetString(actual));
            }
        }
    }
}
