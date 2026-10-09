using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// An object where rows are written to.
/// </summary>
public interface ResultTarget
{
    /// <summary>
    /// Add the row to the result set.
    /// </summary>
    /// <param name="values">the values</param>
    void AddRow(params Value[] values);

    /// <summary>
    /// Get the number of rows.
    /// </summary>
    /// <returns>the number of rows</returns>
    long GetRowCount();

    /// <summary>
    /// A hint that sorting, offset and limit may be ignored by this result because they were applied
    /// during the query.
    /// </summary>
    /// <remarks>
    /// This is useful for the WITH TIES clause, because the result may contain tied rows above the
    /// limit.
    /// </remarks>
    void LimitsWereApplied();
}
