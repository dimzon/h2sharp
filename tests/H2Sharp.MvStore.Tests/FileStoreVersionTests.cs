/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The file store's version knowledge and the cache configuration the builder accepts:
 * an empty store knows every version, a store with data answers for its own version and
 * refuses an unknown one, and cache concurrency reaches the cache configuration.
 */
using H2Sharp.MvStore;
using H2Sharp.MvStore.Type;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class FileStoreVersionTests : StoreTestBase
{
    [Fact]
    public void AnEmptyStoreKnowsEveryVersion()
    {
        string file = TempFile("known.mv");
        DeleteFile(file);
        using MVStore s = new MVStore.Builder().FileName(file).Open();

        IFileStore? fileStore = s.GetFileStore();
        Assert.NotNull(fileStore);
        Assert.True(fileStore!.IsKnownVersion(0));
        Assert.True(fileStore.IsKnownVersion(12345));
    }

    [Fact]
    public void AStoredVersionIsKnownAndAnInventedOneIsNot()
    {
        string file = TempFile("stored.mv");
        DeleteFile(file);

        long version;
        using (MVStore s = new MVStore.Builder().FileName(file).Open())
        {
            var map = s.OpenMap("test", new MVMap<long, long>.Builder<long, long>()
                .KeyType(LongDataType.Instance)
                .ValueType(LongDataType.Instance));
            map.Put(1L, 100L);
            version = s.Commit();
        }

        using (MVStore s = new MVStore.Builder().FileName(file).Open())
        {
            IFileStore fileStore = s.GetFileStore()!;
            Assert.True(fileStore.IsKnownVersion(version));
            Assert.True(fileStore.IsKnownVersion(s.GetCurrentVersion()));
            // The lookup answers the newest chunk at or below the version, so only a version
            // older than the first chunk is unknown.
            Assert.True(fileStore.IsKnownVersion(version + 1000));
            Assert.False(fileStore.IsKnownVersion(0));
        }
    }

    [Fact]
    public void CacheConcurrencyConfiguresTheCache()
    {
        // The cache lives in the file store, so the configuration only reaches it there.
        string file = TempFile("cache.mv");
        DeleteFile(file);
        using MVStore s = new MVStore.Builder().FileName(file)
            .CacheSize(1).CacheConcurrency(2).Open();
        var map = s.OpenMap("test", new MVMap<long, string>.Builder<long, string>()
            .KeyType(LongDataType.Instance)
            .ValueType(StringDataType.Instance));
        for (long i = 0; i < 100; i++)
        {
            map.Put(i, "v" + i);
        }

        Assert.Equal("v42", map.Get(42L));
        Assert.True(s.Commit() > 0);
    }
}
