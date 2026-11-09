/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 102: the delegate index a single-column primary key gets. It stores no map of
 * its own and answers every member from the primary index: the row factory, the counts,
 * the space, the transaction map and the no-op write hooks.
 */
using System;
using System.Linq;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.MvStore.Db;
using H2Sharp.MvStore.Tx;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class MVDelegateIndexMembersTests
{
    [Fact]
    public void TheDelegateAnswersFromThePrimaryIndex()
    {
        Database database = new(ConnectionInfo.Memory("delegate_idx_" + Guid.NewGuid().ToString("N")));
        SessionLocal session = new(database, "sa");
        Schema schema = database.GetSchema("PUBLIC");

        Column id = new("ID", TypeInfo.TypeInteger, null, 0);
        id.SetNullable(false);
        Column name = new("NAME", TypeInfo.TypeVarchar, null, 1);
        MVTable table = new(database, schema, 0, "T", [id, name], persistIndexes: false, persistData: false);
        foreach (Column column in table.GetColumns())
        {
            column.SetTable(table, column.GetColumnId());
        }

        table.AddIndex(session, "PK", 7, [new IndexColumn(id, ascending: true)], 1,
            IndexType.CreatePrimaryKey(persistent: false, hash: false), create: true, indexComment: null);
        schema.Add(table);
        table.AddRow(session, new DefaultRow([ValueInteger.Get(1), ValueVarchar.Get("a")]));
        table.AddRow(session, new DefaultRow([ValueInteger.Get(2), ValueVarchar.Get("b")]));

        MVDelegateIndex index = Assert.IsType<MVDelegateIndex>(
            table.GetIndexes().Single(candidate => candidate is MVDelegateIndex));

        Assert.NotNull(index.GetRowFactory());
        Assert.Equal(2, index.GetRowCount(session));
        Assert.True(index.GetRowCountApproximation(session) >= 2);
        _ = index.GetDiskSpaceUsed(approximate: true);
        _ = index.GetDiskSpaceUsed(approximate: false);
        Assert.True(index.GetCost(session, null, null, 0, null, null, isSelectCommand: true) >= 0);

        // The delegate stores nothing, so the write hooks do nothing.
        Row template = table.GetTemplateRow();
        index.Add(session, template);
        index.Close(session);

        Transaction? transaction = table.GetTransactionBegin();
        Assert.NotNull(transaction);
        Assert.NotNull(index.GetTransactionMap(transaction!));
        transaction!.Rollback();
        database.Close();
    }
}
