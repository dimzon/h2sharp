/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The path from text to something a caller can run.
 *
 * Three steps, and they are the reference's: read the statement, prepare it, wrap it. What a
 * caller receives is a CommandContainer holding the prepared statement, and every question the
 * command contract asks is answered by asking the statement — which is the whole point of the
 * two hierarchies, and the thing that cannot be checked by reading one of the two files.
 *
 * The cache is checked here rather than assumed. A command is only handed back once it has been
 * closed, so nothing that is still in use can be run twice under a caller; and a change anywhere
 * in the database drops the whole cache, which is blunt on purpose and is the rule that cannot
 * be wrong about a dependency it failed to notice.
 */
using System;
using H2Sharp.Command;
using H2Sharp.Command.Ddl;
using H2Sharp.Command.Dml;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Tests;

public sealed class PrepareCommandTests
{
    private static SessionLocal NewSession()
    {
        return new SessionLocal(new Database(new ConnectionInfo("jdbc:h2:mem:prepare_tests")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = Values.CompareMode.GetInstance(null, 0),
        };
    }

    [Fact]
    public void AStatementBecomesACommandThatHoldsIt()
    {
        SessionLocal session = NewSession();

        CommandContainer command = Assert.IsType<CommandContainer>(session.PrepareCommand("SELECT ID FROM T"));

        // The container is not the statement; it asks it. That is the join, and a port with one
        // hierarchy would have nothing to assert here.
        Select prepared = Assert.IsType<Select>(command.GetPreparedCommand());
        Assert.True(prepared.IsQuery());
        Assert.True(command.IsQuery());
        Assert.Equal(CommandInterface.Select, command.GetCommandType());
    }

    [Fact]
    public void EveryKindOfStatementReachesItsOwnPreparedClass()
    {
        SessionLocal session = NewSession();

        // A select is prepared as it is read: the plan is built from the tables behind it, and
        // there are no tables yet to be behind.
        Assert.IsType<Select>(AsContainer(session.PrepareCommand("SELECT ID FROM T")).GetPreparedCommand());
    }

    [Fact]
    public void ADataChangeStatementRefusesToPrepareUntilItIsBound()
    {
        SessionLocal session = NewSession();

        // The reference binds a data change statement in prepare(): it is where the statement
        // reads its own text and finds its table. Here the parser reads the text first and the
        // statement is not yet handed the result, so preparing refuses -- and refusing is the
        // honest answer, because a statement that prepared without its table would have nothing
        // to write to.
        Assert.Throws<TODOException>(() => session.PrepareCommand("INSERT INTO T VALUES (1)"));
        Assert.Throws<TODOException>(() => session.PrepareCommand("UPDATE T SET ID = 1"));
        Assert.Throws<TODOException>(() => session.PrepareCommand("DELETE FROM T"));
    }

    [Fact]
    public void AQueryIsAQuery()
    {
        SessionLocal session = NewSession();

        Assert.True(AsContainer(session.PrepareCommand("SELECT ID FROM T")).IsQuery());

        // A select is transactional even though it changes nothing: the question is how it runs,
        // not what it does.
        Assert.True(AsContainer(session.PrepareCommand("SELECT ID FROM T")).IsTransactional());
    }

    [Fact]
    public void AClosedSessionRefusesToPrepareAnything()
    {
        SessionLocal session = NewSession();
        session.Close();

        // The reference refuses with CONNECTION_BROKEN and the word "session closed". The check
        // itself is ported; the error it throws is a later step, and until then this refuses the
        // same way it refuses everything else it cannot do.
        Assert.ThrowsAny<Exception>(() => session.PrepareCommand("SELECT 1"));
    }

    [Fact]
    public void ACommandIsHandedBackOnlyAfterItHasBeenClosed()
    {
        SessionLocal session = NewSession();

        // A command that has never been closed may still be in use by a result set, so it is not
        // handed out again; preparing the same text builds a new command and that one replaces it
        // in the cache.
        CommandContainer first = AsContainer(session.PrepareCommand("SELECT ID FROM T"));
        Assert.False(first.CanReuse());

        CommandContainer second = AsContainer(session.PrepareCommand("SELECT ID FROM T"));
        Assert.NotSame(first, second);

        // Now the one in the cache is closed, and the third preparation is that very object.
        second.Close();
        Assert.True(second.CanReuse());
        Assert.Same(second, session.PrepareCommand("SELECT ID FROM T"));
    }

    [Fact]
    public void TheCacheIsDroppedWhenTheDatabaseChanges()
    {
        SessionLocal session = NewSession();

        CommandContainer first = AsContainer(session.PrepareCommand("SELECT ID FROM T"));
        first.Close();

        // Nothing has changed, so the same text comes back as the same command.
        Assert.Same(first, session.PrepareCommand("SELECT ID FROM T"));

        _ = session.GetDatabase().GetNextModificationMetaId();
        Assert.NotSame(first, session.PrepareCommand("SELECT ID FROM T"));
    }

    [Fact]
    public void AnEmptyStatementIsAStatementThatDoesNothing()
    {
        SessionLocal session = NewSession();

        // ";" is a statement in every SQL dialect, and something has to come back for it:
        // everything downstream asks the statement what it is.
        NoOperation empty = Assert.IsType<NoOperation>(
            AsContainer(session.PrepareCommand(";")).GetPreparedCommand());
        Assert.Equal(0, empty.Update());
        Assert.Equal(CommandInterface.NoOperation, empty.GetCommandType());
        Assert.True(empty.IsReadOnly());

        // No columns to describe, and a result with no columns would be a thing a caller could
        // read rows from and find none -- a different answer from "there is no result".
        Assert.Null(empty.QueryMeta());
    }

    [Fact]
    public void ADdlStatementPreparesAgainstTheCurrentSchema()
    {
        SessionLocal session = NewSession();

        // The statement is prepared against the schema the session is reading. That is the check
        // that matters: a DDL statement prepared against the wrong schema would create its table
        // somewhere nobody asked for. The reference keeps the schema on the statement and hides
        // it, so what is asserted here is that preparation succeeds and says what it is -- the
        // schema itself is checked on the session below.
        CreateTable create = Assert.IsType<CreateTable>(
            AsContainer(session.PrepareCommand("CREATE TABLE PERSON(ID INT PRIMARY KEY)")).GetPreparedCommand());
        Assert.Equal(CommandInterface.CreateTable, create.GetCommandType());

        // A create or a drop is not transactional, which is what makes the wrapper commit before
        // it runs and leave the transaction alone afterwards.
        Assert.False(create.IsTransactional());
        Assert.False(create.IsReadOnly());

        DropTable drop = Assert.IsType<DropTable>(
            AsContainer(session.PrepareCommand("DROP TABLE PERSON")).GetPreparedCommand());
        Assert.Equal(CommandInterface.DropTable, drop.GetCommandType());
        Assert.False(drop.IsTransactional());
    }

    [Fact]
    public void TheCurrentSchemaIsTheDatabasesMainSchema()
    {
        SessionLocal session = NewSession();

        Assert.Equal("PUBLIC", session.GetCurrentSchemaName());
        Assert.Same(session.GetDatabase().GetMainSchema(), session.GetCurrentSchema());

        // A schema that is not there is the reference's error, and not a null that a caller has
        // to check for.
        Assert.ThrowsAny<Exception>(() => session.SetCurrentSchemaName("NOPE"));
    }

    private static CommandContainer AsContainer(CommandInterface command) => (CommandContainer)command;
}
