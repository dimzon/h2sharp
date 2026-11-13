/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The plain accessors and helpers of the base Index class, called directly on the indexes a
 * small database owns: the drop SQL, the per-position column accessors, the first-column
 * lookup, the uniqueness flags, the unique-column copy, the row equivalence and comparison
 * shortcuts, the planner's one-row fallback for an unresearched column, and the base
 * approximations that inheriting indexes do not override.
 */
using System;
using System.Linq;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.MvStore.Db;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Util;
using Xunit;
using Index = H2Sharp.Indexes.Index;

namespace H2Sharp.Tests;

public sealed class IndexAccessorTests
{
    private readonly SessionLocal session;
    private readonly Database database;
    private readonly MVTable table;
    private readonly Index primary;
    private readonly Index secondary;

    public IndexAccessorTests()
    {
        database = new Database(
            ConnectionInfo.Memory("index_accessors_" + Guid.NewGuid().ToString("N")));
        session = new SessionLocal(database, "sa");
        Execute("CREATE TABLE T(ID INT PRIMARY KEY, A INT)");
        Execute("CREATE INDEX IDX_A ON T(A)");
        Execute("INSERT INTO T VALUES (1, 10)");
        table = (MVTable)database.GetSchema("PUBLIC").GetTableOrView(null, "T");
        primary = table.FindPrimaryKey()!;
        secondary = table.GetIndexes().First(i => i.GetName() == "IDX_A");
    }

    private void Execute(string sql)    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        _ = container.ExecuteUpdate(false);
    }

    private void Query(string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        using ResultInterface result = container.ExecuteQuery(10, 0, false);
    }

    [Fact]
    public void GetDropSqlPrintsDropIndex()
    {
        Assert.Equal("DROP INDEX " + secondary.GetSql(HasSql.DefaultSqlFlags), secondary.GetDropSql());
        _ = Assert.Single(secondary.GetIndexColumns());
    }

    [Fact]
    public void AnIndexOnALobIsRefused()
    {
        Execute("CREATE TABLE LB(B BLOB)");
        H2SharpException e = Assert.Throws<H2SharpException>(
            () => Execute("CREATE INDEX IDX_LB ON LB(B)"));
        Assert.Contains("Index on column", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PositionalColumnAccessorsAnswer()
    {
        Assert.Equal("A", secondary.GetColumn(0).GetColumn().GetName());
        Assert.True(secondary.IsColumnAscending(0));
        Assert.Equal(1, secondary.GetColumnCount());
        Assert.True(secondary.IsFirstColumn(table.GetColumn("A")));
        Assert.False(secondary.IsFirstColumn(table.GetColumn("ID")));
    }

    [Fact]
    public void GetFirstColumnAnswersItsOwnTableOnly()
    {
        Assert.Equal("A", secondary.GetFirstColumn(table)!.GetName());
        Table other = database.GetSchema("PUBLIC").GetTableOrView(null, "T")!;
        Assert.Equal("A", secondary.GetFirstColumn(other)!.GetName());
        Execute("CREATE TABLE U(X INT)");
        Table stranger = (MVTable)database.GetSchema("PUBLIC").GetTableOrView(null, "U");
        Assert.Null(secondary.GetFirstColumn(stranger));
        Assert.Null(secondary.GetFirstColumn(null));
    }

    [Fact]
    public void UniquenessFlagsAnswer()
    {
        Assert.True(primary.IsUnique());
        Assert.True(primary.IsPrimaryKey());
        Assert.False(secondary.IsUnique());
        Assert.False(secondary.IsPrimaryKey());
        Assert.False(secondary.IsScan());
    }

    [Fact]
    public void UniqueColumnsCopyOrComeBackEmpty()
    {
        Assert.Equal(primary.GetUniqueColumnCount(), primary.GetUniqueColumns().Length);
        Assert.Empty(secondary.GetUniqueColumns());
    }

    [Fact]
    public void TheSameRowObjectIsEquivalentAndEqual()
    {
        Row row = (Row)table.GetRow(session, 1)!;
        Assert.True(primary.AreRowsEquivalent(row, row));
        Assert.Equal(0, primary.CompareRows(row, row));
    }

    [Fact]
    public void EqualityOnAnUnresearchedColumnAssumesOneDistinctRow()
    {
        Execute("CREATE TABLE E(A INT)");
        Execute("CREATE INDEX IDX_E ON E(A)");
        Query("SELECT * FROM E WHERE A = 1");
    }

    [Fact]
    public void SecondaryIndexApproximationFallsBackToTheCount()
    {
        Assert.Equal(1L, secondary.GetRowCountApproximation(session));
    }

    [Fact]
    public void InformationSchemaIndexOccupiesNoDeclaredSpace()
    {
        Table infoTable = database.GetSchema("INFORMATION_SCHEMA")
            .GetTableOrView(null, "TABLES")!;
        Index infoIndex = infoTable.GetIndexes().First();
        Assert.Equal(0L, infoIndex.GetDiskSpaceUsed(approximate: true));
    }
}
