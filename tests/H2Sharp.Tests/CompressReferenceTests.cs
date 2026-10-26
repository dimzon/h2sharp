/*
 * Ported from org.h2.test.unit.TestCompress (the LZF part).
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The reference's own tests of the compressor, rather than another corpus of my own.
 *
 * That matters here more than for most parts of the port: these are the cases the algorithm's author
 * chose, at the lengths they chose, with the data his own generator produced. The generator is ported
 * too (see JavaRandom), because a random buffer of my own would be a different test.
 *
 * What is left out and why: the test drives most of its cases through CompressTool, which frames a
 * buffer with an algorithm number and a length before handing it to the compressor. That framing is
 * in org.h2.tools and is not ported yet, so the round trips here go straight to the compressor and to
 * the stream, and the tool is where the framing gets tested when the tool is ported. The variable
 * length integers of the same tool, and the test of the database that measures a page, are not part
 * of LZF at all.
 */
using System;
using System.IO;
using System.Text;
using H2Sharp.Compression;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class CompressReferenceTests
{
    /// <summary>
    /// The lengths the reference runs in its quick pass.
    /// </summary>
    private static readonly int[] Lengths = [0, 1, 7, 50, 200];

    [Fact]
    public void EveryLengthAndPatternComesBackOutAsItWentIn()
    {
        foreach (int length in Lengths)
        {
            RoundTrip(length);
        }

        // The long one is where a back-reference can reach as far as it may, and where the
        // reference wants to know that the hash table still finds the earlier places.
        RoundTrip(4_000_000);
    }

    [Fact]
    public void TheWindowOverBytesIsTheSameAlgorithmAsTheArray()
    {
        // The reference runs the same cases through its ByteBuffer form and asserts that the answer
        // is the same bytes. Here the same two calls are a span and an array, which is the same
        // difference: the window form is what the store will use over a page.
        foreach (int length in Lengths)
        {
            if (length < 4)
            {
                continue;
            }

            WindowRoundTrip(length);
        }

        WindowRoundTrip(4_000_000);
    }

    [Fact]
    public void TheVariableEndComesBackOutAsItWentIn()
    {
        // Ninety zeros and then a tail that is one byte longer every time, up to a hundred bytes of
        // tail: the end of the data falls at every possible place relative to the literals and the
        // matches, and this is the case that walks them.
        string zeros = new('0', 90);
        for (int i = 0; i < 100; i++)
        {
            StringBuilder b = new(zeros);
            for (int j = 0; j < i; j++)
            {
                _ = b.Append((char)('1' + j));
            }

            byte[] bytes = Encoding.ASCII.GetBytes(b.ToString());
            AssertBytesComeBack(bytes, $"{i} tail");
        }
    }

    [Fact]
    public void AFileOfTextComesBackThroughTheStream()
    {
        // The reference's own cases are a few hundred bytes, which is one block. A stream is a file,
        // and a file is many blocks, and a block is the unit the framing is written in: every block
        // says how long it is compressed and how long it is not. Nothing above exercises the second
        // block, and nothing above the size of a buffer exercises the boundary between them.
        //
        // The size is the size of the real text this port was measured on, so the layout of the
        // blocks is that of a real file and not of a convenient one, and the text is prose-shaped:
        // words, punctuation, numbers and markup, which is what compresses partially rather than
        // entirely or not at all.
        byte[] data = Prose(810_738);
        byte[] file = WriteStream(data);
        byte[] test = ReadStream(file);

        Assert.Equal(data.Length, test.Length);
        Assert.True(Hex.BytesToHex(data) == Hex.BytesToHex(test), "the whole file");
    }

    /// <summary>
    /// Text that reads like text and is fixed by its size alone: the same bytes every run, without a
    /// file to carry them in.
    /// </summary>
    private static byte[] Prose(int length)
    {
        string[] words =
        [
            "the", "database", "stores", "each", "page", "compressed", "as", "one", "block", "of",
            "bytes", "and", "reads", "it", "back", "when", "a", "query", "needs", "the", "row",
            "таблица", "хранит", "каждую", "страницу", "сжатой", "в", "один", "блок", "байт",
            "и", "разворачивает", "её", "обратно", "когда", "запрос", "нужен", "ряд", "1 234",
            "2004—2026", "(см. [[]])",
        ];
        byte[] bytes = new byte[length];
        Random r = new JavaRandom(0x5EED_1234);
        int at = 0;
        while (at < length)
        {
            string word = words[r.Next(words.Length)];
            int n = Encoding.UTF8.GetByteCount(word);
            if (at + n + 1 > length)
            {
                break;
            }

            Encoding.UTF8.GetBytes(word, bytes.AsSpan(at));
            at += n;
            bytes[at++] = (byte)r.Next(4) switch { 0 => (byte)' ', 1 => (byte)',', 2 => (byte)'.', _ => (byte)'\n' };
        }

        return bytes;
    }

    [Fact]
    public void TheStreamCarriesEveryCaseBackAndForth()
    {
        foreach (int length in Lengths)
        {
            StreamRoundTrip(length);
        }

        StreamRoundTrip(200_000);
    }

    private static void RoundTrip(int length)
    {
        Random r = new JavaRandom(length);
        for (int pattern = 0; pattern < 4; pattern++)
        {
            byte[] b = new byte[length];
            switch (pattern)
            {
                case 0:
                    // Left empty on purpose.
                    break;

                case 1:
                    r.NextBytes(b);
                    break;

                case 2:
                    for (int x = 0; x < length; x++)
                    {
                        b[x] = (byte)(x & 10);
                    }

                    break;

                default:
                    for (int x = 0; x < length; x++)
                    {
                        b[x] = (byte)(x / 10);
                    }

                    break;
            }

            if (r.Next(2) < 1)
            {
                for (int x = 0; x < length; x++)
                {
                    if (r.Next(20) < 1)
                    {
                        b[x] = (byte)r.Next(255);
                    }
                }
            }

            AssertBytesComeBack(b, $"length {length}, pattern {pattern}");
        }
    }

    private static void WindowRoundTrip(int length)
    {
        Random r = new JavaRandom(length);
        for (int pattern = 0; pattern < 4; pattern++)
        {
            byte[] b = new byte[length];
            switch (pattern)
            {
                case 0:
                    break;

                case 1:
                    r.NextBytes(b);
                    break;

                case 2:
                    for (int x = 0; x < length; x++)
                    {
                        b[x] = (byte)(x & 10);
                    }

                    break;

                default:
                    for (int x = 0; x < length; x++)
                    {
                        b[x] = (byte)(x / 10);
                    }

                    break;
            }

            if (r.Next(2) < 1)
            {
                for (int x = 0; x < length; x++)
                {
                    if (r.Next(20) < 1)
                    {
                        b[x] = (byte)r.Next(255);
                    }
                }
            }

            CompressLZF compress = new();
            byte[] temp = new byte[(100 * 2) + (b.Length * 2)];
            int end = compress.Compress(b.AsSpan(), temp.AsSpan(), 0);
            byte[] expanded = new byte[b.Length];
            CompressLZF.Expand(temp.AsSpan(0, end), expanded.AsSpan());
            Assert.True(Hex.BytesToHex(expanded) == Hex.BytesToHex(b), $"length {length}, pattern {pattern}");
        }
    }

    /// <summary>
    /// The assertions of the reference: the data comes back the same length and the same bytes,
    /// once into a fresh buffer and once into a buffer that was zeroed first, which is how it knows
    /// the expansion wrote every byte and no more.
    /// </summary>
    /// <remarks>
    /// The reference reaches the compressor through CompressTool, which guards the two lengths the
    /// compressor cannot read at all: it reads two bytes before it looks for a match, so a buffer
    /// shorter than that ends in an index error. The stream guards them instead — a block that is
    /// empty is not written — so that is where the shortest lengths are checked here, and the raw
    /// compressor is asked to refuse them on purpose.
    /// </remarks>
    private static void AssertBytesComeBack(byte[] b, string what)
    {
        byte[] compressed = WriteStream(b);
        byte[] test = ReadStream(compressed);
        Assert.Equal(b.Length, test.Length);
        Assert.True(Hex.BytesToHex(b) == Hex.BytesToHex(test), what);

        if (b.Length < 2)
        {
            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                byte[] raw = new byte[(b.Length * 2) + 128];
                _ = new CompressLZF().Compress(b, 0, b.Length, raw, 0);
            });

            return;
        }

        byte[] zeroed = new byte[b.Length + 64];
        byte[] raw = new byte[(b.Length * 2) + 128];
        CompressLZF compress = new();
        int end = compress.Compress(b, 0, b.Length, raw, 0);
        compress.Expand(raw, 0, end, zeroed, 0, b.Length);
        Assert.True(Hex.BytesToHex(b) == Hex.BytesToHex(zeroed[..b.Length]), what);
        Assert.True(AllZero(zeroed[b.Length..]), what);
    }

    private static bool AllZero(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            if (b != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void StreamRoundTrip(int length)
    {
        Random r = new JavaRandom(length);
        byte[] b = new byte[length];
        r.NextBytes(b);
        Assert.True(Hex.BytesToHex(b) == Hex.BytesToHex(ReadStream(WriteStream(b))), $"stream {length}");
    }

    private static byte[] WriteStream(byte[] data)
    {
        using MemoryStream buffer = new();
        using (LZFOutputStream output = new(buffer))
        {
            output.Write(data, 0, data.Length);
        }

        return buffer.ToArray();
    }

    private static byte[] ReadStream(byte[] file)
    {
        using MemoryStream input = new(file);
        using LZFInputStream reader = new(input);
        using MemoryStream read = new();
        reader.CopyTo(read);
        return read.ToArray();
    }
}
