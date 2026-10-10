using System;

namespace H2Sharp.Message;

/// <summary>
/// Where trace lines go: the console, a file, or an adapter supplied by the embedder.
/// </summary>
/// <remarks>
/// The reference names a single implementation, and every place a module wants a trace from holds one
/// of these rather than the concrete system. That indirection is the point here too: a caller writing
/// to a trace does not know and does not have to know where the lines end up.
/// </remarks>
internal interface TraceWriter
{
    /// <summary>
    /// Names the writer, for an adapter that writes somewhere the trace does not know about.
    /// </summary>
    /// <param name="name">the name</param>
    void SetName(string name);

    /// <summary>
    /// Writes one line for a module.
    /// </summary>
    /// <param name="level">how important it is</param>
    /// <param name="module">which module</param>
    /// <param name="s">the line</param>
    /// <param name="t">the exception, or null</param>
    void Write(int level, string module, string s, Exception? t);

    /// <summary>
    /// Writes one line for a module id.
    /// </summary>
    /// <param name="level">how important it is</param>
    /// <param name="moduleId">which module</param>
    /// <param name="s">the line</param>
    /// <param name="t">the exception, or null</param>
    void Write(int level, int moduleId, string s, Exception? t);

    /// <summary>
    /// Whether a level is written anywhere at all.
    /// </summary>
    /// <param name="level">the level</param>
    /// <returns>true when it is written</returns>
    bool IsEnabled(int level);
}
