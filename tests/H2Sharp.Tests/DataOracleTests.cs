/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for org.h2.store.Data and DataReader — the byte format of a page and
 * of the values on it, read back through the other end.
 *
 * Both sides are compared as hex rather than as numbers wherever a byte could be involved,
 * because these two classes have no opinion of their own: they are the format, and the only
 * question worth asking is whether the same bytes come out. The variable size integer is read
 * as a *sequence of byte patterns*, not as a list of numbers, for the same reason — a pattern
 * is what a file holds, and the mapping from pattern to value is exactly what can drift.
 *
 * The corpus for the sizes is the boundary sweep: fillAligned rounds up to a multiple of the
 * block size after reserving two bytes for the checksum, so 0..6 give 8, 7..14 give 16, and a
 * sweep that stops at a round number would miss the whole rule. The capacity sweep walks the
 * expansion, which is (length + plus) * 2 rather than length * 2 — a different rule, not just
 * a different size, and both sides are asked what the buffer became rather than how far it
 * reached.
 *
 * The reader is probed where it is most fragile: a truncated multi-byte character at the end of
 * the stream. The reference's decoder does not validate, so it runs off the end and the caller
 * sees the end of the stream; a decoder that substituted a replacement character instead would
 * produce a file that reads back as different text.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using H2Sharp.Store;
using H2Sharp.Tests.Oracle;
using Xunit;
using PageData = H2Sharp.Store.Data;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class DataOracleTests
{
    [JavaOracleFact]
    public void Data_MatchesReference()
    {
        string body = """
                      int[] sizes = nums;
                      String[] texts = inputs;
                      for (int size : sizes) {
                        org.h2.store.Data d = org.h2.store.Data.create(4);
                        d.writeInt(0x01020304);
                        d.checkCapacity(3);
                        d.write(new byte[] { 1, 2, 3, 4, 5 }, 1, 3);
                        System.out.println(d.length() + " " + bytesHexN.apply(d.getBytes(), d.length()));
                        byte[] out = new byte[5];
                        d.read(out, 0, 5);
                        d.reset();
                        System.out.println(d.readInt() + " " + bytesHexN.apply(out, out.length));
                      }
                      for (int pos : sizes) {
                        byte[] filler = new byte[32];
                        java.util.Arrays.fill(filler, (byte) 9);
                        org.h2.store.Data d = org.h2.store.Data.create(4);
                        d.checkCapacity(pos);
                        d.write(filler, 0, pos);
                        d.fillAligned();
                        System.out.println(d.length() + " " + d.getBytes().length);
                      }
                      for (int plus : sizes) {
                        org.h2.store.Data d = org.h2.store.Data.create(4);
                        for (int i = 0; i < 4; i++) {
                          d.write(new byte[] { 1 }, 0, 1);
                          d.checkCapacity(plus);
                          System.out.print(d.getBytes().length + ",");
                        }
                        System.out.println();
                      }
                      for (String s : texts) {
                        java.io.ByteArrayOutputStream bo = new java.io.ByteArrayOutputStream();
                        org.h2.store.Data.copyString(new java.io.StringReader(s), bo);
                        String h = bytesHex.apply(bo.toByteArray());
                        System.out.println(h.isEmpty() ? "-" : h);
                      }
                      try {
                        org.h2.store.Data d = org.h2.store.Data.create(4);
                        d.writeInt(1);
                        d.reset();
                        d.readInt();
                        d.readInt();
                        System.out.println("no-throw");
                      } catch (RuntimeException ex) {
                        System.out.println("THROW");
                      }
            """;

        List<string> java = OracleRunner.Run(Texts(), Setup + body, Sizes());
        List<string> csharp = Describe();

        CompareLines(java, csharp);
    }

    [JavaOracleFact]
    public void DataReader_MatchesReference()
    {
        string body = """
                      String[] texts = { "", "a", "ab", "café", "日本", "aé日" };
                      byte[][] patterns = {
                          {}, {0}, {1}, {127}, {(byte) 128}, {(byte) 129}, {(byte) 254}, {(byte) 255},
                          {1, 2}, {(byte) 129, 2}, {1, (byte) 130, 3}, {(byte) 131, (byte) 132, 4},
                          {(byte) 133, (byte) 134, (byte) 135, 6}, {(byte) 136, 1, 2, 3, 4, 5, 6}
                      };
                      for (byte[] pattern : patterns) {
                        StringBuilder sb = new StringBuilder();
                        for (byte b : pattern) {
                          sb.append(String.format("%02x", b & 0xff));
                        }
                        StringBuilder value = new StringBuilder();
                        try {
                          org.h2.store.DataReader r =
                              new org.h2.store.DataReader(new java.io.ByteArrayInputStream(pattern));
                          value.append(r.readVarInt());
                        } catch (java.io.IOException ex) {
                          value.append("THROW");
                        }
                        System.out.println(sb + " -> " + value);
                      }
                      for (String s : texts) {
                        byte[] b = s.getBytes(java.nio.charset.StandardCharsets.UTF_8);
                        org.h2.store.DataReader r =
                            new org.h2.store.DataReader(new java.io.ByteArrayInputStream(b));
                        char[] buff = new char[10];
                        int n = r.read(buff, 0, 10);
                        System.out.println(s.length() + " " + n + " " + charsHex.apply(buff, Math.max(n, 0)));
                      }
                      byte[] truncated = new byte[] { (byte) 0xc3 };
                      org.h2.store.DataReader r =
                          new org.h2.store.DataReader(new java.io.ByteArrayInputStream(truncated));
                      char[] one = new char[1];
                      int m = r.read(one, 0, 1);
                      System.out.println("truncated " + m);
                      org.h2.store.DataReader empty =
                          new org.h2.store.DataReader(new java.io.ByteArrayInputStream(new byte[0]));
                      System.out.println("empty " + empty.read(one, 0, 1));
            """;

        List<string> java = OracleRunner.Run([], Setup + body);
        List<string> csharp = DescribeReader();

        CompareLines(java, csharp);
    }

    /// <summary>
    /// Compares line by line and, on the first difference, shows the lines around it. A
    /// mismatch in a sweep is meaningless without its position: "12" is the answer for a dozen
    /// different inputs, and only the neighbours say which one.
    /// </summary>
    private static void CompareLines(List<string> java, List<string> csharp)
    {
        if (java.Count != csharp.Count)
        {
            StringBuilder message = new();
            _ = message.Append("line counts differ: java ").Append(java.Count)
                .Append(", csharp ").Append(csharp.Count).Append("\njava tail:\n");
            foreach (string line in java.Skip(Math.Max(0, java.Count - 6)))
            {
                _ = message.Append("  ").Append(line).Append('\n');
            }

            _ = message.Append("csharp tail:\n");
            foreach (string line in csharp.Skip(Math.Max(0, csharp.Count - 6)))
            {
                _ = message.Append("  ").Append(line).Append('\n');
            }

            Assert.Fail(message.ToString());
        }

        for (int i = 0; i < java.Count; i++)
        {
            if (java[i] == csharp[i])
            {
                continue;
            }

            StringBuilder message = new();
            _ = message.Append("line ").Append(i).Append(":\n");
            for (int j = Math.Max(0, i - 3); j <= i; j++)
            {
                _ = message.Append(j == i ? "> " : "  ").Append(j).Append("  java:   ").Append(java[j])
                    .Append(" | csharp: ").Append(csharp[j]).Append('\n');
            }

            Assert.Fail(message.ToString());
        }
    }

    private static List<int> Sizes() => [0, 1, 2, 3, 4, 5, 6, 7, 8, 14, 15, 16, 22, 23, 24];

    private static List<string> Texts() => ["", "a", "ab", "café", "日本", "aé日", new string('x', 5000)];

    private static List<string> Describe()
    {
        List<string> lines = [];
        foreach (int size in Sizes())
        {
            PageData d = PageData.Create(4);
            d.WriteInt(0x01020304);
            d.CheckCapacity(3);
            d.Write([1, 2, 3, 4, 5], 1, 3);
            lines.Add(d.Length() + " " + Hex(d.GetBytes(), d.Length()));
            byte[] read = new byte[5];
            d.Read(read, 0, 5);
            d.Reset();
            lines.Add(d.ReadInt() + " " + Hex(read, read.Length));
        }

        foreach (int pos in Sizes())
        {
            PageData d = PageData.Create(4);
            d.CheckCapacity(pos);
            byte[] filler = new byte[32];
            Array.Fill(filler, (byte)9);
            d.Write(filler, 0, pos);
            d.FillAligned();
            lines.Add(d.Length() + " " + d.GetBytes().Length);
        }

        foreach (int plus in Sizes())
        {
            List<string> sizes = [];
            PageData d = PageData.Create(4);
            for (int i = 0; i < 4; i++)
            {
                d.Write([1], 0, 1);
                d.CheckCapacity(plus);
                sizes.Add(d.GetBytes().Length.ToString());
            }

            // Each capacity is printed with a trailing comma on the reference side, so the
            // last one is there too; joining without it would differ on the line and on
            // nothing else.
            lines.Add(string.Join(",", sizes) + ",");
        }

        foreach (string text in Texts())
        {
            using MemoryStream target = new();
            PageData.CopyString(new StringReader(text), target);
            string hex = Hex(target.ToArray(), (int)target.Length);
            // An empty result is a blank line, and the harness drops blank lines — so both
            // sides say "-" for it, the way the harness's own hex() helper does.
            lines.Add(hex.Length == 0 ? "-" : hex);
        }

        try
        {
            PageData d = PageData.Create(4);
            d.WriteInt(1);
            d.Reset();
            _ = d.ReadInt();
            _ = d.ReadInt();
            lines.Add("no-throw");
        }
        catch (Exception)
        {
            lines.Add("THROW");
        }

        return lines;
    }

    private static List<string> DescribeReader()
    {
        List<string> lines = [];
        byte[][] patterns =
        [
            [], [0], [1], [127], [128], [129], [254], [255],
            [1, 2], [129, 2], [1, 130, 3], [131, 132, 4],
            [133, 134, 135, 6], [136, 1, 2, 3, 4, 5, 6],
        ];

        foreach (byte[] pattern in patterns)
        {
            StringBuilder value = new();
            try
            {
                using DataReader r = new(new MemoryStream(pattern));
                _ = value.Append(r.ReadVarInt());
            }
            catch (IOException)
            {
                _ = value.Append("THROW");
            }

            lines.Add(Hex(pattern, pattern.Length) + " -> " + value);
        }

        foreach (string text in new[] { "", "a", "ab", "café", "日本", "aé日" })
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using DataReader r = new(new MemoryStream(bytes));
            char[] buff = new char[10];
            int n = r.Read(buff, 0, 10);
            lines.Add(text.Length + " " + n + " " + HexChars(buff, Math.Max(n, 0)));
        }

        using DataReader truncated = new(new MemoryStream([0xc3]));
        char[] one = new char[1];
        lines.Add("truncated " + truncated.Read(one, 0, 1));

        using DataReader empty = new(new MemoryStream([]));
        lines.Add("empty " + empty.Read(one, 0, 1));

        return lines;
    }

    private static string HexChars(char[] chars, int length)
    {
        StringBuilder builder = new();
        for (int i = 0; i < length; i++)
        {
            _ = builder.Append(((int)chars[i]).ToString("x4"));
        }

        return builder.Length == 0 ? "-" : builder.ToString();
    }

    /// <summary>
    /// The harness reads what a snippet prints and compares it with ours, so the snippet has to
    /// print hex. Non-ASCII text would go through the console, and the console's idea of an
    /// encoding is whatever the machine says — the strings under test here are exactly the ones
    /// that would be mangled. The names are not <c>hex</c>: the preamble already declares a
    /// static <c>hex(String)</c>, and Java keeps methods and variables in different namespaces,
    /// so a local named hex would silently resolve to that one and fail on a byte[].
    /// </summary>
    private const string Setup = """
                      java.util.function.Function<byte[], String> bytesHex = b -> {
                        StringBuilder sb = new StringBuilder();
                        for (byte x : b) {
                          sb.append(String.format("%02x", x & 0xff));
                        }
                        return sb.toString();
                      };
                      java.util.function.BiFunction<byte[], Integer, String> bytesHexN = (b, n) -> {
                        StringBuilder sb = new StringBuilder();
                        for (int i = 0; i < n; i++) {
                          sb.append(String.format("%02x", b[i] & 0xff));
                        }
                        return sb.toString();
                      };
                      java.util.function.BiFunction<char[], Integer, String> charsHex = (chars, n) -> {
                        StringBuilder sb = new StringBuilder();
                        for (int i = 0; i < n; i++) {
                          sb.append(String.format("%04x", (int) chars[i]));
                        }
                        return sb.length() == 0 ? "-" : sb.toString();
                      };
            """;

    private static string Hex(byte[] bytes, int length)
    {
        StringBuilder builder = new();
        for (int i = 0; i < length; i++)
        {
            _ = builder.Append(bytes[i].ToString("x2"));
        }

        return builder.ToString();
    }
}
