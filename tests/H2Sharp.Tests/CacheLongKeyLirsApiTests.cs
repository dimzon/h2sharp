/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 97: the LIRS cache as a map — entry/key/value views, the get/put/remove
 * contract, the counters, and the non-resident queue a small cache produces.
 * TestCacheLirs drives the eviction behaviour through a subclass; these calls
 * are the public API around it, including the base SizeOf that a subclass hides.
 */
using System.Collections.Generic;
using System.Linq;
using H2Sharp.MvStore.Cache;
using Xunit;

namespace H2Sharp.Tests;

public sealed class CacheLongKeyLirsApiTests
{
    private static CacheLongKeyLIRS<string> NewCache(long maxMemory)
        => new(new CacheLongKeyLIRS<string>.Config { MaxMemory = maxMemory, SegmentCount = 1 });

    [Fact]
    public void MapViewReflectsPutsAndGets()
    {
        CacheLongKeyLIRS<string> cache = NewCache(1_000_000);
        Assert.True(cache.IsEmpty());
        cache.Put(1, "a");
        Assert.Equal("a", cache.Put(1, "b"));
        cache.Put(2, "c");
        cache.Put(3, "d", 8);

        Assert.Equal(3, cache.Size());
        Assert.False(cache.IsEmpty());
        Assert.True(cache.ContainsKey(2));
        Assert.Equal("c", cache.Get(2));
        Assert.Equal("c", cache.Peek(2));
        Assert.Null(cache.Get(999));
        Assert.True(cache.ContainsValue("c"));
        Assert.False(cache.ContainsValue("zzz"));
        Assert.True(cache.GetHits() >= 1);
        Assert.True(cache.GetMisses() >= 1);

        List<KeyValuePair<long, string>> entries = cache.EntrySet().ToList();
        Assert.Equal(3, entries.Count);
        Assert.Equal("b", cache.GetMap()[1]);

        cache.PutAll(new Dictionary<long, string> { [4] = "e", [5] = "f" });
        Assert.Equal(5, cache.Size());
        Assert.Equal(5, cache.KeySet().Count);
        Assert.True(cache.SizeMapArray() >= cache.Size());
        Assert.True(cache.SizeHot() >= 0);
        Assert.True(cache.SizeNonResident() >= 0);
        Assert.True(cache.GetUsedMemory() > 0);
        Assert.True(cache.GetMemory(1) > 0);

        // Cold and hot key views together are the whole map; the non-resident
        // selector changes which queue the cold view reads.
        List<long> cold = cache.Keys(cold: true, nonResident: false);
        List<long> hot = cache.Keys(cold: false, nonResident: false);
        Assert.Equal(cache.KeySet().Count, cold.Concat(hot).Distinct().Count());
        Assert.Equal(hot, cache.Keys(cold: false, nonResident: true));
        _ = cache.Keys(cold: true, nonResident: true);

        cache.TrimNonResidentQueue();
        Assert.Equal("e", cache.Remove(4));
        Assert.Null(cache.Remove(4));
        Assert.Equal(4, cache.Size());
    }

    /// <summary>
    /// A cache that cannot hold its entries evicts the oldest; their keys stay known
    /// while the residents stay inside the budget.
    /// </summary>
    [Fact]
    public void ASmallCacheEvictsWithinTheBudget()
    {
        CacheLongKeyLIRS<string> cache = NewCache(
            4 * (16 + CacheLongKeyLIRS<string>.GetMemoryOverhead()));
        for (int i = 0; i < 64; i++)
        {
            // No explicit memory: the base SizeOf decides, which is the default 16.
            cache.Put(i, "v" + i);
        }

        Assert.True(cache.GetUsedMemory() <= cache.GetMaxMemory());
        Assert.True(cache.KeySet().Count >= cache.Size());
        Assert.True(cache.Size() < 64);
        cache.TrimNonResidentQueue();
        Assert.True(cache.SizeNonResident() <= 64);
    }
}
