using System;

namespace H2Sharp.Message;

/// <summary>
/// The levels a trace line can have, and the defaults for where they go.
/// </summary>
/// <remarks>
/// This is the part of <c>TraceSystem</c> that does not write to a file. The reference keeps all of it
/// in one class, but the file machinery wants <c>FileUtils</c> and the JDBC exception type, and
/// neither is in the port yet. What is here — the levels and what they mean — stands on its own: a
/// trace asks whether a line at a level goes anywhere, and that question does not need a file.
/// <para>
/// <see cref="Trace"/> holds one of these per module and answers through it, and the cache reports
/// through <see cref="Trace"/> when it cannot make room. None of those need a file to be decided.
/// </para>
/// </remarks>
public static class TraceLevels
{
    /// <summary>
    /// Ask the parent writer whether this level goes anywhere.
    /// </summary>
    public const int Parent = -1;

    /// <summary>
    /// Nothing is written.
    /// </summary>
    public const int Off = 0;

    /// <summary>
    /// Errors.
    /// </summary>
    public const int Error = 1;

    /// <summary>
    /// Informational lines.
    /// </summary>
    public const int Info = 2;

    /// <summary>
    /// Debugging lines.
    /// </summary>
    public const int Debug = 3;

    /// <summary>
    /// An adapter supplied by the embedder decides.
    /// </summary>
    public const int Adapter = 4;

    /// <summary>
    /// The default for the console: nothing.
    /// </summary>
    public const int DefaultTraceLevelSystemOut = Off;

    /// <summary>
    /// The default for the file: errors.
    /// </summary>
    public const int DefaultTraceLevelFile = Error;

    /// <summary>
    /// The default largest a trace file may grow.
    /// </summary>
    public const long DefaultMaxFileSize = 64 * 1024 * 1024;
}
