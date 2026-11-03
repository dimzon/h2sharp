/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The command list as the session hands it out: several statements prepared from one text,
 * the first answering, the rest running for effect, parameters of the whole text, and the
 * dependency and invalidation questions the command cache asks.
 */
using System;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Result;
using Xunit;

namespace H2Sharp.Tests;

public sealed class CommandListDirectTests
{
    private delegate void Execute(SessionLocal session, string sql);

    private static SessionLocal Open()
        => new(new Database(ConnectionInfo.Memory("command_list_" + Guid.NewGuid().ToString("N"))), "sa") { };

    private static void Run(SessionLocal session, string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        _ = container.ExecuteUpdate(false);
    }

    [Fact]
    public void TheListAnswersWithItsFirstStatement()
    {
        SessionLocal session = Open();
        CommandList list = Assert.IsType<CommandList>(
            session.PrepareCommand("CREATE TABLE T(ID INT PRIMARY KEY); INSERT INTO T VALUES (1); INSERT INTO T VALUES (2)"));

        Assert.True(list.IsTransactional());
        Assert.False(list.IsQuery());
        Assert.False(list.IsReadOnly());
        // Only the first statement could be prepared; the rest waits as deferred text.
        Assert.Single(list.GetCommands());
        Assert.Equal(H2Sharp.Command.CommandInterface.CreateTable, list.GetCommandType());
        Assert.Equal(0, list.UpdateRows(false).GetUpdateCount());
        // Running the list ran the deferred statements too.
        Assert.Equal(2, QuerySingleInt(session, "SELECT COUNT(*) FROM T"));

        list.Close();
        session.Close();
    }

    [Fact]
    public void AQueryListAnswersTheQueryOnlyUntilTheListRuns()
    {
        SessionLocal session = Open();
        Run(session, "CREATE TABLE T(ID INT PRIMARY KEY)");
        Run(session, "INSERT INTO T VALUES (1), (2)");

        CommandList list = Assert.IsType<CommandList>(
            session.PrepareCommand("SELECT ID FROM T ORDER BY ID; DELETE FROM T"));
        Assert.True(list.IsQuery());

        ResultInterface result = list.Query(-1);
        List<int> ids = [];
        while (result.Next())
        {
            ids.Add(result.CurrentRow()[0]!.GetInt());
        }

        Assert.Equal([1, 2], ids);
        // The query answered; the trailing statement has not run yet.
        Assert.Equal(2, QuerySingleInt(session, "SELECT COUNT(*) FROM T"));

        list.Close();
        session.Close();
    }

    [Fact]
    public void TheListForgetsItsResultWhenATableChanges()
    {
        SessionLocal session = Open();
        Run(session, "CREATE TABLE T(ID INT PRIMARY KEY)");

        CommandList list = Assert.IsType<CommandList>(
            session.PrepareCommand("INSERT INTO T VALUES (1); INSERT INTO T VALUES (2)"));

        // The cache tells the list to drop its result when a table changes; that is a no-op
        // for statements but must not fail.
        Tables.Table table = session.GetDatabase().GetSchema("PUBLIC").GetTableOrView(null, "T");
        list.InvalidateCachedResult(table);

        Assert.Equal(1, list.UpdateRows(false).GetUpdateCount());
        Assert.Equal(2, QuerySingleInt(session, "SELECT COUNT(*) FROM T"));

        list.Close();
        session.Close();
    }

    [Fact]
    public void ADeferredListOfQueriesRunsForEffect()
    {
        SessionLocal session = Open();

        // The two SELECTs cannot be prepared before the CREATE has run, so they are deferred
        // together, parsed as a list of their own and drained for effect.
        CommandList list = Assert.IsType<CommandList>(session.PrepareCommand(
            "CREATE TABLE T(ID INT PRIMARY KEY); INSERT INTO T VALUES (1); "
            + "SELECT ID FROM T; SELECT ID FROM T"));
        Assert.Equal(0, list.UpdateRows(false).GetUpdateCount());
        Assert.Equal(1, QuerySingleInt(session, "SELECT COUNT(*) FROM T"));

        list.Close();
        session.Close();
    }

    [Fact]
    public void TheListReportsTheDependenciesItKnows()
    {
        SessionLocal session = Open();
        CommandList list = Assert.IsType<CommandList>(
            session.PrepareCommand("CREATE TABLE T(ID INT PRIMARY KEY); INSERT INTO T VALUES (1)"));

        // A DDL statement depends on nothing; the deferred part is not prepared yet.
        Assert.Empty(list.GetDependencies());

        list.Close();
        session.Close();
    }

    private static int QuerySingleInt(SessionLocal session, string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        ResultInterface result = container.Query(-1);
        Assert.True(result.Next());
        return result.CurrentRow()[0]!.GetInt();
    }
}
