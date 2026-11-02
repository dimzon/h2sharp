/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The long-key LIRS cache driven directly: entries come back, memory stays
 * bounded, removal and clearing hold, and a null value is refused.
 */
using System;
using H2Sharp.MvStore.Cache;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class TestCacheLirs
{
    private sealed class TestCache : CacheLongKeyLIRS<string>
    {
        public TestCache(long maxMemory)
            : base(new Config { MaxMemory = maxMemory, SegmentCount = 1 })
        {
        }

        protected override long SizeOf(string value) => 100;
    }

    [Fact]
    public void EntriesComeBackByKey()
    {
        TestCache cache = new(100_000);
        Assert.True(cache.IsEmpty());

        Assert.Null(cache.Put(1, "one"));
        Assert.Null(cache.Put(2, "two"));

        Assert.Equal("one", cache.Get(1));
        Assert.Equal("two", cache.Peek(2));
        Assert.True(cache.ContainsKey(1));
        Assert.False(cache.ContainsKey(3));
        Assert.Null(cache.Get(3));
        Assert.Equal(2, cache.Size());
        Assert.Equal(100 + GetOverhead(), cache.GetMemory(1));
        Assert.Equal(0, cache.GetMemory(3));
        Assert.False(cache.IsEmpty());
    }

    [Fact]
    public void PuttingAnExistingKeyReturnsTheOldValue()
    {
        TestCache cache = new(100_000);
        Assert.Null(cache.Put(1, "one"));

        Assert.Equal("one", cache.Put(1, "uno"));
        Assert.Equal("uno", cache.Get(1));
        Assert.Equal(1, cache.Size());
    }

    [Fact]
    public void RemovalDropsResidentEntries()
    {
        TestCache cache = new(100_000);
        cache.Put(1, "one");
        cache.Put(2, "two");

        Assert.Equal("one", cache.Remove(1));
        Assert.False(cache.ContainsKey(1));
        Assert.Null(cache.Remove(1));
        Assert.Equal(1, cache.Size());

        cache.Clear();
        Assert.True(cache.IsEmpty());
        Assert.Equal(0, cache.Size());
    }

    [Fact]
    public void MemoryStaysWithinTheBudget()
    {
        // Four entries of 100 plus overhead fit; the 64 that follow do not.
        TestCache cache = new(4 * (100 + GetOverhead()));
        for (int i = 0; i < 64; i++)
        {
            cache.Put(i, "value-" + i);
        }

        Assert.True(cache.GetUsedMemory() <= cache.GetMaxMemory());
        // Evicted entries linger as non-residents, so the key set outgrows the residents.
        Assert.True(cache.KeySet().Count >= cache.Size());
        Assert.True(cache.Size() < 64);
    }

    [Fact]
    public void NullValuesAreRefused()
    {
        TestCache cache = new(100_000);

        Assert.Throws<ArgumentException>(() => cache.Put(1, null!));
    }

    [Fact]
    public void IntrospectionCountsAddUp()
    {
        TestCache cache = new(100_000);
        for (int i = 0; i < 10; i++)
        {
            cache.Put(i, "value-" + i);
        }

        Assert.Equal(10, cache.Size());
        Assert.Equal(10, cache.Values().Count);
        Assert.Equal(10, cache.KeySet().Count);
        Assert.True(cache.SizeHot() <= cache.Size());
        Assert.True(cache.GetHits() >= 0);
        Assert.True(cache.GetMisses() >= 0);
    }

    private static long GetOverhead() => CacheLongKeyLIRS<string>.GetMemoryOverhead();
}
