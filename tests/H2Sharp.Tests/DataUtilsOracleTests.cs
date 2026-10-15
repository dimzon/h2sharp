using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using H2Sharp.MvStore;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The store's encodings against the reference: variable length numbers, the compact string form, the
/// packed page positions, the map text form and the checksum.
/// </summary>
/// <remarks>
/// These are the bytes on disk. A difference in any of them is not a rendering difference, it is a
/// store this port cannot read and the reference cannot read either way round, so every case is
/// compared at the byte level rather than by what it means.
/// <para>
/// The string encoding is deliberately hand written and deliberately limited: it counts characters,
/// not bytes, and does not carry anything outside the basic multilingual plane. An emoji is in the
/// corpus for that reason, and the answer is compared rather than assumed.
/// </para>
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class DataUtilsOracleTests
{
    private static readonly int[] Ints =
    [
        0, 1, 126, 127, 128, 129, 16383, 16384, 16385, 2097151, 2097152, 2097153,
        268435455, 268435456, 268435457, int.MaxValue, -1, -128, int.MinValue,
    ];

    private static readonly long[] Longs =
    [
        0, 1, 127, 128, 16383, 16384, 1099511627775L, 1099511627776L,
        long.MaxValue, -1, long.MinValue,
    ];

    private static readonly int[] Lengths =
    [
        0, 1, 31, 32, 33, 63, 64, 65, 127, 128, 255, 256, 1023, 1024, 4096,
        65535, 65536, 1048575, 1048576, 16777215, 16777216, 16777216 * 16, 1 << 28,
    ];

    private static readonly string[] Strings =
    [
        "",
        "a",
        "name.TEST",
        "map.TEST",
        "chunk.0",
        "root.1",
        "line1\nline2",
        "tab\there",
        "привет",
        "€uro",
        "🎉",
        "a🎉b",
    ];

    /// <summary>
    /// One Integer.decode for the whole project: the same text through Utils.TryParseInt and
    /// through DataUtils.GetConfigParam must answer the same, and both must answer what the JDK
    /// answers — including the two places the old copies diverged ("0" threw on one road, "#..."
    /// on the other) and the JDK quirks (a lone "0" stays decimal, "-2147483648" refuses).
    /// </summary>
    [JavaOracleFact]
    public void DecodeIntegersAgreeWithTheReference()
    {
        string[] corpus =
        [
            "0", "010", "0x1f", "-0x1f", "#10", "-#10", "+0x1f", "+010", "-0", "00",
            "08", "0x", "#", "-", "+", "--1", "0x80000000", "-0x80000000", "2147483647", "2147483648",
            "-2147483648", " 10", "10 ", "42x", string.Empty,
        ];
        string body = """
            for (String s : inputs) {
                try {
                    System.out.println(Integer.decode(s));
                } catch (NumberFormatException e) {
                    System.out.println("ERR");
                }
            }
            """;

        List<string> java = OracleRunner.Run(
            corpus,
            body,
            numbers: null,
            extraJavaArgs: ["-Duser.language=en"],
            packageName: null);

        List<string> csharp = [];
        foreach (string s in corpus)
        {
            bool parsed = Utils.TryParseInt(s, out int value);
            csharp.Add(parsed ? value.ToString(CultureInfo.InvariantCulture) : "ERR");

            // The same config value through the store's road answers the same.
            Dictionary<string, object?> config = new() { ["v"] = s };
            Assert.Equal(
                parsed ? value : -1,
                DataUtils.GetConfigParam(config, "v", -1));
        }

        Assert.Equal(java, csharp);
    }

    [Fact]
    public void EncodingsAgreeWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Program(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"],
                packageName: "org.h2.mvstore")
            .ToList();

        List<string> csharp = [];
        csharp.AddRange(IntLines());
        csharp.AddRange(LongLines());
        csharp.AddRange(StringLines());
        csharp.AddRange(LengthLines());
        csharp.AddRange(PageLines());
        csharp.AddRange(MapLines());
        csharp.AddRange(MiscLines());

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

    /// <summary>The reference program, generated into its own package for the package-private calls.</summary>
    private static string Program() =>
        """
        {
        class R {
        static String hex(byte[] b) {
          return org.h2.util.StringUtils.convertBytesToHex(b);
        }

        static void safe(Runnable body) {
          try {
            body.run();
          } catch (Throwable e) {
            System.out.println("THROWS");
          }
        }

        static byte[] concat(byte[] a, byte[] b) {
          byte[] r = new byte[a.length + b.length];
          System.arraycopy(a, 0, r, 0, a.length);
          System.arraycopy(b, 0, r, a.length, b.length);
          return r;
        }
        }

        int[] ints = { 0, 1, 126, 127, 128, 129, 16383, 16384, 16385, 2097151, 2097152,
            2097153, 268435455, 268435456, 268435457, 2147483647, -1, -128, -2147483648 };
        for (int i = 0; i < ints.length; i++) {
          java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
          DataUtils.writeVarInt(out, ints[i]);
          byte[] b = out.toByteArray();
          System.out.println(ints[i] + "|" + DataUtils.getVarIntLen(ints[i]) + "|" + b.length + "|"
              + R.hex(b) + "|" + DataUtils.readVarInt(java.nio.ByteBuffer.wrap(b)));
        }

        long[] longs = { 0, 1, 127, 128, 16383, 16384, 1099511627775L, 1099511627776L,
            9223372036854775807L, -1, -9223372036854775808L };
        for (int i = 0; i < longs.length; i++) {
          java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
          DataUtils.writeVarLong(out, longs[i]);
          byte[] b = out.toByteArray();
          System.out.println(longs[i] + "|" + DataUtils.getVarLongLen(longs[i]) + "|" + b.length + "|"
              + R.hex(b) + "|" + DataUtils.readVarLong(java.nio.ByteBuffer.wrap(b)));
        }

        String[] strings = { "", "a", "name.TEST", "map.TEST", "chunk.0", "root.1",
            "line1\nline2", "tab\there", "привет", "€uro",
            "🎉", "a🎉b" };
        for (int i = 0; i < strings.length; i++) {
          String s = strings[i];
          java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
          DataUtils.writeVarInt(out, s.length());
          byte[] head = out.toByteArray();
          byte[] body = new byte[s.length() * 3 + 16];
          java.nio.ByteBuffer buff = java.nio.ByteBuffer.wrap(body);
          DataUtils.writeStringData(buff, s, s.length());
          buff.flip();
          byte[] written = new byte[buff.limit()];
          buff.get(written);
          String back = DataUtils.readString(java.nio.ByteBuffer.wrap(R.concat(head, written)));
          System.out.println(s.length() + "|" + head.length + "|" + R.hex(head) + "|" + R.hex(written) + "|"
              + R.hex(back.getBytes(java.nio.charset.StandardCharsets.UTF_8)));
        }

        int[] lengths = { 0, 1, 31, 32, 33, 63, 64, 65, 127, 128, 255, 256, 1023, 1024, 4096,
            65535, 65536, 1048575, 1048576, 16777215, 16777216, 16777216 * 16, 1 << 28 };
        for (int i = 0; i < lengths.length; i++) {
          int len = lengths[i];
          int code = DataUtils.encodeLength(len);
          System.out.println(len + "|" + code + "|" + DataUtils.decodePageLength(code) + "|"
              + (DataUtils.decodePageLength(code) >= len));
        }

        int[][] parts = { {0, 0, 0, 0}, {1, 2, 3, 0}, {1, 2, 3, 1}, {7, 64, 100, 1}, {255, 65536, 4096, 0} };
        for (int i = 0; i < parts.length; i++) {
          long pos = DataUtils.composePagePos(parts[i][0], parts[i][1], parts[i][2], parts[i][3]);
          System.out.println(pos + "|" + DataUtils.getPageChunkId(pos) + "|" + DataUtils.getPageOffset(pos) + "|"
              + DataUtils.getPageType(pos) + "|" + DataUtils.isLeafPosition(pos) + "|"
              + DataUtils.isPageSaved(pos) + "|" + DataUtils.isPageRemoved(pos) + "|"
              + DataUtils.getPageMaxLength(pos));
          long toc = DataUtils.composeTocElement(parts[i][0], parts[i][1], parts[i][2], parts[i][3]);
          System.out.println(toc + "|" + DataUtils.getPageMapId(toc) + "|" + DataUtils.getPageOffset(toc) + "|"
              + DataUtils.getPageType(toc));
        }
        System.out.println(DataUtils.composePagePos(3, 0x3FFFFFFFFFL));

        for (int i = 0; i <= 32; i++) {
          System.out.println("len" + i + "|" + DataUtils.decodePageLength(i));
        }

        java.util.HashMap<String, String> map = new java.util.HashMap<>();
        map.put("name", "TEST");
        map.put("a", "1");
        map.put("comma", "x,y");
        map.put("quote", "he said \"hi\"");
        map.put("both", "x,\"y\"");
        String text = DataUtils.appendMap(new StringBuilder(), map).toString();
        System.out.println(text);
        System.out.println(new java.util.TreeMap<>(DataUtils.parseMap(text)));
        System.out.println(DataUtils.getMapName(text));
        System.out.println(DataUtils.getFromMap(text, "name"));
        System.out.println(DataUtils.getFromMap(text, "comma"));
        System.out.println(DataUtils.getFromMap(text, "quote"));
        System.out.println(DataUtils.getFromMap(text, "missing"));
        R.safe(() -> System.out.println(DataUtils.getFromMap("nocolon", "x")));

        java.util.HashMap<String, Object> mixed = new java.util.HashMap<>();
        mixed.put("n", 42);
        mixed.put("l", 42L);
        mixed.put("s", "2a");
        System.out.println(DataUtils.readHexLong(mixed, "l", -1));
        System.out.println(DataUtils.readHexLong(mixed, "s", -1));
        System.out.println(DataUtils.readHexLong(mixed, "absent", -1));
        System.out.println(DataUtils.readHexInt(mixed, "n", -1));
        System.out.println(DataUtils.readHexInt(mixed, "s", -1));
        System.out.println(DataUtils.readHexInt(mixed, "absent", -1));
        try { System.out.println(DataUtils.parseHexBytes(mixed, "n")); } catch (RuntimeException e) { System.out.println("THROWS"); }
        R.safe(() -> System.out.println(DataUtils.getConfigParam(mixed, "n", -1)));
        R.safe(() -> System.out.println(DataUtils.getConfigParam(mixed, "s", -1)));
        R.safe(() -> System.out.println(DataUtils.getConfigParam(mixed, "absent", -1)));

        for (int i = 0; i < 5; i++) {
          System.out.println("cv" + i + "|" + DataUtils.getCheckValue(i * 1234567));
        }

        byte[] data = "hello, store".getBytes(java.nio.charset.StandardCharsets.ISO_8859_1);
        for (int len = 0; len <= data.length; len++) {
          System.out.println("fl" + len + "|" + DataUtils.getFletcher32(data, 0, len));
        }

        System.out.println(DataUtils.parseHexLong("2a"));
        System.out.println(DataUtils.parseHexLong("ffffffffffffffff"));
        System.out.println(DataUtils.parseHexInt("7fffffff"));
        R.safe(() -> System.out.println(DataUtils.parseHexInt("zz")));
        R.safe(() -> System.out.println(DataUtils.parseHexLong("zz")));

        System.out.println(DataUtils.formatMessage(0, "plain"));
        System.out.println(DataUtils.formatMessage(3, "one {0} two {1}", "a", 7));
        System.out.println(DataUtils.newMVStoreException(6, "bad {0}", "thing").getMessage());
        System.out.println(DataUtils.newMVStoreException(6, "bad {0}", "thing").getErrorCode());
        try {
          DataUtils.newMVStoreException(6, "bad {0}", "thing");
        } catch (RuntimeException e) {
          System.out.println("threw");
        }

        Integer[] src = { 10, 11, 12, 13, 14 };
        Integer[] gap = new Integer[6];
        DataUtils.copyWithGap(src, gap, src.length, 2);
        System.out.println(java.util.Arrays.toString(gap));
        Integer[] except = new Integer[4];
        DataUtils.copyExcept(src, except, src.length, 2);
        System.out.println(java.util.Arrays.toString(except));
        }
        """;

    private static IEnumerable<string> IntLines()
    {
        foreach (int x in Ints)
        {
            using MemoryStream buffer = new();
            DataUtils.WriteVarInt(buffer, x);
            byte[] b = buffer.ToArray();
            int position = 0;
            yield return string.Join(
                "|",
                x.ToString(CultureInfo.InvariantCulture),
                DataUtils.GetVarIntLen(x).ToString(CultureInfo.InvariantCulture),
                b.Length.ToString(CultureInfo.InvariantCulture),
                Hex.BytesToHex(b),
                DataUtils.ReadVarInt(b, ref position).ToString(CultureInfo.InvariantCulture));
        }
    }

    private static IEnumerable<string> LongLines()
    {
        foreach (long x in Longs)
        {
            using MemoryStream buffer = new();
            DataUtils.WriteVarLong(buffer, x);
            byte[] b = buffer.ToArray();
            int position = 0;
            yield return string.Join(
                "|",
                x.ToString(CultureInfo.InvariantCulture),
                DataUtils.GetVarLongLen(x).ToString(CultureInfo.InvariantCulture),
                b.Length.ToString(CultureInfo.InvariantCulture),
                Hex.BytesToHex(b),
                DataUtils.ReadVarLong(b, ref position).ToString(CultureInfo.InvariantCulture));
        }
    }

    private static IEnumerable<string> StringLines()
    {
        foreach (string s in Strings)
        {
            byte[] head = [];
            using (MemoryStream buffer = new())
            {
                DataUtils.WriteVarInt(buffer, s.Length);
                head = buffer.ToArray();
            }

            byte[] body = new byte[(s.Length * 3) + 16];
            int position = 0;
            DataUtils.WriteStringData(body, ref position, s, s.Length);
            byte[] written = body[..position];
            byte[] whole = [.. head, .. written];
            int at = 0;
            string read = Try(
                () => DataUtils.ReadString(whole, ref at),
                "THROWS");
            yield return string.Join(
                "|",
                s.Length.ToString(CultureInfo.InvariantCulture),
                head.Length.ToString(CultureInfo.InvariantCulture),
                Hex.BytesToHex(head),
                Hex.BytesToHex(written),
                Try(() => Hex.BytesToHex(Encoding.UTF8.GetBytes(read)), "THROWS"));
        }
    }

    private static IEnumerable<string> LengthLines()
    {
        foreach (int len in Lengths)
        {
            int code = DataUtils.EncodeLength(len);
            int decoded = DataUtils.DecodePageLength(code);
            yield return string.Join(
                "|",
                len.ToString(CultureInfo.InvariantCulture),
                code.ToString(CultureInfo.InvariantCulture),
                decoded.ToString(CultureInfo.InvariantCulture),
                J(decoded >= len));
        }
    }

    private static IEnumerable<string> PageLines()
    {
        int[][] parts = [[0, 0, 0, 0], [1, 2, 3, 0], [1, 2, 3, 1], [7, 64, 100, 1], [255, 65536, 4096, 0]];
        foreach (int[] part in parts)
        {
            long pos = DataUtils.ComposePagePos(part[0], part[1], part[2], part[3]);
            yield return string.Join(
                "|",
                L(pos),
                DataUtils.GetPageChunkId(pos).ToString(CultureInfo.InvariantCulture),
                DataUtils.GetPageOffset(pos).ToString(CultureInfo.InvariantCulture),
                DataUtils.GetPageType(pos).ToString(CultureInfo.InvariantCulture),
                J(DataUtils.IsLeafPosition(pos)),
                J(DataUtils.IsPageSaved(pos)),
                J(DataUtils.IsPageRemoved(pos)),
                DataUtils.GetPageMaxLength(pos).ToString(CultureInfo.InvariantCulture));

            long toc = DataUtils.ComposeTocElement(part[0], part[1], part[2], part[3]);
            yield return string.Join(
                "|",
                L(toc),
                DataUtils.GetPageMapId(toc).ToString(CultureInfo.InvariantCulture),
                DataUtils.GetPageOffset(toc).ToString(CultureInfo.InvariantCulture),
                DataUtils.GetPageType(toc).ToString(CultureInfo.InvariantCulture));
        }

        yield return L(DataUtils.ComposePagePos(3, 0x3FFFFFFFFFL));

        for (int i = 0; i <= 32; i++)
        {
            yield return "len" + i.ToString(CultureInfo.InvariantCulture) + "|"
                + DataUtils.DecodePageLength(i).ToString(CultureInfo.InvariantCulture);
        }
    }

    private static IEnumerable<string> MapLines()
    {
        Dictionary<string, object?> map = new()
        {
            ["name"] = "TEST",
            ["a"] = "1",
            ["comma"] = "x,y",
            ["quote"] = "he said \"hi\"",
            ["both"] = "x,\"y\"",
        };
        string text = DataUtils.AppendMap(new StringBuilder(), map).ToString();
        yield return text;

        // The reference prints a java.util.Map with Map.toString, which is braces, a comma and a
        // space between entries. The C# side builds the same text by hand rather than relying on an
        // implementation detail of its own dictionary type.
        yield return "{" + string.Join(
            ", ",
            DataUtils.ParseMap(text).OrderBy(static p => p.Key, StringComparer.Ordinal)
                .Select(static p => p.Key + "=" + p.Value)) + "}";
        yield return DataUtils.GetMapName(text) ?? "null";
        yield return DataUtils.GetFromMap(text, "name") ?? "null";
        yield return DataUtils.GetFromMap(text, "comma") ?? "null";
        yield return DataUtils.GetFromMap(text, "quote") ?? "null";
        yield return DataUtils.GetFromMap(text, "missing") ?? "null";
        yield return Try(() => DataUtils.GetFromMap("nocolon", "x") ?? "null", "THROWS");

        Dictionary<string, object?> mixed = new()
        {
            ["n"] = 42,
            ["l"] = 42L,
            ["s"] = "2a",
        };
        yield return L(DataUtils.ReadHexLong(mixed, "l", -1));
        yield return Try(() => L(DataUtils.ReadHexLong(mixed, "s", -1)), "THROWS");
        yield return L(DataUtils.ReadHexLong(mixed, "absent", -1));
        yield return DataUtils.ReadHexInt(mixed, "n", -1).ToString(CultureInfo.InvariantCulture);
        yield return DataUtils.ReadHexInt(mixed, "s", -1).ToString(CultureInfo.InvariantCulture);
        yield return DataUtils.ReadHexInt(mixed, "absent", -1).ToString(CultureInfo.InvariantCulture);
        yield return Try(() => DataUtils.ParseHexBytes(mixed, "n") is null ? "null" : "bytes", "THROWS");
        yield return DataUtils.GetConfigParam(mixed, "n", -1).ToString(CultureInfo.InvariantCulture);
        yield return DataUtils.GetConfigParam(mixed, "s", -1).ToString(CultureInfo.InvariantCulture);
        yield return DataUtils.GetConfigParam(mixed, "absent", -1).ToString(CultureInfo.InvariantCulture);
    }

    private static IEnumerable<string> MiscLines()
    {
        for (int i = 0; i < 5; i++)
        {
            yield return "cv" + i.ToString(CultureInfo.InvariantCulture) + "|"
                + DataUtils.GetCheckValue(i * 1234567).ToString(CultureInfo.InvariantCulture);
        }

        byte[] data = Encoding.Latin1.GetBytes("hello, store");
        for (int len = 0; len <= data.Length; len++)
        {
            yield return "fl" + len.ToString(CultureInfo.InvariantCulture) + "|"
                + DataUtils.GetFletcher32(data, 0, len).ToString(CultureInfo.InvariantCulture);
        }

        yield return L(DataUtils.ParseHexLong("2a"));
        yield return L(DataUtils.ParseHexLong("ffffffffffffffff"));
        yield return DataUtils.ParseHexInt("7fffffff").ToString(CultureInfo.InvariantCulture);
        yield return Try(() => DataUtils.ParseHexInt("zz").ToString(CultureInfo.InvariantCulture), "THROWS");
        yield return Try(() => L(DataUtils.ParseHexLong("zz")), "THROWS");

        yield return DataUtils.FormatMessage(0, "plain");
        yield return DataUtils.FormatMessage(3, "one {0} two {1}", "a", 7);
        yield return DataUtils.NewMvStoreException(6, "bad {0}", "thing").Message;
        yield return DataUtils.NewMvStoreException(6, "bad {0}", "thing").GetErrorCode().ToString(CultureInfo.InvariantCulture);

        // The reference uses Integer[], whose unused slot prints as null. An int[] would print 0 there and
        // the comparison would be about the corpus rather than about the port, so the element type matches.
        int?[] src = [10, 11, 12, 13, 14];
        int?[] gap = new int?[6];
        DataUtils.CopyWithGap(src, gap, src.Length, 2);
        yield return "[" + string.Join(", ", gap.Select(static v => v?.ToString(CultureInfo.InvariantCulture) ?? "null")) + "]";

        int?[] except = new int?[4];
        DataUtils.CopyExcept(src, except, src.Length, 2);
        yield return "[" + string.Join(", ", except.Select(static v => v?.ToString(CultureInfo.InvariantCulture) ?? "null")) + "]";
    }

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

    private static string L(long value) => value.ToString(CultureInfo.InvariantCulture);
}
