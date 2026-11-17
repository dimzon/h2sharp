/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 272: the two virtual tables' public surface — the recursive-query shadow table and
 * DUAL — which the SQL suites only reach through a cursor and never ask about directly.
 */
using System;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class VirtualTableSurfaceTests
{
    [Fact]
    public void ShadowTableAnswersItsShapeAndRefusesVirtualOperations()
    {
        Database database = new(ConnectionInfo.Memory("shadow_table_surface"));
        SessionLocal session = new(database, "sa");
        try
        {
            Column[] columns = [new("A", TypeInfo.TypeInteger), new("B", TypeInfo.TypeVarchar)];
            ShadowTable table = new(database.GetSchema("PUBLIC"), "SHADOW", columns);

            Assert.Equal("SHADOW", table.GetTableName());
            Assert.Equal(Table.TABLE, table.GetTableType());
            Assert.Equal(2, table.GetColumnCount());
            Assert.Same(columns[0], table.GetColumn(0));
            Assert.Same(columns, table.GetColumns());
            Assert.Empty(table.GetIndexes());
            Assert.False(table.CanGetRowCount(session));
            Assert.Equal(long.MaxValue, table.GetRowCount(session));
            Assert.Equal(long.MaxValue, table.GetRowCountApproximation(session));
            Assert.Null(table.GetCreateSql());
            Assert.Equal("\"SHADOW\"", table.GetSql(0));
            Assert.Equal(0, table.GetId());
            Assert.False(table.IsClosed());
            Assert.Same(database.GetSchema("PUBLIC"), table.GetSchema());
            Assert.ThrowsAny<Exception>(() => table.GetResult(session));
            Assert.ThrowsAny<Exception>(() => table.Truncate(session));
            table.RemoveChildrenAndResources(session);
            table.Close();
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void DualTableHasOneRowAndNoColumns()
    {
        Database database = new(ConnectionInfo.Memory("dual_table_surface"));
        SessionLocal session = new(database, "sa");
        try
        {
            DualTable table = new(database);
            Assert.Equal(DualTable.Name, table.GetTableName());
            Assert.Equal(Table.SYSTEM_TABLE, table.GetTableType());
            Assert.Equal(0, table.GetColumnCount());
            Assert.Empty(table.GetColumns());
            Assert.NotNull(table.GetScanIndex(session));
            Assert.Same(table.GetScanIndex(session), table.GetIndexFor(session, null!));
            Assert.Single(table.GetIndexes());
            Assert.Equal(1L, table.GetRowCountApproximation(session));
            Assert.Equal(0L, table.GetMaxDataModificationId());
            Assert.True(table.CanGetRowCount(session));
            Assert.Equal(1L, table.GetRowCount(session));
            Assert.Null(table.GetCreateSql());
            Assert.Equal("DUAL", table.GetSql(0));
            Assert.Equal(0, table.GetId());
            Assert.False(table.IsClosed());
            Assert.Same(database.GetMainSchema(), table.GetSchema());
            Assert.Throws<TODOException>(() => table.GetColumn(0));
            Assert.ThrowsAny<H2SharpException>(() => table.Truncate(session));
            table.RemoveChildrenAndResources(session);
            table.Close();
        }
        finally
        {
            database.Close();
        }
    }
}
