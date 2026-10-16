/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * A transaction over a connection.
 *
 * The state machine is the whole contract the conformance suite checks: begin on a closed
 * connection or beside a live one refuses, commit and rollback clear the connection, a
 * second completion refuses, and anything after disposal is ObjectDisposed. The engine
 * half opens the session transaction the statements then share and commits or rolls it
 * back; per-statement scopes inside table operations still commit on their own, so an
 * explicit transaction isolates only what runs through the session transaction — which
 * is where the gap is written down, not where it is hidden.
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

    private MvStore.Tx.Transaction? engineTransaction;

    private bool completed;

    private bool disposed;

    /// <summary>
    /// Begins a transaction on the given connection.
    /// </summary>
    /// <param name="connection">the connection</param>
    /// <param name="isolationLevel">the isolation level</param>
    /// <param name="engineTransaction">the session transaction the statements share</param>
    internal H2Transaction(
        H2Connection connection, IsolationLevel isolationLevel, MvStore.Tx.Transaction engineTransaction)
    {
        this.connection = connection;
        IsolationLevel = isolationLevel;
        this.engineTransaction = engineTransaction;
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
        engineTransaction?.Commit();
        engineTransaction = null;
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
        engineTransaction?.Rollback();
        engineTransaction = null;
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
                engineTransaction?.Rollback();
            }

            engineTransaction = null;
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
