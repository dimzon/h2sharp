/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of MVStore's edge surface: close-with-compaction, read-only
 * commit refusal, invalid rollback versions, in-memory info/names, panic through
 * ExecuteFilestoreOperation, encryption refusal and in-memory map removal.
 */
using System;
using System.Collections.Generic;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class MVStoreApiEdgeTests : StoreTestBase
{
    [Fact]
    public void CloseWithCompactionKeepsTheData()
    {
        string file = TempFile("edge-close.mv");
        DeleteFile(file);
        using (MVStore s = MVStore.Open(file))
        {
            MVMap<int, string> m = s.OpenMap<int, string>("m");
            for (int i = 0; i < 100; i++)
            {
                m.Put(i, "v" + i);
            }

            s.Commit();
            s.Close(-1);
        }

        using MVStore s2 = MVStore.Open(file);
        MVMap<int, string> m2 = s2.OpenMap<int, string>("m");
        Assert.Equal(100L, m2.SizeAsLong());
        Assert.Equal("v42", m2.Get(42));
    }

    [Fact]
    public void ReadOnlyStoreRefusesCommit()
    {
        string file = TempFile("edge-readonly.mv");
        DeleteFile(file);
        using (MVStore s = MVStore.Open(file))
        {
            s.OpenMap<int, string>("m").Put(1, "a");
            s.Commit();
        }

        MVStore readOnly = new MVStore.Builder().FileName(file).ReadOnly().Open();
        try
        {
            readOnly.OpenMap<int, string>("m").Put(2, "b");
            Assert.ThrowsAny<Exception>(() => readOnly.Commit());
        }
        finally
        {
            if (!readOnly.IsClosed())
            {
                readOnly.CloseImmediately();
            }
        }
    }

    [Fact]
    public void RollbackToUnknownVersionRefuses()
    {
        using MVStore s = new MVStore.Builder().Open();
        _ = s.OpenMap<int, string>("m").Put(1, "a");
        s.Commit();
        Assert.Throws<ArgumentException>(() => s.RollbackTo(-1));
    }

    [Fact]
    public void InMemoryInfoAndNamesAnswer()
    {
        using MVStore s = new MVStore.Builder().Open();
        s.SetVersionsToKeep(0);
        MVMap<int, string> m = s.OpenMap<int, string>("first");
        _ = s.OpenMap<int, string>("second");
        m.Put(1, "a");
        s.Commit();
        Assert.Contains("first", s.GetMapNames());
        Assert.Contains("second", s.GetMapNames());
        int pairs = 0;
        s.PopulateInfo((_, _) => pairs++);
        Assert.True(pairs > 0);
        Assert.True(s.GetTimeAbsolute() >= 0);
        Assert.False(s.IsVersioningRequired());
    }

    [Fact]
    public void ExecuteFilestoreOperationPanicsWithHandler()
    {
        string file = TempFile("edge-panic.mv");
        DeleteFile(file);
        Exception? seen = null;
        MVStore s = new MVStore.Builder()
            .FileName(file)
            .BackgroundExceptionHandler((_, e) => seen = e)
            .Open();
        try
        {
            Assert.ThrowsAny<Exception>(
                () => s.ExecuteFilestoreOperation(() => throw new InvalidOperationException("boom")));
            Assert.NotNull(seen);
            Assert.NotNull(s.GetPanicException());
            Assert.True(s.IsClosed());
        }
        finally
        {
            if (!s.IsClosed())
            {
                s.CloseImmediately();
            }
        }
    }

    [Fact]
    public void EncryptionKeyIsRefusedAndZeroed()
    {
        string file = TempFile("edge-crypt.mv");
        DeleteFile(file);
        char[] key = "secret".ToCharArray();
        Assert.ThrowsAny<Exception>(() => new MVStore.Builder()
            .FileName(file)
            .EncryptionKey(key)
            .Open());
        Assert.All(key, c => Assert.Equal('\0', c));
    }

    [Fact]
    public void ClosedStoreRefusesOperations()
    {
        string file = TempFile("edge-closed.mv");
        DeleteFile(file);
        using (MVStore s = MVStore.Open(file))
        {
            s.OpenMap<int, string>("m").Put(1, "a");
            s.Commit();
        }

        MVStore closed = MVStore.Open(file);
        closed.Close();
        Assert.True(closed.IsClosed());
        Assert.ThrowsAny<Exception>(() => closed.GetMapNames());
        Assert.ThrowsAny<Exception>(() => closed.OpenMap<int, string>("m"));
    }

    [Fact]
    public void ExecuteFilestoreOperationCatchesStoreErrors()
    {
        string file = TempFile("edge-panic2.mv");
        DeleteFile(file);
        using (MVStore s = new MVStore.Builder().FileName(file).Open())
        {
            Assert.ThrowsAny<Exception>(() => s.ExecuteFilestoreOperation(
                () => throw DataUtils.NewMvStoreException(DataUtils.ErrorInternal, "store failed")));
            Assert.NotNull(s.GetPanicException());
        }

        string file2 = TempFile("edge-panic3.mv");
        DeleteFile(file2);
        using (MVStore s = new MVStore.Builder()
            .FileName(file2)
            .BackgroundExceptionHandler((_, _) => throw new InvalidOperationException("handler"))
            .Open())
        {
            Assert.ThrowsAny<Exception>(
                () => s.ExecuteFilestoreOperation(() => throw new InvalidOperationException("boom")));
            Assert.NotNull(s.GetPanicException());
        }
    }

    [Fact]
    public void RemoveClosedMapOnCloseDeregistersRoot()
    {
        string file = TempFile("edge-dereg.mv");
        DeleteFile(file);
        using (MVStore s = MVStore.Open(file))
        {
            MVMap<int, string> m = s.OpenMap<int, string>("gone");
            m.Put(1, "a");
            s.Commit();
            s.RemoveMap(m);
            Assert.True(m.IsClosed());
            s.Close();
        }
    }

    [Fact]
    public void RemoveMapInMemoryDropsTheRegistryEntry()
    {
        using MVStore s = new MVStore.Builder().Open();
        s.SetVersionsToKeep(0);
        MVMap<int, string> m = s.OpenMap<int, string>("victim");
        m.Put(1, "a");
        int id = m.GetId();
        s.RemoveMap("victim");
        Assert.False(s.HasMap("victim"));
        Assert.Null(s.GetMap<int, string>(id));
        s.Commit();
    }
}