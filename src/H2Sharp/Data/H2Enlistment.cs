/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * The System.Transactions notification for one enlisted H2 connection.
 *
 * This is the single-resource case: the ambient transaction asks the connection to commit or
 * roll back, and the session transaction does the work. It is a volatile enlistment, not a
 * durable resource manager; multi-resource/DTC promotion is not part of this slice.
 */
using System.Transactions;

namespace H2Sharp.Data;

/// <summary>
/// Carries the completion of an ambient transaction back to its connection.
/// </summary>
internal sealed class H2Enlistment : IEnlistmentNotification, ISinglePhaseNotification
{
    private H2Connection? connection;

    /// <summary>
    /// Creates the enlistment for a connection.
    /// </summary>
    /// <param name="connection">the enlisted connection</param>
    internal H2Enlistment(H2Connection connection)
    {
        this.connection = connection;
    }

    /// <inheritdoc/>
    public void Prepare(PreparingEnlistment preparingEnlistment)
    {
        // The session transaction is already doing the work; there is no separate prepared
        // state in this single-resource slice.
        preparingEnlistment.Prepared();
    }

    /// <inheritdoc/>
    public void Commit(Enlistment enlistment)
    {
        try
        {
            Complete(commit: true);
        }
        finally
        {
            enlistment.Done();
        }
    }

    /// <inheritdoc/>
    public void Rollback(Enlistment enlistment)
    {
        try
        {
            Complete(commit: false);
        }
        finally
        {
            enlistment.Done();
        }
    }

    /// <inheritdoc/>
    public void InDoubt(Enlistment enlistment)
    {
        try
        {
            Complete(commit: false);
        }
        finally
        {
            enlistment.Done();
        }
    }

    /// <inheritdoc/>
    public void SinglePhaseCommit(SinglePhaseEnlistment singlePhaseEnlistment)
    {
        try
        {
            Complete(commit: true);
            singlePhaseEnlistment.Done();
        }
        catch (System.Exception e)
        {
            singlePhaseEnlistment.Aborted(e);
        }
    }

    private void Complete(bool commit)
    {
        H2Connection? current = connection;
        connection = null;
        current?.CompleteEnlisted(commit);
    }
}
