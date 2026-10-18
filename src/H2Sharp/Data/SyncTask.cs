/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * The async surface of ADO.NET over an engine that is synchronous underneath.
 */
using System;
using System.Threading;
using System.Threading.Tasks;

namespace H2Sharp.Data;

/// <summary>
/// Bridges a synchronous call into the task shape the ADO.NET async surface requires.
/// </summary>
/// <remarks>
/// The engine holds its rows in memory, so the work has no I/O to await and the only real
/// asynchrony a cancellation token can buy is refusing before the call starts. A faulted call
/// comes back as a faulted task rather than a synchronous throw, which is what the
/// <see cref="System.Data.Common.DbCommand"/> and
/// <see cref="System.Data.Common.DbDataReader"/> async contracts promise. This is the one
/// copy the shell's five async methods share (issue #12 C6); it deliberately does not use
/// <see cref="Task.Run(Func{Task})"/>, because the contract is for callers that need the API,
/// not for moving CPU work off the calling thread.
/// </remarks>
internal static class SyncTask
{
    /// <summary>
    /// Runs the work now and returns its outcome as a completed task.
    /// </summary>
    /// <typeparam name="T">the result type</typeparam>
    /// <param name="work">the synchronous work</param>
    /// <param name="cancellationToken">the caller's cancellation token</param>
    /// <returns>the completed, cancelled or faulted task</returns>
    internal static Task<T> Run<T>(Func<T> work, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        try
        {
            return Task.FromResult(work());
        }
        catch (Exception e)
        {
            return Task.FromException<T>(e);
        }
    }
}
