/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

namespace H2Sharp.Message;

/// <summary>
/// Receives the engine's trace lines. The embedder supplies an implementation through
/// <c>H2DataSource.SetTraceListener</c>, and the whole database reports to it.
/// </summary>
/// <remarks>
/// This is the embedder's replacement for the reference's <c>TraceSystem</c>: the reference
/// writes its lines to a trace file and the console, while an embedded engine hands them to
/// the application. The levels are the engine's own (see <see cref="TraceLevels"/>); what a
/// level means for delivery is decided together with the database's trace level setting.
/// </remarks>
public interface ITraceListener
{
    /// <summary>
    /// Writes one trace line for a module.
    /// </summary>
    /// <param name="level">how important the line is (see <see cref="TraceLevels"/>)</param>
    /// <param name="module">which module writes it (see <see cref="Trace.ModuleNames"/>)</param>
    /// <param name="message">the line</param>
    /// <param name="t">the exception, or null</param>
    void Write(int level, string module, string message, Exception? t);

    /// <summary>
    /// Whether a level goes anywhere at all. The engine asks before formatting a line, so the
    /// hot path does not pay for messages nobody will read.
    /// </summary>
    /// <param name="level">the level</param>
    /// <returns>true when the line would be received</returns>
    bool IsEnabled(int level);
}
