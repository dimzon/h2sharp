/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct unit coverage for utility classes: SmallMap, CacheTQ, PropertiesFile,
 * DateTimeTemplate.
 */
using System.Collections.Generic;
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

    private sealed class CacheObj : CacheObject
    {
        internal CacheObj(int pos) => SetPos(pos);

        public override int GetMemory() => 64;

        public override bool CanRemove() => true;
    }
}
