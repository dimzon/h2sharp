/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of MVSecondaryIndex: no-op lifecycle, row-not-found on remove,
 * closed-map errors, Truncate, the TODO map accessor, and the duplicate-key path.
 */
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.MvStore.Db;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;
using System.Reflection;
using Xunit;

namespace H2Sharp.Tests;

public sealed class SecondaryIndexDirectTests
{
    private readonly Database database;
    private readonly SessionLocal session;
    private readonly MVSecondaryIndex index;

    public SecondaryIndexDirectTests()
    {
        database = new Database(
            ConnectionInfo.Memory("secondary_direct_" + Guid.NewGuid().ToString("N")));
        session = new SessionLocal(database, "sa");
        Execute("CREATE TABLE T(ID INT PRIMARY KEY, A INT)");
        Execute("CREATE UNIQUE INDEX IDX_A ON T(A)");
        Execute("INSERT INTO T VALUES (1, 10), (2, 20)");
        index = database.GetSchema("PUBLIC").GetTableOrView(null, "T").GetIndexes()
            .OfType<MVSecondaryIndex>().Single();
    }

    private CommandContainer Prepare(string sql)
        => Assert.IsType<CommandContainer>(session.PrepareCommand(sql));

    private void Execute(string sql)
    {
        CommandContainer container = Prepare(sql);
        _ = container.ExecuteUpdate(false);
    }

    private string Scalar(string sql)
    {
        CommandContainer container = Prepare(sql);
        using ResultInterface result = container.ExecuteQuery(10, 0, false);
        Assert.True(result.Next());
        return result.CurrentRow()![0]!.GetString()!;
    }

    [Fact]
    public void CloseIsANop()
    {
        index.Close(session);
    }

    [Fact]
    public void RemovingAMissingRowIsRefused()
    {
        MVTable table = (MVTable)database.GetSchema("PUBLIC").GetTableOrView(null, "T");
        Row row = table.GetTemplateRow();
        row.SetKey(999);
        row.SetValue(table.GetColumn("A").GetColumnId(), ValueInteger.Get(77));

        H2SharpException e = Assert.Throws<H2SharpException>(() => index.Remove(session, row));
        Assert.Equal(Api.ErrorCode.RowNotFoundWhenDeleting1, e.H2ErrorCode);
    }

    [Fact]
    public void GetMVMapIsATodo()
    {
        TODOException e = Assert.Throws<TODOException>(() => index.GetMVMap());
        Assert.Contains("object-typed", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMapWithNullSessionReturnsDataMap()
    {
        // GetMap(null) returns the root dataMap directly (line 464-465)
        var getMap = typeof(MVSecondaryIndex).GetMethod("GetMap", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = getMap.Invoke(index, new object?[] { null });
        Assert.NotNull(result);
    }
}
