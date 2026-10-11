/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.DefineCommand.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A statement that changes the catalogue rather than the rows.
 *
 * The two halves of the catalogue layer differ in one thing that is worth stating, because
 * every subclass inherits it and nobody has to say it again: a statement that defines or drops
 * an object is not transactional by default. It commits whatever is open and then runs on its
 * own, because the object it creates has to be visible to whatever comes after it, and a
 * statement whose effect dies with the transaction it failed in would leave the catalogue
 * describing something that is not there. A subclass that really is transactional — one whose
 * whole meaning is a change inside a transaction — says so with setTransactional, and the only
 * one that does is the pair that creates and drops a constraint together.
 */
using H2Sharp.Result;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// A statement that creates or drops an object.
/// </summary>
public abstract class DefineCommand : Prepared
{
    /// <summary>
    /// Whether this statement runs inside the caller's transaction.
    /// </summary>
    protected bool transactional;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    protected DefineCommand(Engine.SessionLocal session)
        : base(session)
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Not read only: it changes the catalogue, whatever else it does.
    /// </remarks>
    public override bool IsReadOnly() => false;

    /// <inheritdoc/>
    public override ResultInterface QueryMeta()
        => throw new TODOException("DefineCommand.queryMeta: the schema is not ported");

    /// <summary>
    /// Sets whether this statement runs inside the caller's transaction.
    /// </summary>
    /// <param name="transactional">true to be transactional</param>
    public void SetTransactional(bool transactional)
    {
        this.transactional = transactional;
    }

    /// <inheritdoc/>
    public override bool IsTransactional() => transactional;

    /// <inheritdoc/>
    public override bool IsRetryable() => false;
}
