/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for LZF.
 *
 * This is the one part of the port where "it round trips" is not enough. Compression has many
 * formats that all decompress to the same bytes, so a port can pass every round trip test and still
 * write files that the reference cannot read. What has to hold here is that the same input produces
 * the *same bytes*: the store's files are read by both sides, so the compressor is compared byte for
 * byte, and the LZF stream on top of it, header and block framing included, is compared the same
 * way.
 *
 * The corpus is where the algorithm decides differently: nothing at all, a few bytes (below the
 * length where it starts looking for matches), one repeated byte (the longest possible
 * back-references), an alternating pattern, text that repeats, bytes that repeat only after a long
 * gap (past the reach of a back-reference), and pseudo-random bytes that do not compress at all and
 * therefore have to come back out stored as they went in. The last item is the one the stream has to
 * get right on its own, because a block that did not get smaller is written verbatim.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using H2Sharp.Compression;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class LzfOracleTests
{
    /// <summary>
    /// The corpus, where every item is deterministic and every item names what it is for.
    /// </summary>
    private static readonly (string Name, byte[] Bytes)[] Corpus =
    [
        ("empty", []),
        ("one byte", [0x41]),
        ("three bytes", [1, 2, 3]),
        ("four bytes", [1, 2, 3, 4]),
        ("five bytes", [1, 2, 3, 4, 5]),
        ("same byte 1000", Filled(0x5A, 1000)),
        ("same byte 4", Filled(0x5A, 4)),
        ("alternating 500", Alternating(500)),
        ("text repeated", Text(40)),
        ("text once", Text(1)),
        ("repeat after a gap", Gap(9_000)),
        ("pseudo random", Random(4_000)),
        ("mixed", Mixed(30_000)),
        ("one block 128K", Block(128 * 1024)),
        ("two blocks", Concat([Block(128 * 1024), Block(70 * 1024)])),
        ("three blocks", Concat([Block(100), Block(128 * 1024), Block(200_000)])),
    ];

    private static byte[] Filled(byte value, int length)
    {
        byte[] bytes = new byte[length];
        Array.Fill(bytes, value);
        return bytes;
    }

    private static byte[] Alternating(int length)
    {
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i & 1);
        }

        return bytes;
    }

    private static byte[] Text(int times)
    {
        byte[] line = Encoding.ASCII.GetBytes(
            "CREATE TABLE PUBLIC.T(ID BIGINT PRIMARY KEY, NAME CHARACTER VARYING(255) NOT NULL);");
        byte[] bytes = new byte[line.Length * times];
        for (int i = 0; i < times; i++)
        {
            Array.Copy(line, 0, bytes, i * line.Length, line.Length);
        }

        return bytes;
    }

    /// <summary>
    /// A long run of one byte, then the same run again: the second one is further back than a
    /// back-reference may reach, so it has to come out as literals.
    /// </summary>
    private static byte[] Gap(int length)
        => Concat([Filled(0x11, length), Filled(0x11, length)]);

    private static byte[] Random(int length)
    {
        byte[] bytes = new byte[length];
        uint state = 0x1234_5678;
        for (int i = 0; i < length; i++)
        {
            state = (state * 1_664_525) + 1_013_904_223;
            bytes[i] = (byte)(state >> 24);
        }

        return bytes;
    }

    private static byte[] Mixed(int length)
    {
        byte[] bytes = new byte[length];
        byte[] random = Random(length / 2);
        Array.Copy(random, 0, bytes, 0, length / 2);
        Array.Copy(Text(20), 0, bytes, length / 2, Math.Min(Text(20).Length, length - (length / 2)));
        for (int i = 0; i < 64; i++)
        {
            bytes[length - 64 + i] = (byte)i;
        }

        return bytes;
    }

    private static byte[] Block(int length) => Random(length);

    private static byte[] Concat(byte[][] parts)
    {
        byte[] bytes = new byte[parts.Sum(static p => p.Length)];
        int at = 0;
        foreach (byte[] part in parts)
        {
            Array.Copy(part, 0, bytes, at, part.Length);
            at += part.Length;
        }

        return bytes;
    }

    [JavaOracleFact]
    public void TheCompressedBytesMatchTheReference()
    {
        // The data goes over as hex and comes back as hex, because the thing being compared is a
        // sequence of bytes and a number is not one.
        List<string> java = OracleRunner.Run(
                [.. Corpus.Select(static c => Hex.BytesToHex(c.Bytes))],
                """
                java.util.function.Function<String, byte[]> bytes = s -> {
                  if (s.isEmpty()) {
                    return new byte[0];
                  }
                  byte[] out = new byte[s.length() / 2];
                  for (int i = 0; i < out.length; i++) {
                    out[i] = (byte) Integer.parseInt(s.substring(i * 2, i * 2 + 2), 16);
                  }
                  return out;
                };
                for (int i = 0; i < inputs.length; i++) {
                  byte[] in = bytes.apply(inputs[i]);
                  StringBuilder b = new StringBuilder();
                  try {
                    org.h2.compress.CompressLZF c = new org.h2.compress.CompressLZF();
                    byte[] out = new byte[in.length * 2 + 128];
                    int end = c.compress(in, 0, in.length, out, 0);
                    b.append(end).append('|');
                    b.append(org.h2.util.StringUtils.convertBytesToHex(out, end)).append('|');
                    byte[] back = new byte[in.length];
                    c.expand(out, 0, end, back, 0, in.length);
                    b.append(org.h2.util.StringUtils.convertBytesToHex(back, in.length)).append('|');
                    b.append(org.h2.util.StringUtils.convertBytesToHex(new org.h2.compress.CompressNo()
                        .compress(in, 0, in.length, new byte[in.length], 0) == in.length
                            ? in
                            : new byte[0]));
                    System.out.println(hex(b.toString()));
                  } catch (RuntimeException e) {
                    b.setLength(0);
                    b.append("REFUSED");
                    System.out.println(hex(b.toString()));
                  }
                }
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = [.. Corpus.Select(Describe)];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"{Corpus[i].Name}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    [JavaOracleFact]
    public void TheStreamMatchesTheReferenceByteForByte()
    {
        List<string> java = OracleRunner.Run(
                [.. Corpus.Select(static c => Hex.BytesToHex(c.Bytes))],
                """
                java.util.function.Function<String, byte[]> bytes = s -> {
                  if (s.isEmpty()) {
                    return new byte[0];
                  }
                  byte[] out = new byte[s.length() / 2];
                  for (int i = 0; i < out.length; i++) {
                    out[i] = (byte) Integer.parseInt(s.substring(i * 2, i * 2 + 2), 16);
                  }
                  return out;
                };
                for (int i = 0; i < inputs.length; i++) {
                  byte[] in = bytes.apply(inputs[i]);
                  StringBuilder b = new StringBuilder();
                  try {
                    java.io.ByteArrayOutputStream file = new java.io.ByteArrayOutputStream();
                    try (org.h2.compress.LZFOutputStream out = new org.h2.compress.LZFOutputStream(file)) {
                      out.write(in);
                    }
                    b.append(org.h2.util.StringUtils.convertBytesToHex(file.toByteArray())).append('|');
                    org.h2.compress.LZFInputStream in2 =
                        new org.h2.compress.LZFInputStream(new java.io.ByteArrayInputStream(file.toByteArray()));
                    java.io.ByteArrayOutputStream read = new java.io.ByteArrayOutputStream();
                    byte[] chunk = new byte[1000];
                    int r;
                    while ((r = in2.read(chunk)) > 0) {
                      read.write(chunk, 0, r);
                    }
                    in2.close();
                    b.append(read.size()).append('|');
                    b.append(org.h2.util.StringUtils.convertBytesToHex(read.toByteArray()));
                    System.out.println(hex(b.toString()));
                  } catch (java.io.IOException e) {
                    b.setLength(0);
                    b.append("REFUSED");
                    System.out.println(hex(b.toString()));
                  }
                }
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = [.. Corpus.Select(DescribeStream)];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"{Corpus[i].Name}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// A stream written here is read by the reference, and a stream written by the reference is read
    /// here, which is what the store will do with the files of the other side. So what crosses to
    /// the reference is a whole stream file, written the way the reference writes one.
    /// </summary>
    /// <remarks>
    /// The two shorter items of the corpus never get that far: a block the compressor cannot read is
    /// refused by both sides before a stream is written, so they are refused here too rather than
    /// quietly skipped.
    /// </remarks>
    [JavaOracleFact]
    public void TheStreamsOfTheTwoSidesReadEachOther()
    {
        List<string> java = OracleRunner.Run(
                [.. Corpus.Select(static c => AsText(() => Hex.BytesToHex(WriteStream(c.Bytes))))],
                """
                java.util.function.Function<String, byte[]> bytes = s -> {
                  if (s.isEmpty()) {
                    return new byte[0];
                  }
                  byte[] out = new byte[s.length() / 2];
                  for (int i = 0; i < out.length; i++) {
                    out[i] = (byte) Integer.parseInt(s.substring(i * 2, i * 2 + 2), 16);
                  }
                  return out;
                };
                for (int i = 0; i < inputs.length; i++) {
                  StringBuilder b = new StringBuilder();
                  try {
                    if (inputs[i].equals("REFUSED")) {
                      // The compressor could not read that input, so there is no stream to read.
                      throw new IllegalArgumentException();
                    }
                    byte[] file = bytes.apply(inputs[i]);
                    org.h2.compress.LZFInputStream in = new org.h2.compress.LZFInputStream(
                        new java.io.ByteArrayInputStream(file));
                    java.io.ByteArrayOutputStream read = new java.io.ByteArrayOutputStream();
                    byte[] chunk = new byte[777];
                    int r;
                    while ((r = in.read(chunk)) > 0) {
                      read.write(chunk, 0, r);
                    }
                    in.close();
                    b.append(read.size()).append('|');
                    b.append(org.h2.util.StringUtils.convertBytesToHex(read.toByteArray()));
                    System.out.println(hex(b.toString()));
                  } catch (java.io.IOException | IllegalArgumentException
                      | ArrayIndexOutOfBoundsException e) {
                    b.setLength(0);
                    b.append("REFUSED");
                    System.out.println(hex(b.toString()));
                  }
                }
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp =
        [
            .. Corpus.Select(c => AsText(() =>
            {
                byte[] read = ReadStream(WriteStream(c.Bytes));
                return $"{read.Length}|{Hex.BytesToHex(read)}";
            })),
        ];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"{Corpus[i].Name}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// What the reference does with damaged input is part of the format, because a store that has to
    /// read a damaged file has to fail the same way.
    /// </summary>
    [Fact]
    public void DamagedInputIsRefusedTheSameWay()
    {
        CompressLZF compress = new();

        // A back-reference that reaches before the start of the output.
        Assert.Throws<IndexOutOfRangeException>(() => CompressLZF.Expand([0xE0, 0x00], new byte[8]));

        // A back-reference that would write past the end of the output, which the reference checks
        // for once and then copies without checking.
        Assert.Throws<IndexOutOfRangeException>(() => CompressLZF.Expand([0xE0, 0x02, 1, 2, 3, 4], new byte[4]));

        // A place before the start of either buffer.
        Assert.Throws<ArgumentException>(() => compress.Expand([1, 2, 3], -1, 3, new byte[8], 0, 3));
        Assert.Throws<ArgumentException>(() => compress.Expand([1, 2, 3], 0, 3, new byte[8], -1, 3));
        Assert.Throws<ArgumentException>(() => compress.Expand([1, 2, 3], 0, 3, new byte[8], 0, -1));

        Assert.Equal(Compressors.LZF, compress.GetAlgorithm());
        Assert.Equal(Compressors.No, new CompressNo().GetAlgorithm());
    }

    [Fact]
    public void EveryCorpusItemComesBackOutAsItWentIn()
    {
        foreach ((string name, byte[] bytes) in Corpus)
        {
            // The compressor reads two bytes before it looks for a match, so it refuses anything
            // shorter than that, and so does the reference. The store never hands it that much:
            // a block is either empty or a whole buffer.
            if (bytes.Length < 2)
            {
                Assert.Throws<IndexOutOfRangeException>(() => Compress(bytes));
                continue;
            }

            byte[] compressed = Compress(bytes);

            // What the format can cost on top of the data is one control byte for every run of
            // literals, and a run holds at most 32 of them, so data that does not compress at all
            // comes out bigger by about one thirty-secondth. On a few bytes of input it costs one
            // whole control byte, which is why a stream stores a block verbatim when the compressed
            // form came out longer.
            Assert.True(compressed.Length <= bytes.Length + (bytes.Length / 32) + 1, name);

            byte[] back = new byte[bytes.Length];
            new CompressLZF().Expand(compressed, 0, compressed.Length, back, 0, bytes.Length);

            Assert.True(Hex.BytesToHex(back) == Hex.BytesToHex(bytes), name);
        }
    }

    private static byte[] Compress(byte[] bytes)
    {
        CompressLZF compress = new();
        byte[] outBuffer = new byte[(bytes.Length * 2) + 128];
        int end = compress.Compress(bytes, 0, bytes.Length, outBuffer, 0);
        return outBuffer[..end];
    }

    /// <summary>
    /// The whole stream file: header, and one block per buffer's worth of what was written.
    /// </summary>
    private static byte[] WriteStream(byte[] data)
    {
        using MemoryStream buffer = new();
        using (LZFOutputStream out2 = new(buffer))
        {
            out2.Write(data, 0, data.Length);
        }

        return buffer.ToArray();
    }

    private static byte[] ReadStream(byte[] file)
    {
        // Closing the writer closes the stream under it, so the bytes are taken first and read back
        // through a stream of their own.
        using MemoryStream input = new(file);
        using LZFInputStream reader = new(input);
        using MemoryStream read = new();
        reader.CopyTo(read);
        return read.ToArray();
    }

    private static string Describe((string Name, byte[] Bytes) item)
    {
        try
        {
            CompressLZF compress = new();
            byte[] bytes = item.Bytes;
            byte[] outBuffer = new byte[(bytes.Length * 2) + 128];
            int end = compress.Compress(bytes, 0, bytes.Length, outBuffer, 0);

            byte[] back = new byte[bytes.Length];
            compress.Expand(outBuffer, 0, end, back, 0, bytes.Length);

            return string.Join(
                '|',
                end,
                Hex.BytesToHex(outBuffer.AsSpan(0, end)),
                Hex.BytesToHex(back),
                Hex.BytesToHex(new CompressNo().Compress(bytes, 0, bytes.Length, new byte[bytes.Length], 0) == bytes.Length ? bytes : []));
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or H2SharpException)
        {
            return "REFUSED";
        }
    }

    private static string DescribeStream((string Name, byte[] Bytes) item)
    {
        try
        {
            byte[] file;
            byte[] read;
            using (MemoryStream buffer = new())
            {
                using (LZFOutputStream out2 = new(buffer))
                {
                    out2.Write(item.Bytes, 0, item.Bytes.Length);
                }

                file = buffer.ToArray();
            }

            using (MemoryStream buffer = new(file))
            {
                using LZFInputStream input = new(buffer);
                using MemoryStream sink = new();
                input.CopyTo(sink);
                read = sink.ToArray();
            }

            return string.Join('|', Hex.BytesToHex(file), read.Length, Hex.BytesToHex(read));
        }
        catch (Exception e) when (e is IOException or ArgumentException or IndexOutOfRangeException)
        {
            return "REFUSED";
        }
    }

    /// <summary>
    /// What the reference refuses rather than answers, written the same way on both sides.
    /// </summary>
    private static string AsText(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or IOException)
        {
            return "REFUSED";
        }
    }

    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        if (only.Length == 0 || !only.All(static c => char.IsAsciiHexDigit(c)))
        {
            return hex;
        }

        StringBuilder b = new(only.Length / 4);
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            _ = b.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return b.ToString();
    }
}
