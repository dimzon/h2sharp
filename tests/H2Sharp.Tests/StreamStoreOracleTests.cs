using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using H2Sharp.MvStore;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The stream store against the reference: what an id looks like, and what reading it back gives.
/// </summary>
/// <remarks>
/// This is the format behind every BLOB and CLOB that does not fit in a value: the data goes into
/// blocks and the id is a description of where. So the id itself is the contract, and it is compared
/// byte for byte — an id that differs by one tag reads as a different stream.
/// <para>
/// The corpus is chosen so that each tag actually happens. A stream smaller than the block minimum
/// is inlined and starts with tag 0; one between the minimum and the maximum is a single block, tag 1;
/// one that needs several blocks ends up with an indirect id, tag 2, holding the id of the id. A
/// corpus of small streams would only ever see tag 0 and would say nothing about the other two.
/// </para>
/// <para>
/// Reading is checked against reading: bytes out, compared to the bytes that went in, plus the
/// answer for a stream that was skipped over partway and for one whose block is gone.
/// </para>
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class StreamStoreOracleTests
{
    [JavaOracleFact]
    public void StreamStoreAgreesWithTheReference()
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

    /// <summary>The reference program, in its own package for the package-private members.</summary>
    private static string Program() =>
        """
        {
        class R {
        static String bytes(byte[] b) {
          return org.h2.util.StringUtils.convertBytesToHex(b);
        }

        static String show(byte[] id) {
          return org.h2.mvstore.StreamStore.toString(id);
        }

        static String safeShow(byte[] id) {
          try {
            return show(id);
          } catch (RuntimeException e) {
            return "THROWS";
          }
        }

        static String safe(java.util.function.Supplier<String> body) {
          try {
            return body.get();
          } catch (RuntimeException e) {
            return "THROWS";
          }
        }

        static String read(org.h2.mvstore.StreamStore store, byte[] id) {
          java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
          byte[] buf = new byte[7];
          try (java.io.InputStream in = store.get(id)) {
            int len;
            while ((len = in.read(buf, 0, buf.length)) >= 0) {
              out.write(buf, 0, len);
            }
          } catch (java.io.IOException e) {
            return "IOERROR";
          }
          return bytes(out.toByteArray());
        }

        static String readSkipping(org.h2.mvstore.StreamStore store, byte[] id, long skip) {
          java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
          byte[] buf = new byte[5];
          try (java.io.InputStream in = store.get(id)) {
            long s = in.skip(skip);
            int len;
            while ((len = in.read(buf, 0, buf.length)) >= 0) {
              out.write(buf, 0, len);
            }
            return "skipped=" + s + "|" + bytes(out.toByteArray());
          } catch (java.io.IOException e) {
            return "IOERROR";
          }
        }
        }

        int[] sizes = { 0, 1, 5, 10, 255, 256, 257, 600, 1000, 2000, 5000 };
        for (int i = 0; i < sizes.length; i++) {
          java.util.Map<Long, byte[]> map = new java.util.HashMap<>();
          org.h2.mvstore.StreamStore store = new org.h2.mvstore.StreamStore(map, 8, 64, null);
          byte[] data = new byte[sizes[i]];
          for (int j = 0; j < sizes[i]; j++) {
            data[j] = (byte) (j % 251);
          }
          byte[] id = store.put(new java.io.ByteArrayInputStream(data));
          System.out.println("id" + sizes[i] + "|" + R.bytes(id) + "|" + R.safe(() -> R.show(id))
              + "|" + R.safe(() -> "" + store.length(id)) + "|" + R.safe(() -> "" + store.isInPlace(id))
              + "|" + R.safe(() -> "" + store.getMaxBlockKey(id)) + "|" + map.size()
              + "|" + R.read(store, id));
          System.out.println("keys" + sizes[i] + "|" + new java.util.TreeMap<>(map).keySet());
          System.out.println("skip" + sizes[i] + "|" + R.readSkipping(store, id, 3));
          store.remove(id);
          System.out.println("after" + sizes[i] + "|" + map.size());
        }

        // Keys already taken are skipped, and the search for a free one does not stop early.
        java.util.Map<Long, byte[]> map = new java.util.HashMap<>();
        map.put(0L, new byte[] { 1 });
        map.put(1L, new byte[] { 2 });
        map.put(2L, new byte[] { 3 });
        org.h2.mvstore.StreamStore store = new org.h2.mvstore.StreamStore(map, 8, 64, null);
        byte[] id = store.put(new java.io.ByteArrayInputStream(new byte[100]));
        System.out.println("skipfree|" + R.bytes(id) + "|" + new java.util.TreeMap<>(map).keySet());
        store.setNextKey(1);
        System.out.println("setkey|" + store.getNextKey());
        store.setNextKey(100);
        System.out.println("setkey|" + store.getNextKey() + "|" + store.getMinBlockSize() + "|"
            + store.getMaxBlockSize());

        // An id that is not one of the three kinds is refused rather than guessed at.
        try {
          store.length(new byte[] { 9 });
          System.out.println("badid|no");
        } catch (RuntimeException e) {
          System.out.println("badid|THROWS");
        }
        System.out.println("baddesc|" + R.safe(() -> R.show(new byte[] { 9, 0 })));
        }
        """;

    private static IEnumerable<string> Lines()
    {
        int[] sizes = [0, 1, 5, 10, 255, 256, 257, 600, 1000, 2000, 5000];
        foreach (int size in sizes)
        {
            Dictionary<long, byte[]> map = [];
            StreamStore store = new(map, 8, 64, null);
            byte[] data = new byte[size];
            for (int j = 0; j < size; j++)
            {
                data[j] = (byte)(j % 251);
            }

            using MemoryStream source = new(data, false);
            byte[] id = store.Put(source);

            // Each field is guarded separately, because the reference guards each separately too:
            // an id the describe walks off the end of does not stop the length from being asked for.
            yield return string.Join(
                "|",
                "id" + size.ToString(CultureInfo.InvariantCulture),
                Hex.BytesToHex(id),
                Try(() => StreamStore.Describe(id), "THROWS"),
                Try(() => store.Length(id).ToString(CultureInfo.InvariantCulture), "THROWS"),
                Try(() => J(store.IsInPlace(id)), "THROWS"),
                Try(() => store.GetMaxBlockKey(id).ToString(CultureInfo.InvariantCulture), "THROWS"),
                map.Count.ToString(CultureInfo.InvariantCulture),
                Read(store, id));

            yield return "keys" + size.ToString(CultureInfo.InvariantCulture) + "|" + KeyList(map);

            yield return "skip" + size.ToString(CultureInfo.InvariantCulture) + "|"
                + ReadAfterSkip(store, id, 3);

            store.Remove(id);
            yield return "after" + size.ToString(CultureInfo.InvariantCulture) + "|"
                + map.Count.ToString(CultureInfo.InvariantCulture);
        }

        Dictionary<long, byte[]> taken = new()
        {
            [0L] = [1],
            [1L] = [2],
            [2L] = [3],
        };
        StreamStore busy = new(taken, 8, 64, null);
        using (MemoryStream hundred = new(new byte[100], false))
        {
            byte[] id = busy.Put(hundred);
            yield return "skipfree|" + Hex.BytesToHex(id) + "|" + KeyList(taken);
        }

        busy.SetNextKey(1);
        yield return "setkey|" + busy.GetNextKey().ToString(CultureInfo.InvariantCulture);
        busy.SetNextKey(100);
        yield return "setkey|" + busy.GetNextKey().ToString(CultureInfo.InvariantCulture) + "|"
            + busy.GetMinBlockSize().ToString(CultureInfo.InvariantCulture) + "|"
            + busy.GetMaxBlockSize().ToString(CultureInfo.InvariantCulture);

        yield return "badid|" + Try(() => busy.Length([9]).ToString(CultureInfo.InvariantCulture), "THROWS");
        yield return "baddesc|" + Try(() => StreamStore.Describe([9, 0]), "THROWS");
    }

    private static string Read(StreamStore store, byte[] id)
    {
        using MemoryStream collected = new();
        using Stream source = store.Get(id);
        byte[] buffer = new byte[7];
        try
        {
            int len;
            while ((len = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                collected.Write(buffer, 0, len);
            }
        }
        catch (IOException)
        {
            return "IOERROR";
        }

        return Hex.BytesToHex(collected.ToArray());
    }

    private static string ReadAfterSkip(StreamStore store, byte[] id, long skip)
    {
        using MemoryStream collected = new();
        using Stream source = store.Get(id);
        byte[] buffer = new byte[5];
        try
        {
            long s = Skip(source, skip);
            int len;
            while ((len = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                collected.Write(buffer, 0, len);
            }

            return "skipped=" + s.ToString(CultureInfo.InvariantCulture) + "|"
                + Hex.BytesToHex(collected.ToArray());
        }
        catch (IOException)
        {
            return "IOERROR";
        }
    }

    /// <summary>
    /// The keys the way the reference prints them, in brackets and comma separated.
    /// </summary>
    private static string KeyList(IDictionary<long, byte[]> map)
        => "[" + string.Join(", ", map.Keys.Order().Select(static k => k.ToString(CultureInfo.InvariantCulture))) + "]";

    /// <summary>
    /// Skips by reading and discarding.    /// </summary>
    /// <remarks>
    /// The reference's stream overrides <c>skip</c> and can jump over a block without touching it,
    /// because its id is a description rather than a stream of bytes. <c>System.IO.Stream</c> has no
    /// <c>Skip</c> at all, so here it is read and discarded. The bytes that come out afterwards are
    /// the same either way, which is what the oracle compares.
    /// </remarks>
    private static long Skip(Stream source, long count)
    {
        byte[] discard = new byte[(int)Math.Min(count, 64)];
        long skipped = 0;
        while (skipped < count)
        {
            int len = source.Read(discard, 0, (int)Math.Min(discard.Length, count - skipped));
            if (len <= 0)
            {
                break;
            }

            skipped += len;
        }

        return skipped;
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
