/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * rollbackTo(0) means "remove all data". The reference is broken there: it resets the
 * layout but leaves the discarded chunks registered, so close asserts an orphaned chunk
 * and the header still names a chunk whose layout would resurrect the data on reopen.
 * The port fixes that (D-051): the store is emptied, the close is clean and a reopen
 * sees no rows from before the rollback.
 */
using System;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.MvStore.Tests;

public sealed class RollbackToZeroTests : StoreTestBase
{
    [Fact]
    public void RollbackToZeroEmptiesTheStoreAndKeepsWriting()
    {
        string file = TempFile("rollback-zero.mv");
        DeleteFile(file);
        using (MVStore s = MVStore.Open(file))
        {
            MVMap<int, string> m = s.OpenMap<int, string>("m");
            m.Put(1, "a");
            s.Commit();
            m.Put(2, "b");
            s.Commit();

            s.RollbackTo(0);
            Assert.Equal(0L, s.GetCurrentVersion());
            // The old handle belongs to a map created in the rolled-back version: the
            // reference closes it and keeps its data readable for concurrent readers.
            Assert.True(m.IsClosed());

            MVMap<int, string> fresh = s.OpenMap<int, string>("m");
            Assert.Equal(0L, fresh.SizeAsLong());
            fresh.Put(3, "c");
            s.Commit();
        }

        using (MVStore s2 = MVStore.Open(file))
        {
            MVMap<int, string> m2 = s2.OpenMap<int, string>("m");
            Assert.False(m2.ContainsKey(1));
            Assert.False(m2.ContainsKey(2));
            Assert.Equal("c", m2.Get(3));
            Assert.Equal(1L, m2.SizeAsLong());
        }
    }

    [Fact]
    public void CloseRightAfterRollbackToZeroReopensEmpty()
    {
        string file = TempFile("rollback-zero-close.mv");
        DeleteFile(file);
        using (MVStore s = MVStore.Open(file))
        {
            MVMap<int, string> m = s.OpenMap<int, string>("m");
            m.Put(1, "a");
            s.Commit();
            s.RollbackTo(0);
        }

        using (MVStore s2 = MVStore.Open(file))
        {
            Assert.Equal(0L, s2.OpenMap<int, string>("m").SizeAsLong());
        }
    }
}