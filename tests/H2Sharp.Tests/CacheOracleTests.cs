using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using H2Sharp.Message;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The cache family against the reference: what is kept, what is dropped, and what is written back.
/// </summary>
/// <remarks>
/// A cache here is where rows live between the table and the file, so the answers that matter are
/// operational rather than textual: which positions are still there after an eviction, in what order
/// the changed ones went back, and whether a read twice counts differently from a read once.
/// <para>
/// The part worth looking at is the part nobody specified: the least recently used half moves a
/// record to the front on read, the first-in-first-out half does not, and the two queue cache
/// promotes a row on its second consecutive read but not on two reads of the same thing back to back.
/// All three are behaviours, and a port that made them the same would pass any test that only put
/// things in and looked them up.
/// </para>
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class CacheOracleTests
{
    [Fact]
    public void CacheAgreesWithTheReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                Program(),
                numbers: null,
                extraJavaArgs: ["-Duser.language=en"],
                packageName: "org.h2.message")
            .ToList();

        List<string> csharp = [.. Lines()];

        Compare(java, csharp);
    }

    /// <summary>
    /// The reference program, generated into the message package because the null trace writer it
    /// needs is package-private there, and everything it calls on the cache is public.
    /// </summary>
    private static string Program() =>
        """
        {
        class R {
        static class NullWriter implements TraceWriter {
          public void setName(String n) { }
          public void write(int level, String module, String s, Throwable t) { }
          public void write(int level, int moduleId, String s, Throwable t) { }
          public boolean isEnabled(int level) { return false; }
        }

        static class W implements org.h2.util.CacheWriter {
          java.util.List<String> log = new java.util.ArrayList<>();
          Trace trace;

          W(TraceWriter tw) {
            trace = new Trace(tw, "cache");
          }

          public void writeBack(org.h2.util.CacheObject e) {
            log.add("wb:" + e.getPos());
          }

          public void flushLog() {
            log.add("flush");
          }

          public Trace getTrace() {
            return trace;
          }
        }

        static class Rec extends org.h2.util.CacheObject {
          int mem;
          boolean removable;

          Rec(int pos, int m, boolean changed, boolean r) {
            mem = m;
            removable = r;
            setPos(pos);
            setChanged(changed);
          }

          public boolean canRemove() {
            return removable;
          }

          public int getMemory() {
            return mem;
          }
        }

        static String safe(java.util.function.Supplier<String> body) {
          try {
            return body.get();
          } catch (RuntimeException e) {
            return "upd|THROWS";
          }
        }

        static String put(org.h2.util.Cache c, int pos, int mem, boolean changed, boolean removable) {
          try {
            c.put(new Rec(pos, mem, changed, removable));
            return "ok";
          } catch (RuntimeException e) {
            return "THROWS";
          }
        }

        static String show(org.h2.util.Cache c, int pos) {
          org.h2.util.CacheObject r = c.find(pos);
          return r == null ? "missing" : "mem" + r.getMemory() + (r.isChanged() ? "/changed" : "");
        }
        }

        R.NullWriter nw = new R.NullWriter();
        R.W w = new R.W(nw);

        org.h2.util.Cache lru = org.h2.util.CacheLRU.getCache(w, "LRU", 16);
        StringBuilder b = new StringBuilder();
        b.append(R.put(lru, 1, 100, false, true)).append('|');
        b.append(R.put(lru, 2, 100, true, true)).append('|');
        System.out.println("put|" + b + "|" + lru.getMemory() + "|" + R.show(lru, 1) + "|" + R.show(lru, 2));
        w.log.clear();
        for (int i = 3; i < 60; i++) {
          R.put(lru, i, 100, i % 3 == 0, true);
        }
        System.out.println("evict|" + lru.getMemory() + "|" + w.log + "|" + R.show(lru, 1) + "|" + R.show(lru, 2));

        org.h2.util.Cache fifo = org.h2.util.CacheLRU.getCache(w, "TQ", 16);
        w.log.clear();
        R.put(fifo, 10, 100, true, true);
        R.put(fifo, 11, 100, false, true);
        System.out.println("tq|" + fifo.getMemory() + "|" + R.show(fifo, 10));
        fifo.get(10);
        fifo.get(10);
        fifo.get(11);
        System.out.println("tqread|" + w.log);
        for (int i = 20; i < 60; i++) {
          R.put(fifo, i, 100, false, true);
        }
        System.out.println("tqevict|" + w.log + "|" + R.show(fifo, 10) + "|" + R.show(fifo, 11));

        org.h2.util.Cache soft = org.h2.util.CacheLRU.getCache(w, "SOFT_LRU", 16);
        R.put(soft, 100, 100, false, true);
        System.out.println("soft|" + R.show(soft, 100) + "|" + soft.getMemory());
        soft.remove(100);
        System.out.println("softrem|" + R.show(soft, 100));

        org.h2.util.Cache upd = org.h2.util.CacheLRU.getCache(w, "LRU", 64);
        R.put(upd, 5, 100, false, true);
        System.out.println(R.safe(() -> "upd|" + upd.update(5, new R.Rec(5, 200, true, true)).getMemory() + "|"
            + R.show(upd, 5)));
        try {
          upd.update(5, new R.Rec(5, 200, true, true));
          System.out.println("upddiff|no");
        } catch (RuntimeException e) {
          System.out.println("upddiff|THROWS");
        }
        System.out.println("allchanged|" + upd.getAllChanged().size());
        upd.setMaxMemory(128);
        System.out.println("maxmem|" + upd.getMaxMemory());

        Trace t = new Trace(nw, "cache");
        System.out.println("trace|" + t.isInfoEnabled() + "|" + t.isDebugEnabled());
        t.setLevel(2);
        System.out.println("trace|" + t.isInfoEnabled() + "|" + t.isDebugEnabled());
        t.setLevel(3);
        System.out.println("trace|" + t.isInfoEnabled() + "|" + t.isDebugEnabled());

        java.util.ArrayList<org.h2.expression.ParameterInterface> params = new java.util.ArrayList<>();
        System.out.println("params|" + Trace.formatParams(params));
        }
        """;

    private static IEnumerable<string> Lines()
    {
        NullTraceWriter nullWriter = new();
        TestWriter w = new(nullWriter);

        Cache lru = CacheLRU.GetCache(w, "LRU", 16);
        StringBuilder b = new();
        _ = b.Append(Put(lru, 1, 100, false, true)).Append('|');
        _ = b.Append(Put(lru, 2, 100, true, true)).Append('|');
        yield return "put|" + b + "|" + lru.GetMemory() + "|" + Show(lru, 1) + "|" + Show(lru, 2);
        w.Log.Clear();
        for (int i = 3; i < 60; i++)
        {
            _ = Put(lru, i, 100, i % 3 == 0, true);
        }

        yield return "evict|" + lru.GetMemory() + "|" + LogOf(w.Log) + "|" + Show(lru, 1)
            + "|" + Show(lru, 2);

        Cache fifo = CacheLRU.GetCache(w, "TQ", 16);
        w.Log.Clear();
        _ = Put(fifo, 10, 100, true, true);
        _ = Put(fifo, 11, 100, false, true);
        yield return "tq|" + fifo.GetMemory() + "|" + Show(fifo, 10);
        _ = fifo.Get(10);
        _ = fifo.Get(10);
        _ = fifo.Get(11);
        yield return "tqread|" + LogOf(w.Log);
        for (int i = 20; i < 60; i++)
        {
            _ = Put(fifo, i, 100, false, true);
        }

        yield return "tqevict|" + LogOf(w.Log) + "|" + Show(fifo, 10) + "|" + Show(fifo, 11);

        Cache soft = CacheLRU.GetCache(w, "SOFT_LRU", 16);
        _ = Put(soft, 100, 100, false, true);
        yield return "soft|" + Show(soft, 100) + "|" + soft.GetMemory();
        _ = soft.Remove(100);
        yield return "softrem|" + Show(soft, 100);

        Cache upd = CacheLRU.GetCache(w, "LRU", 64);
        _ = Put(upd, 5, 100, false, true);
        yield return Try(
            () => "upd|" + upd.Update(5, new Rec(5, 200, true, true))?.GetMemory() + "|" + Show(upd, 5),
            "upd|THROWS");
        string different;
        try
        {
            upd.Update(5, new Rec(5, 200, true, true));
            different = "no";
        }
        catch (Exception)
        {
            different = "THROWS";
        }

        yield return "upddiff|" + different;
        yield return "allchanged|" + upd.GetAllChanged().Count;
        upd.SetMaxMemory(128);
        yield return "maxmem|" + upd.GetMaxMemory();

        Trace t = new(nullWriter, "cache");
        yield return "trace|" + J(t.IsInfoEnabled()) + "|" + J(t.IsDebugEnabled());
        t.SetLevel(2);
        yield return "trace|" + J(t.IsInfoEnabled()) + "|" + J(t.IsDebugEnabled());
        t.SetLevel(3);
        yield return "trace|" + J(t.IsInfoEnabled()) + "|" + J(t.IsDebugEnabled());

        yield return "params|" + Trace.FormatParams([]);
    }

    private sealed class NullTraceWriter : TraceWriter
    {
        public void SetName(string name)
        {
        }

        public void Write(int level, string module, string s, Exception? t)
        {
        }

        public void Write(int level, int moduleId, string s, Exception? t)
        {
        }

        public bool IsEnabled(int level) => false;
    }

    private sealed class TestWriter(NullTraceWriter traceWriter) : CacheWriter
    {
        internal readonly List<string> Log = [];

        public void WriteBack(CacheObject entry) => Log.Add("wb:" + entry.GetPos().ToString(CultureInfo.InvariantCulture));

        public void FlushLog() => Log.Add("flush");

        public Trace GetTrace() => new(traceWriter, "cache");
    }

    private sealed class Rec(int pos, int memory, bool changed, bool removable)
        : CacheObject
    {
        public override bool CanRemove() => removable;

        public override int GetMemory() => memory;

        internal Rec WithPos()
        {
            SetPos(pos);
            SetChanged(changed);
            return this;
        }
    }

    private static string Put(Cache c, int pos, int memory, bool changed, bool removable)
    {
        try
        {
            Rec rec = new(pos, memory, changed, removable);
            rec.SetPos(pos);
            rec.SetChanged(changed);
            c.Put(rec);
            return "ok";
        }
        catch (Exception)
        {
            return "THROWS";
        }
    }

    private static string Show(Cache c, int pos)
    {
        CacheObject? r = c.Find(pos);
        return r is null ? "missing" : "mem" + r.GetMemory().ToString(CultureInfo.InvariantCulture)
            + (r.IsChanged() ? "/changed" : "");
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

    private static string LogOf(List<string> log) => "[" + string.Join(", ", log) + "]";

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
                + "\n    csharp: " + (first >= 0 ? csharp[first] : "-"));
        }

        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(java[i] == csharp[i], $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }
}
