/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.NoOperation.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * An empty statement, or one that has no effect.
 *
 * It exists so that the parser always has something to return. A statement may be empty — the
 * text was ";" or a comment — and "no statement" has to be a statement that runs and answers
 * nothing, because everything downstream asks the statement what it is and there is nothing else
 * to ask.
 *
 * `queryMeta()` answers null rather than an empty result. That is not an oversight: there are no
 * columns to describe, and a result with no columns would be a thing a caller could read rows
 * from and find none — which is a different answer from "there is no result".
 *
 * It is transactional even though it changes nothing, because "does this statement run inside a
 * transaction" is a question about how it is run, not about what it does.
 */
using H2Sharp.Engine;
using H2Sharp.Result;

namespace H2Sharp.Command.Dml;

/// <summary>
/// Represents an empty statement or a statement that has no effect.
/// </summary>
public class NoOperation : Prepared
{
    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    public NoOperation(SessionLocal session)
        : base(session)
    {
    }

    /// <inheritdoc/>
    public override long Update() => 0;

    /// <inheritdoc/>
    public override bool IsTransactional() => true;

    /// <inheritdoc/>
    public override bool NeedRecompile() => false;

    /// <inheritdoc/>
    public override bool IsReadOnly() => true;

    /// <inheritdoc/>
    /// <remarks>
    /// Null, not an empty result. There are no columns to describe, and a result with no columns
    /// would be a thing a caller could read rows from and find none — a different answer from
    /// "there is no result at all".
    /// </remarks>
    public override ResultInterface? QueryMeta() => null;

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.NoOperation;
}
