/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using H2Sharp.Data;

namespace H2Sharp.Message;

/// <summary>
/// Presents the embedder's <see cref="IH2DatabaseTraceListener"/> as the writer the module
/// traces write through.
/// </summary>
/// <remarks>
/// The reference's <c>TraceWriterAdapter</c> does the same for a logging framework: the trace
/// does not know where the lines end up, and the listener does not know the trace asked.
/// </remarks>
internal sealed class TraceListenerWriter(IH2DatabaseTraceListener listener) : TraceWriter
{
    /// <inheritdoc/>
    public void SetName(string name)
    {
    }

    /// <inheritdoc/>
    public void Write(int level, string module, string s, Exception? t)
        => listener.Write((H2TraceLevel)level, module, s, t);

    /// <inheritdoc/>
    public void Write(int level, int moduleId, string s, Exception? t)
        => listener.Write((H2TraceLevel)level, Trace.ModuleNames[moduleId], s, t);

    /// <inheritdoc/>
    public bool IsEnabled(int level) => listener.IsEnabled((H2TraceLevel)level);
}
