/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * A transaction over a connection.
 *
 * The state machine is the whole contract the conformance suite checks: begin on a closed
 * connection or beside a live one refuses, commit and rollback clear the connection, a
 * second completion refuses, and anything after disposal is ObjectDisposed.
 *
 * The engine half is the session transaction: begin switches the session out of auto-commit,
 * and commit or rollback finishes that transaction. The statements run on it through the
 * session, so an explicit transaction really does isolate what runs inside it.
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

    private bool completed;

    private bool disposed;

    /// <summary>
    /// Begins a transaction on the given connection.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="isolationLevel">the isolation level</param>
    internal H2Transaction(H2Connection connection, IsolationLevel isolationLevel)
    {
        this.connection = connection;
        IsolationLevel = isolationLevel;
    }

    /// <inheritdoc/>
    protected override DbConnection? DbConnection => connection;

    /// <summary>
    /// Gets the connection this transaction belongs to, while it belongs to one.
    /// </summary>
    internal H2Connection? OwningConnection => connection;

    /// <inheritdoc/>
    public override IsolationLevel IsolationLevel { get; }

    /// <inheritdoc/>
    public override void Commit()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(H2Transaction));
        }

        if (completed)
        {
            throw new InvalidOperationException("The transaction is already completed");
        }

        completed = true;
        connection?.Session?.Commit(false);
        Detach();
    }

    /// <inheritdoc/>
    public override void Rollback()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(H2Transaction));
        }

        if (completed)
        {
            throw new InvalidOperationException("The transaction is already completed");
        }

        completed = true;
        connection?.Session?.Rollback();
        Detach();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            if (!completed)
            {
                completed = true;
                connection?.Session?.Rollback();
            }

            Detach();
        }

        base.Dispose(disposing);
    }

    private void Detach()
    {
        H2Connection? current = connection;
        connection = null;
        current?.ClearTransaction(this);
    }
}
