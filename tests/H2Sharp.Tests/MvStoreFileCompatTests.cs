/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The Phase 2 gate: a store file written by H2Sharp opens in the reference H2, and a
 * file written by the reference opens here, with identical entries. Plain maps only —
 * no versioned (tx) maps, whose type registry deliberately uses .NET names and is not
 * cross-compatible (see MIGRATION_STATUS.md).
 */

using System;
using System.Collections.Generic;
using System.IO;
using H2Sharp.MvStore;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Cross-implementation file compatibility, both directions.
/// </summary>
/// <remarks>
/// Each direction dumps the same file on both sides and diffs the dumps: entries, in map
/// order, as hex, so that unicode and odd bytes compare exactly. Map iteration order is
/// part of the contract (same key comparator), so the dumps must match line for line.
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class MvStoreFileCompatTests
{
    [JavaOracleFact]
    public void H2SharpFileOpensInReference()
    {
        string fileName = TempFile();
        try
        {
            WriteCorpus(new MVStore.Builder().FileName(fileName).Open());
            List<string> csharp = DumpFile(fileName);
            List<string> java = OracleRunner.Run(
                [fileName],
                DumpProgram(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"]);
            Compare(java, csharp, "H2Sharp-written");
        }
        finally
        {
            DeleteFile(fileName);
        }
    }

    [JavaOracleFact]
    public void ReferenceFileOpensInH2Sharp()
    {
        string fileName = TempFile();
        try
        {
            List<string> java = OracleRunner.Run(
                [fileName],
                WriteProgram(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"]);
            List<string> csharp = DumpFile(fileName);
            Compare(java, csharp, "reference-written");
        }
        finally
        {
            DeleteFile(fileName);
        }
    }

    /// <summary>
    /// The corpus both writers produce: ints with removals, strings incl. unicode and a
    /// large value, and boundary longs, over several commits (several chunks).
    /// </summary>
    private static void WriteCorpus(MVStore store)
    {
        using (store)
        {
            MVMap<int, string> numbers = store.OpenMap<int, string>("numbers");
            for (int i = 0; i < 500; i++)
            {
                numbers.Put(i, "value-" + i);
            }

            store.Commit();
            for (int i = 0; i < 500; i += 2)
            {
                numbers.Remove(i);
            }

            store.Commit();

            MVMap<string, string> words = store.OpenMap<string, string>("words");
            words.Put("", "empty-key");
            words.Put("empty-value", "");
            words.Put("hello", "world");
            words.Put("héllo", "wörld");
            words.Put("arrow→here", "emoji😀end");
            words.Put("long", new string('x', 100000) + "tail");
            store.Commit();

            MVMap<long, long> counters = store.OpenMap<long, long>("counters");
            counters.Put(0L, 0L);
            counters.Put(-1L, -1L);
            counters.Put(1L << 40, 1L << 40);
            counters.Put(-(1L << 40), -(1L << 40));
            counters.Put(long.MaxValue, long.MaxValue);
            counters.Put(long.MinValue, long.MinValue);
            store.Commit();
        }
    }

    /// <summary>
    /// Dumps a file the way the Java side does: ==map== headers, then =hexkey==hexvalue
    /// lines in iteration order. Maps are opened with the types they were written with.
    /// </summary>
    private static List<string> DumpFile(string fileName)
    {
        var lines = new List<string>();
        using MVStore store = new MVStore.Builder().FileName(fileName).ReadOnly().Open();
        lines.Add("==numbers==");
        DumpMap(store.OpenMap<int, string>("numbers"), lines);
        lines.Add("==words==");
        DumpMap(store.OpenMap<string, string>("words"), lines);
        lines.Add("==counters==");
        DumpMap(store.OpenMap<long, long>("counters"), lines);
        return lines;
    }

    private static void DumpMap<K, V>(MVMap<K, V> map, List<string> lines)
    {
        foreach (KeyValuePair<K, V> entry in map.EntrySet())
        {
            string key = entry.Key?.ToString() ?? "<null>";
            string value = entry.Value?.ToString() ?? "<null>";
            lines.Add(OracleRunner.Hex(key) + "=" + OracleRunner.Hex(value));
        }
    }

    private static void Compare(List<string> java, List<string> csharp, string origin)
    {
        // 3 map headers + 250 numbers + 6 words + 6 counters = 265 lines. Anything less
        // means one side did not see the corpus at all, and an empty-equals-empty pass
        // would prove nothing.
        Assert.True(java.Count == 265, origin + ": java dump has " + java.Count + " lines");
        string expected = string.Join("\n", java);
        string actual = string.Join("\n", csharp);
        Assert.True(expected == actual,
            origin + " file dumps differ: java " + java.Count + " lines, csharp " + csharp.Count + " lines.\n"
            + FirstDifference(java, csharp));
    }

    private static string FirstDifference(List<string> java, List<string> csharp)
    {
        int count = Math.Min(java.Count, csharp.Count);
        for (int i = 0; i < count; i++)
        {
            if (java[i] != csharp[i])
            {
                return "first difference at line " + i + ":\njava:   " + java[i] + "\n"
                    + "csharp: " + csharp[i];
            }
        }

        return java.Count == csharp.Count ? "no differences" : "lengths differ";
    }

    /// <summary>
    /// Opens inputs[0] read-only and dumps it.
    /// </summary>
    private static string DumpProgram() =>
        """
        {
        String path = inputs[0];
        org.h2.mvstore.MVStore s = new org.h2.mvstore.MVStore.Builder().fileName(path).readOnly().open();
        try {
        BODY
        } finally {
          s.close();
        }
        }
        """.Replace("BODY", DumpProgramBody());

    private static string DumpProgramBody() =>
        """
        String[] names = { "numbers", "words", "counters" };
        for (String name : names) {
          System.out.println("==" + name + "==");
          org.h2.mvstore.MVMap<Object, Object> map = s.openMap(name);
          for (java.util.Map.Entry<Object, Object> e : map.entrySet()) {
            String k = String.valueOf(e.getKey());
            String v = String.valueOf(e.getValue());
            System.out.println(hex(k) + "=" + hex(v));
          }
        }
        """;

    /// <summary>
    /// Writes the corpus to inputs[0] and dumps it. Mirrors WriteCorpus entry for entry.
    /// </summary>
    private static string WriteProgram() =>
        """
        {
        String path = inputs[0];
        org.h2.mvstore.MVStore s = new org.h2.mvstore.MVStore.Builder().fileName(path).open();
        try {
          org.h2.mvstore.MVMap<Object, Object> numbers = s.openMap("numbers");
          for (int i = 0; i < 500; i++) {
            numbers.put(i, "value-" + i);
          }
          s.commit();
          for (int i = 0; i < 500; i += 2) {
            numbers.remove(i);
          }
          s.commit();
          org.h2.mvstore.MVMap<Object, Object> words = s.openMap("words");
          words.put("", "empty-key");
          words.put("empty-value", "");
          words.put("hello", "world");
          words.put("h\u00e9llo", "w\u00f6rld");
          words.put("arrow\u2192here", "emoji\ud83d\ude00end");
          words.put("long", new String(new char[100000]).replace("\0", "x") + "tail");
          s.commit();
          org.h2.mvstore.MVMap<Object, Object> counters = s.openMap("counters");
          counters.put(0L, 0L);
          counters.put(-1L, -1L);
          counters.put(1L << 40, 1L << 40);
          counters.put(-(1L << 40), -(1L << 40));
          counters.put(Long.MAX_VALUE, Long.MAX_VALUE);
          counters.put(Long.MIN_VALUE, Long.MIN_VALUE);
          s.commit();
        BODY
        } finally {
          s.close();
        }
        }
        """.Replace("BODY", DumpProgramBody());

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "h2sharp-gate-" + Guid.NewGuid().ToString("N") + ".h3");

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
