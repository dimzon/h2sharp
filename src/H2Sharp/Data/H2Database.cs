/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The ambient database context of a running .NET function. A scalar or table UDF that needs
 * to run SQL on the statement's own session asks for CurrentSessionConnection; the
 * connection it gets is a borrowed view of that session, bound to the running transaction.
 */
using System;
using System.Threading;
using H2Sharp.Engine;

namespace H2Sharp.Data;

/// <summary>
/// The database context of the currently running .NET function.
/// </summary>
/// <remarks>
/// The context exists only while the engine calls a registered scalar or table UDF. The
/// connection is created lazily, on the first access: a function that does not run SQL does
/// not pay for a connection and does not change the session. The connection it answers is a
/// borrowed view of the statement's session: closing it does not close the session, and
/// after the call it is closed. Every statement run on it belongs to the transaction the
/// calling statement already runs in; the transaction cannot be begun, committed, rolled
/// back, enlisted or changed, and non-transactional statements are refused. The state
/// travels in <see cref="AsyncLocal{T}"/> so a nested function sees and restores its caller's
/// context.
/// </remarks>
public static class H2Database
{
    private static readonly AsyncLocal<SessionScope?> Current = new();

    /// <summary>
    /// The connection of the statement whose .NET function is running, or null outside a
    /// function call.
    /// </summary>
    public static H2Connection? CurrentSessionConnection => Current.Value?.Connection;

    /// <summary>
    /// Makes the session current for the duration of a function call.
    /// </summary>
    /// <param name="session">the session of the running statement</param>
    /// <returns>the scope that restores the previous context</returns>
    internal static IDisposable EnterSession(SessionLocal session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var scope = new SessionScope(session, Current.Value);
        Current.Value = scope;
        return scope;
    }

    private sealed class SessionScope : IDisposable
    {
        private readonly SessionLocal session;

        private readonly SessionScope? previous;

        private H2Connection? connection;

        private bool initialized;

        private bool wasAutoCommit;

        private bool disposed;

        internal SessionScope(SessionLocal session, SessionScope? previous)
        {
            this.session = session;
            this.previous = previous;
        }

        internal H2Connection Connection
        {
            get
            {
                if (!initialized)
                {
                    initialized = true;
                    wasAutoCommit = session.GetAutoCommit();

                    // Every statement of the context runs inside the calling statement's
                    // transaction: with auto-commit off, a nested statement neither starts
                    // nor finishes one.
                    if (wasAutoCommit)
                    {
                        session.SetAutoCommit(false);
                    }

                    connection = H2Connection.BorrowSessionContext(session);
                }

                return connection!;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (initialized)
            {
                connection!.Close();
                if (session.IsOpen())
                {
                    session.SetAutoCommit(wasAutoCommit);
                }
            }

            Current.Value = previous;
        }
    }
}
