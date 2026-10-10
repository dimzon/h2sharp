/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * A transaction over a connection.
 *
 * Commit and Rollback refuse for now, and the refusal says which layer is missing rather than
 * pretending to have saved something: a transaction that reports a commit without the undo log
 * behind it is the worst possible answer, because the caller goes on to believe the data is
 * safe.
 */
using System;
using System.Data;
using System.Data.Common;
using H2Sharp.Engine;

namespace H2Sharp.Data;

/// <summary>
/// A transaction over a connection.
/// </summary>
public sealed class H2Transaction : DbTransaction
{
    private H2Connection? connection;

    /// <summary>
    /// Begins a transaction on the given connection.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="isolationLevel">the isolation level</param>
    public H2Transaction(H2Connection connection, IsolationLevel isolationLevel)
    {
        this.connection = connection;
        IsolationLevel = isolationLevel;
    }

    /// <inheritdoc/>
    protected override DbConnection? DbConnection => connection;

    /// <inheritdoc/>
    public override IsolationLevel IsolationLevel { get; }

    /// <inheritdoc/>
    public override void Commit()
        => throw new TODOException("H2Transaction.Commit: the transaction layer is not ported");

    /// <inheritdoc/>
    public override void Rollback()
        => throw new TODOException("H2Transaction.Rollback: the transaction layer is not ported");
}
