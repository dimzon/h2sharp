/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 241: the reference's metadata table refuses every write even though its rows are
 * computed on read; the schema and the columns are the ones it was built with.
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class MetaTableSurfaceTests
{
    private sealed class TestMetaTable : MetaTable
    {
        internal TestMetaTable(Schema schema, int id, int type) : base(schema, id, type)
        {
            SetMetaTableName("TEST_META");
            SetColumns([Column("NAME")]);
            SetIndexes();
        }

        public override List<Row> GenerateRows(SessionLocal session, SearchRow? first, SearchRow? last)
            => [];
    }

    [Fact]
    public void AnswersItsShapeAndRefusesWrites()
    {
        Database database = new(ConnectionInfo.Memory("meta_surface"));
        Schema schema = database.GetSchema("PUBLIC");
        TestMetaTable table = new(schema, 12345, 7);
        Assert.Equal("TEST_META", table.GetTableName());
        Assert.Equal(12345, table.GetId());
        Assert.Equal(1, table.GetColumnCount());
        Assert.Equal("NAME", table.GetColumn(0).GetName());
        Assert.Same(schema, table.GetSchema());
        Assert.True(table.IsDeterministic());
        Assert.False(table.CanReference());
        Assert.False(table.CanGetRowCount(null!));
        Assert.Equal(Table.SYSTEM_TABLE, table.GetTableType());
        Assert.Null(table.GetCreateSql());
        Assert.Single(table.GetIndexes());
        Assert.NotNull(table.GetScanIndex(null!));
        Assert.NotNull(table.GetIndexFor(null!, null!));
        Assert.False(table.IsClosed());
        Assert.Throws<H2SharpException>(() => table.Truncate(null!));
        Assert.Throws<H2SharpException>(() => table.CheckRename());
        Assert.Throws<H2SharpException>(() => table.CheckSupportAlter());
        Assert.Throws<H2SharpException>(() => table.RemoveChildrenAndResources(null!));
        table.Close();
        database.Close();
    }
}
