using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.MvStore;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;
using StoreType = H2Sharp.MvStore.Type;

namespace H2Sharp.Tests;

/// <summary>
/// The store's data types against the reference.
/// </summary>
/// <remarks>
/// Every page in the store is written through one of these, so a difference here is a difference in
/// the bytes on disk rather than in how something is displayed. Each type is checked for the bytes it
/// writes, the value it reads back out of them, and what it says about memory, which the page tree
/// divides by when it decides where to split a page.
/// <para>
/// The binary search is checked with the cached index as well as without, because the cache starts at
/// zero and the first search therefore has to fall back to the middle. A search that only ever got
/// the cache right would pass every real run and fail the first time it saw a new page.
/// </para>
/// <para>
/// This is the store's own data type, the one <c>mvstore.type</c> holds. The catalogue of column
/// types that values declare themselves with is a different thing entirely and has its own oracle.
/// </para>
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class MvStoreDataTypeOracleTests
{
    private static readonly int[] Ints =
    [
        0, 1, -1, 127, 128, -128, 16383, 16384, 2097151, 2097152, 268435455, 268435456,
        int.MaxValue, int.MinValue,
    ];

    private static readonly long[] Longs =
    [
        0, 1, -1, 127, 128, long.MaxValue, long.MinValue, 1099511627776L, -1099511627776L,
    ];

    private static readonly string[] Strings =
    [
        "", "a", "table", "TABLE", "привет", "€uro", "line1\nline2",
    ];

    private static readonly byte[][] ByteArrays =
    [
        [], [0], [1, 2, 3], [0, 0, 1], [255, 254],
    ];

    [Fact]
    public void DataTypesAgreeWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Program(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"],
                packageName: "org.h2.mvstore.type")
            .ToList();

        List<string> csharp = [.. Lines()];

        Compare(java, csharp);
    }

    private static string Program() =>
        """
        {
        class R {
        static String hex(byte[] b) {
          return org.h2.util.StringUtils.convertBytesToHex(b);
        }
        }

        org.h2.mvstore.type.IntegerDataType it = org.h2.mvstore.type.IntegerDataType.INSTANCE;
        org.h2.mvstore.type.LongDataType lt = org.h2.mvstore.type.LongDataType.INSTANCE;
        org.h2.mvstore.type.StringDataType st = org.h2.mvstore.type.StringDataType.INSTANCE;
        org.h2.mvstore.type.ByteArrayDataType bt = org.h2.mvstore.type.ByteArrayDataType.INSTANCE;

        int[] ints = { 0, 1, -1, 127, 128, -128, 16383, 16384, 2097151, 2097152, 268435455, 268435456,
            2147483647, -2147483648 };
        for (int i = 0; i < ints.length; i++) {
          org.h2.mvstore.WriteBuffer b = new org.h2.mvstore.WriteBuffer(8);
          it.write(b, ints[i]);
          byte[] all = new byte[b.position()];
          b.position(0);
          b.get(all);
          System.out.println("int|" + ints[i] + "|" + R.hex(all) + "|" + it.getMemory(ints[i]));
        }

        long[] longs = { 0, 1, -1, 127, 128, 9223372036854775807L, -9223372036854775808L,
            1099511627776L, -1099511627776L };
        for (int i = 0; i < longs.length; i++) {
          org.h2.mvstore.WriteBuffer b = new org.h2.mvstore.WriteBuffer(8);
          lt.write(b, longs[i]);
          byte[] all = new byte[b.position()];
          b.position(0);
          b.get(all);
          System.out.println("long|" + longs[i] + "|" + R.hex(all) + "|" + lt.getMemory(longs[i]));
        }

        String[] strings = { "", "a", "table", "TABLE", "привет", "€uro", "line1\nline2" };
        for (int i = 0; i < strings.length; i++) {
          org.h2.mvstore.WriteBuffer b = new org.h2.mvstore.WriteBuffer(8);
          st.write(b, strings[i]);
          byte[] all = new byte[b.position()];
          b.position(0);
          b.get(all);
          System.out.println("string|" + strings[i].length() + "|" + R.hex(all) + "|" + st.getMemory(strings[i]));
        }

        byte[][] bytes = { {}, { 0 }, { 1, 2, 3 }, { 0, 0, 1 }, { (byte) 255, (byte) 254 } };
        for (int i = 0; i < bytes.length; i++) {
          org.h2.mvstore.WriteBuffer b = new org.h2.mvstore.WriteBuffer(8);
          bt.write(b, bytes[i]);
          byte[] all = new byte[b.position()];
          b.position(0);
          b.get(all);
          System.out.println("bytes|" + bytes[i].length + "|" + R.hex(all) + "|" + bt.getMemory(bytes[i]));
        }

        Integer[] ordered = { 5, 10, 20, 30 };
        int[] keys = { 0, 5, 10, 15, 20, 25, 30, 40 };
        for (int i = 0; i < keys.length; i++) {
          System.out.println("isearch|" + keys[i] + "|" + it.binarySearch(keys[i], ordered, 4, 0)
              + "|" + it.binarySearch(keys[i], ordered, 4, 2));
        }

        String[] orderedS = { "a", "b", "c", "d" };
        String[] keysS = { "a", "b", "c", "d", "e", "0" };
        for (int i = 0; i < keysS.length; i++) {
          System.out.println("ssearch|" + keysS[i] + "|" + st.binarySearch(keysS[i], orderedS, 4, 0));
        }

        byte[][] orderedB = { { 1 }, { 2 }, { 3 } };
        for (int i = 0; i < 4; i++) {
          System.out.println("bsearch|" + i + "|"
              + bt.binarySearch(new byte[] { (byte) i }, orderedB, 3, 0));
        }

        System.out.println("storage|" + it.createStorage(0).length + "|"
            + it.createStorage(3).length + "|" + lt.createStorage(2).length + "|"
            + st.createStorage(4).length + "|" + bt.createStorage(5).length);
        }
        """;

    private static IEnumerable<string> Lines()
    {
        foreach (int x in Ints)
        {
            WriteBuffer b = new(8);
            StoreType.IntegerDataType.Instance.Write(b, x);
            yield return "int|" + x.ToString(CultureInfo.InvariantCulture) + "|" + Hex.BytesToHex(b.ToArray()) + "|"
                + StoreType.IntegerDataType.Instance.GetMemory(x).ToString(CultureInfo.InvariantCulture);
        }

        foreach (long x in Longs)
        {
            WriteBuffer b = new(8);
            StoreType.LongDataType.Instance.Write(b, x);
            yield return "long|" + x.ToString(CultureInfo.InvariantCulture) + "|"
                + Hex.BytesToHex(b.ToArray()) + "|"
                + StoreType.LongDataType.Instance.GetMemory(x).ToString(CultureInfo.InvariantCulture);
        }

        foreach (string s in Strings)
        {
            WriteBuffer b = new(8);
            StoreType.StringDataType.Instance.Write(b, s);
            yield return "string|" + s.Length.ToString(CultureInfo.InvariantCulture) + "|"
                + Hex.BytesToHex(b.ToArray()) + "|"
                + StoreType.StringDataType.Instance.GetMemory(s).ToString(CultureInfo.InvariantCulture);
        }

        foreach (byte[] data in ByteArrays)
        {
            WriteBuffer b = new(8);
            StoreType.ByteArrayDataType.Instance.Write(b, data);
            yield return "bytes|" + data.Length.ToString(CultureInfo.InvariantCulture) + "|"
                + Hex.BytesToHex(b.ToArray()) + "|"
                + StoreType.ByteArrayDataType.Instance.GetMemory(data).ToString(CultureInfo.InvariantCulture);
        }

        int[] ordered = [5, 10, 20, 30];
        foreach (int key in (int[])[0, 5, 10, 15, 20, 25, 30, 40])
        {
            yield return "isearch|" + key.ToString(CultureInfo.InvariantCulture) + "|"
                + StoreType.IntegerDataType.Instance.BinarySearch(key, ordered, 4, 0).ToString(CultureInfo.InvariantCulture)
                + "|" + StoreType.IntegerDataType.Instance.BinarySearch(key, ordered, 4, 2).ToString(CultureInfo.InvariantCulture);
        }

        string[] orderedS = ["a", "b", "c", "d"];
        foreach (string key in (string[])["a", "b", "c", "d", "e", "0"])
        {
            yield return "ssearch|" + key + "|"
                + StoreType.StringDataType.Instance.BinarySearch(key, orderedS, 4, 0).ToString(CultureInfo.InvariantCulture);
        }

        byte[][] orderedB = [[1], [2], [3]];
        for (int i = 0; i < 4; i++)
        {
            yield return "bsearch|" + i.ToString(CultureInfo.InvariantCulture) + "|"
                + StoreType.ByteArrayDataType.Instance.BinarySearch([(byte)i], orderedB, 3, 0)
                    .ToString(CultureInfo.InvariantCulture);
        }

        yield return "storage|"
            + StoreType.IntegerDataType.Instance.CreateStorage(0).Length.ToString(CultureInfo.InvariantCulture) + "|"
            + StoreType.IntegerDataType.Instance.CreateStorage(3).Length.ToString(CultureInfo.InvariantCulture) + "|"
            + StoreType.LongDataType.Instance.CreateStorage(2).Length.ToString(CultureInfo.InvariantCulture) + "|"
            + StoreType.StringDataType.Instance.CreateStorage(4).Length.ToString(CultureInfo.InvariantCulture) + "|"
            + StoreType.ByteArrayDataType.Instance.CreateStorage(5).Length.ToString(CultureInfo.InvariantCulture);
    }

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
