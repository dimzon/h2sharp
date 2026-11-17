/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 278: the Index base class's capabilities and refusals, reached through the DUAL table's
 * scan index, which has no storage behind it.
 */
using System;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class IndexSurfaceTests
{
    [Fact]
    public void AStorageLessIndexAnswersCapabilitiesAndRefusesMutations()
    {
        Database database = new(ConnectionInfo.Memory("index_surface"));
        SessionLocal session = new(database, "sa");
        try
        {
            H2Sharp.Indexes.Index index = new DualTable(database).GetScanIndex(session)!;
            Assert.Equal(index.GetRowCount(session), index.GetRowCountApproximation(session));
            Assert.True(index.CanGetFirstOrLast());
            Assert.False(index.CanFindNext());

            Row row = Row.Get([ValueInteger.Get(1)], 1, 1L);
            Assert.Throws<TODOException>(() => { index.Add(session, row); });
            Assert.Throws<TODOException>(() => { index.Remove(session, row); });
            Assert.Throws<TODOException>(() => { index.Remove(session); });
            Assert.Throws<TODOException>(() => { index.Find(session, row); });
        }
        finally
        {
            database.Close();
        }
    }
}
