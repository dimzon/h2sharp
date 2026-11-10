/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 115: the command list a multi-statement text becomes — its commands,
 * parameters, meta answer, dependencies, the query and update paths, invalidation
 * of a cached result and close.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Command;
using H2Sharp.Expressions;
using H2Sharp.Values;
using H2Sharp.Engine;
using H2Sharp.MvStore.Db;
using H2Sharp.Result;
using H2Sharp.Tables;
using Xunit;

namespace H2Sharp.Tests;

public sealed class CommandListMembersTests
{
    [Fact]
    public void TheListAnswersItsMembersAndRuns()
    {
        Database database = new(ConnectionInfo.Memory("cmd_list_" + Guid.NewGuid().ToString("N")));
        SessionLocal session = new(database, "sa");

        CommandList list = Assert.IsType<CommandList>(
            session.PrepareCommand("CREATE TABLE C(ID INT); INSERT INTO C VALUES(1); SELECT ID FROM C"));

        // The session prepares the first statement; the rest stay lazy in the ADO
        // command, so the list answers with the one it holds.
        Assert.Single(list.GetCommands());
        Assert.Empty(list.GetParameters());
        // A DDL command has no meta answer in the port: the schema reader is not
        // ported, and the refusal is the documented TODO.
        _ = Assert.Throws<TODOException>(() => list.QueryMeta());
        Assert.False(list.IsReadOnly());
        Assert.True(list.IsTransactional());
        Assert.False(list.IsQuery());
        Assert.Equal(CommandInterface.CreateTable, list.GetCommandType());
        Assert.NotNull(list.GetDependencies());

        _ = list.UpdateRows(null!);
        list.InvalidateCachedResult(Assert.IsType<MVTable>(database.GetSchema("PUBLIC").GetTableOrView(session, "C")));
        list.Close();
        list.Close();

        // A query list answers through its first command, and a parameter list
        // collects the question marks of every statement.
        CommandList queryList = Assert.IsType<CommandList>(
            session.PrepareCommand("SELECT ? + 1; SELECT ? + 2"));
        Assert.Equal(2, queryList.GetParameters().Count);
        Assert.IsType<Parameter>(queryList.GetParameters()[0]).SetValue(ValueInteger.Get(0));
        Assert.IsType<Parameter>(queryList.GetParameters()[1]).SetValue(ValueInteger.Get(1));
        using ResultInterface result = queryList.Query(10);
        Assert.True(result.Next());
        Assert.Equal(1, result.CurrentRow()[0]!.GetInt());
        queryList.Close();

        database.Close();
    }
}
