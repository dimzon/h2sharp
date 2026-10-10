using H2Sharp.Message;

namespace H2Sharp.Util;

/// <summary>
/// Writes a cache record back when the cache drops it.
/// </summary>
/// <remarks>
/// The cache decides what to drop and when; this decides what "dropped" means for the caller. Writing
/// the record back is not the same as forgetting it, and the distinction is why the cache does not do
/// it itself.
/// </remarks>
public interface CacheWriter
{
    /// <summary>
    /// Writes a dropped record back.
    /// </summary>
    /// <param name="entry">the record</param>
    void WriteBack(CacheObject entry);

    /// <summary>
    /// Flushes whatever log the records were written through.
    /// </summary>
    void FlushLog();

    /// <summary>
    /// Where the cache reports that it cannot make room.
    /// </summary>
    /// <returns>the trace</returns>
    Trace GetTrace();
}
