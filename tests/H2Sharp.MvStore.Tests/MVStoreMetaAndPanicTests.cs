/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 224: the store's cleanup edges — the meta map scrub on open, the panic that closes a
 * store, a known version on a file store, a closed map dropped by a later commit, and the
 * last-key trimming of a regular and a single-writer map.
 */
using System;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class MVStoreMetaAndPanicTests : StoreTestBase
{
    [Fact]
    public void TheMetaScrubRepairsAndRemovesEntries()
    {
        string file = TempFile("meta-scrub.mv");
        DeleteFile(file);
        using (MVStore s = MVStore.Open(file))
        {
            MVMap<int, string> m = s.OpenMap<int, string>("m");
            m.Put(1, "a");
            _ = s.Commit();

            MVMap<string, string> meta = s.GetMetaMap();
            meta.Put(DataUtils.MetaName + "gone", "9");
            meta.Put(DataUtils.MetaMap + "7", "name:bogus");
            // A meta change is what makes the store write the meta map root.
            s.SetStoreVersion(5);
            _ = s.Commit();
        }

        using MVStore reopened = MVStore.Open(file);
        MVMap<string, string> cleaned = reopened.GetMetaMap();
        Assert.Null(cleaned.Get(DataUtils.MetaName + "gone"));
        var keys = new System.Collections.Generic.List<string>();
        var iterator = cleaned.KeyIterator(null);
        while (iterator.MoveNext())
        {
            keys.Add(iterator.Current);
        }

        Assert.True(cleaned.Get(DataUtils.MetaName + "bogus") == "7",
            "map7=" + (cleaned.Get(DataUtils.MetaMap + "7") ?? "null")
            + " name.m=" + (cleaned.Get(DataUtils.MetaName + "m") ?? "null")
            + " keys=" + string.Join(",", keys));
        Assert.Equal("m", reopened.OpenMap<int, string>("m").GetName());
        _ = reopened.Commit();
    }

    [Fact]
    public void APanicClosesTheStoreAndIsThrownByTheNextOperation()
    {
        using (MVStore s = MVStore.Open(TempFile("panic-one.mv")))
        {
            MvStoreException e = Assert.Throws<MvStoreException>(
                () => s.Panic(new MvStoreException(DataUtils.ErrorInternal, "boom")));
            Assert.Contains("boom", e.Message, StringComparison.Ordinal);
            Assert.True(s.IsClosed());
            _ = Assert.Throws<MvStoreException>(() => s.Commit());
        }

        using (MVStore s = MVStore.Open(TempFile("panic-two.mv")))
        {
            _ = Assert.Throws<MvStoreException>(
                () => s.Panic(new InvalidOperationException("wrapped")));
            Assert.True(s.IsClosed());
        }
    }

    [Fact]
    public void ARollbackToAnOlderFileVersionChecksTheChunks()
    {
        string file = TempFile("rollback-file.mv");
        DeleteFile(file);
        using MVStore s = MVStore.Open(file);
        MVMap<int, string> m = s.OpenMap<int, string>("m");
        m.Put(1, "one");
        long v1 = s.Commit();
        m.Put(2, "two");
        _ = s.Commit();

        s.RollbackTo(v1);
        Assert.Equal(v1, s.GetCurrentVersion());
        Assert.Equal("one", m.Get(1));
        Assert.Null(m.Get(2));
    }

    [Fact]
    public void AClosedMapIsDroppedByALaterCommit()
    {
        string file = TempFile("closed-map.mv");
        DeleteFile(file);
        using MVStore s = MVStore.Open(file);
        s.SetVersionsToKeep(0);
        MVMap<int, string> gone = s.OpenMap<int, string>("gone");
        gone.Put(1, "a");
        _ = s.Commit();
        MVMap<int, string> keeper = s.OpenMap<int, string>("keeper");
        ((IAnyMap)gone).MapClose();

        for (int i = 0; i < 10; i++)
        {
            keeper.Put(i, "v" + i);
            _ = s.Commit();
        }

        Assert.Equal("v9", keeper.Get(9));
    }

    [Fact]
    public void TrimLastDropsTheLastKeyOfBothMapKinds()
    {
        using MVStore s = new MVStore.Builder().Open();
        MVMap<int, string> regular = s.OpenMap<int, string>("regular");
        for (int i = 0; i < 5; i++)
        {
            regular.Put(i, "v" + i);
        }

        regular.TrimLast();
        Assert.Null(regular.Get(4));
        Assert.Equal(4L, regular.SizeAsLong());

        MVMap<int, string> writer = s.OpenMap(
            "writer", new MVMap<int, string>.Builder<int, string>().SingleWriter());
        writer.Append(1, "a");
        writer.Append(2, "b");
        writer.TrimLast();
        Assert.Null(writer.Get(2));
        Assert.Equal("a", writer.Get(1));
        Assert.Equal(1L, writer.SizeAsLong());
    }

    [Fact]
    public void OpeningADirectoryOrALockedFileIsRefused()
    {
        MvStoreException directory = Assert.Throws<MvStoreException>(() => MVStore.Open(TempDir));
        Assert.Contains("Could not open file", directory.Message, StringComparison.Ordinal);

        string file = TempFile("locked.mv");
        DeleteFile(file);
        using (MVStore first = MVStore.Open(file))
        {
            first.OpenMap<int, string>("m").Put(1, "a");
            MvStoreException locked = Assert.Throws<MvStoreException>(() => MVStore.Open(file));
            Assert.Contains("locked", locked.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AStoreOpenedReadOnlyReadsItsData()
    {
        string file = TempFile("read-only.mv");
        DeleteFile(file);
        using (MVStore s = MVStore.Open(file))
        {
            MVMap<int, string> m = s.OpenMap<int, string>("m");
            m.Put(1, "a");
            _ = s.Commit();
        }

        using MVStore readOnly = new MVStore.Builder().FileName(file).ReadOnly().Open();
        Assert.True(readOnly.IsReadOnly());
        Assert.Equal("a", readOnly.OpenMap<int, string>("m").Get(1));
        Assert.EndsWith("read-only.mv", readOnly.GetFileStore()!.GetFileName()!,
            StringComparison.OrdinalIgnoreCase);
    }
}
