/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The dependency walk of an UPDATE: the condition and the set clauses are visited, and what
 * the visitor finds — a sequence behind NEXT VALUE FOR, in a plain assignment as well as in
 * an array element one — lands in the statement's dependencies. A plain update collects
 * nothing.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Command;
using H2Sharp.Command.Dml;
using H2Sharp.Engine;
using H2Sharp.Schemas;
using Xunit;

namespace H2Sharp.Tests;

public sealed class UpdateDependenciesPortTests
{
    private readonly Database database;
    private readonly SessionLocal session;

    public UpdateDependenciesPortTests()
    {
        database = new Database(ConnectionInfo.Memory("update_deps_" + Guid.NewGuid().ToString("N")));
        session = new SessionLocal(database, "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
        };
        Execute("CREATE SEQUENCE SEQ");
        Execute("CREATE TABLE T(ID INT PRIMARY KEY, V INT, A INT ARRAY)");
    }

    [Fact]
    public void TheWalkCollectsTheSequenceAndNothingElse()
    {
        Sequence sequence = database.GetSchema("PUBLIC").FindSequence("SEQ")
            ?? throw new InvalidOperationException("the sequence is missing");

        Update update = PrepareUpdate("UPDATE T SET V = NEXT VALUE FOR SEQ");
        HashSet<DbObject> dependencies = [];
        update.CollectDependencies(dependencies);
        Assert.Contains(sequence, dependencies);

        // A plain assignment collects nothing.
        update = PrepareUpdate("UPDATE T SET V = 1");
        dependencies = [];
        update.CollectDependencies(dependencies);
        Assert.Empty(dependencies);

        // The array element form walks its indexes through the same visitor.
        update = PrepareUpdate("UPDATE T SET A[1] = NEXT VALUE FOR SEQ");
        dependencies = [];
        update.CollectDependencies(dependencies);
        Assert.Contains(sequence, dependencies);

        // The condition is walked as well.
        Execute("CREATE TABLE U(ID INT PRIMARY KEY, W INT)");
        update = PrepareUpdate("UPDATE T SET V = 1 WHERE ID IN (SELECT W FROM U)");
        dependencies = [];
        update.CollectDependencies(dependencies);
        Assert.Empty(dependencies);

        session.Close();
    }

    private void Execute(string sql)
    {
        CommandContainer container = Assert.IsType<CommandContainer>(session.PrepareCommand(sql));
        _ = container.ExecuteUpdate(false);
    }

    private Update PrepareUpdate(string sql)
        => Assert.IsType<Update>(((CommandContainer)session.PrepareCommand(sql)).GetPreparedCommand());
}
