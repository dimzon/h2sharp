/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct unit coverage for utility classes: SmallMap, CacheTQ, PropertiesFile,
 * DateTimeTemplate.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using H2Sharp.Message;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class UtilCoverageTests
{
    [Fact]
    public void SmallMapSmoke()
    {
        SmallMap map = new(4);
        map.AddObject(10, "a");
        map.AddObject(20, "b");
        Assert.Equal("a", map.GetObject(10, true));
        Assert.Equal("b", map.GetObject(20, true));
        map.FreeObject(10);
        Assert.Null(map.GetObject(10, true));
        Assert.NotNull(map.GetObject(20, true));
        for (int i = 0; i < 12; i++)
        {
            map.AddObject(100 + i, "v" + i);
        }

        Assert.NotNull(map.GetObject(111, true));
    }

    [Fact]
    public void CacheTqRoundTripsObjects()
    {
        var writer = new ListCacheWriter();
        Cache cache = CacheLRU.GetCache(writer, "TQ", 8);
        var objs = new CacheObj[20];
        for (int i = 0; i < objs.Length; i++)
        {
            objs[i] = new CacheObj(i);
            cache.Put(objs[i]);
        }

        for (int i = 0; i < objs.Length; i += 3)
        {
            CacheObject? found = cache.Get(i);
            if (found is not null)
            {
                Assert.Equal(i, found.GetPos());
            }
        }

        cache.Update(2, objs[2]);
        Assert.NotNull(cache.Find(2));
        cache.SetMaxMemory(1);
        _ = cache.GetMaxMemory();
        _ = cache.GetMemory();
        cache.Clear();
        Assert.Null(cache.Find(2));
    }

    [Fact]
    public void CacheLruEdgePaths()
    {
        var writer = new TracedCacheWriter();
        Cache cache = CacheLRU.GetCache(writer, "LRU", 1000);

        // Put twice: the second refuses instead of overwriting.
        CacheObj a = new(1) { Memory = 64 };
        cache.Put(a);
        Assert.Throws<Exception>(() => cache.Put(new CacheObj(1) { Memory = 64 }));

        // Update with a record that is not in the cache: the old one is null, so it is put.
        CacheObj fresh = new(2) { Memory = 64 };
        Assert.Null(cache.Update(2, fresh));

        // Update with the same instance on an LRU cache moves it to the front.
        _ = cache.Update(1, a);

        // GetAllChanged collects the changed records.
        CacheObj changed = new(3) { Memory = 64 };
        changed.SetChanged(true);
        cache.Put(changed);
        Assert.Contains(changed, cache.GetAllChanged());

        // Removing a position that is not there answers false.
        Assert.False(cache.Remove(9999));
        Assert.False(cache.Remove(10000));

        // An unremovable record blocks eviction: the removable ones are written back and
        // dropped, the scan reorders the stuck one, the writer's log is flushed once, and
        // the cache gives up, keeping the record. Needs more records than CacheMinRecords
        // (16) and a cache too small to hold them all.
        var smallWriter = new TracedCacheWriter();
        Cache stuck = CacheLRU.GetCache(smallWriter, "LRU", 4);
        CacheObj pinned = new(1) { Memory = 100, Removable = false };
        stuck.Put(pinned);
        for (int i = 2; i <= 20; i++)
        {
            stuck.Put(new CacheObj(i) { Memory = 100, Removable = true });
        }

        stuck.Put(new CacheObj(21) { Memory = 100, Removable = true });
    }

    [Fact]
    public void PropertiesFileLoadsJavaAndPlainContent()
    {
        const string java = "# comment\nname=value\nwith.dots = spaced\n";
        Dictionary<string, string> props = PropertiesFile.LoadJavaProperties(java);
        Assert.Equal("value", props["name"]);
        Assert.Equal("spaced", props["with.dots"]);

        Dictionary<string, string> lines = PropertiesFile.LoadPlainLines("a=1\nb=2\n");
        Assert.Equal("1", lines["a"]);
        Assert.Equal("2", lines["b"]);
        Assert.Equal("a:b", PropertiesFile.Unescape("a\\:b"));
        Assert.Equal("x", PropertiesFile.Unescape("x"));
    }

    [Fact]
    public void DateTimeTemplateFormatsAndParses()
    {
        var value = H2Sharp.Values.ValueTimestamp.Parse("2020-05-06 12:34:56", null);
        DateTimeTemplate template = DateTimeTemplate.Of("YYYY-MM-DD HH24:MI:SS");
        Assert.Equal("2020-05-06 12:34:56", template.Format(value));
        var parsed = template.Parse("2020-05-06 12:34:56",
            H2Sharp.Values.TypeInfo.TypeTimestamp, new TestCastDataProvider());
        Assert.NotNull(parsed);
        Assert.Equal(2020, H2Sharp.Util.DateTimeUtils.YearFromDateValue(
            ((H2Sharp.Values.ValueTimestamp)parsed).GetDateValue()));
    }

    private sealed class ListCacheWriter : CacheWriter
    {
        public void WriteBack(CacheObject entry)
        {
        }

        public void FlushLog()
        {
        }

        public Message.Trace GetTrace() => null!;
    }

    private sealed class TracedCacheWriter : CacheWriter
    {
        public void WriteBack(CacheObject entry)
        {
        }

        public void FlushLog()
        {
        }

        public Message.Trace GetTrace() => new(new NullTraceWriter(), "cache");
    }

    private sealed class NullTraceWriter : Message.TraceWriter
    {
        public void SetName(string n)
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

    private sealed class CacheObj : CacheObject
    {
        internal CacheObj(int pos) => SetPos(pos);

        public int Memory { get; set; } = 64;

        public bool Removable { get; set; } = true;

        public override int GetMemory() => Memory;

        public override bool CanRemove() => Removable;
    }
}
