/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using H2Sharp.Message;

namespace H2Sharp.Data;

/// <summary>
/// The trace levels the engine writes lines at, mirroring <see cref="TraceLevels"/> for the
/// public listener surface.
/// </summary>
public enum H2TraceLevel
{
    /// <summary>Errors.</summary>
    Error = TraceLevels.Error,

    /// <summary>Informational lines.</summary>
    Info = TraceLevels.Info,

    /// <summary>Debugging lines.</summary>
    Debug = TraceLevels.Debug,
}

/// <summary>
/// Receives the database's trace lines. The embedder supplies an implementation through
/// <c>H2DataSource.SetTraceListener</c>; everything the engine traces reports to it.
/// </summary>
/// <remarks>
/// This is the embedder's replacement for the reference's <c>TraceSystem</c>: instead of a
/// trace file or the console, the embedded engine hands its lines to the application. What a
/// level means for delivery is decided together with the database's trace level setting.
/// </remarks>
public interface IH2DatabaseTraceListener
{
    /// <summary>
    /// Writes one trace line for a module.
    /// </summary>
    /// <param name="level">how important the line is</param>
    /// <param name="module">which module writes it (see <see cref="Trace.ModuleNames"/>)</param>
    /// <param name="message">the line</param>
    /// <param name="t">the exception, or null</param>
    void Write(H2TraceLevel level, string module, string message, Exception? t);

    /// <summary>
    /// Whether a level goes anywhere at all. The engine asks before formatting a line, so the
    /// hot path does not pay for messages nobody will read.
    /// </summary>
    /// <param name="level">the level</param>
    /// <returns>true when the line would be received</returns>
    bool IsEnabled(H2TraceLevel level);
}
