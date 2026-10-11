using H2Sharp.Result;

namespace H2Sharp.Indexes;

/// <summary>
/// A cursor is a helper object to iterate through an index.
/// </summary>
/// <remarks>
/// For indexes that are sorted (such as the b tree index), it can iterate to the very end of the
/// index. For other indexes that don't support that (such as a hash index), only one row is returned.
/// The cursor is initially positioned before the first row, that means <see cref="Next"/> must be
/// called before accessing data.
/// </remarks>
public interface Cursor
{
    /// <summary>
    /// Get the complete current row. All columns are available.
    /// </summary>
    /// <returns>the complete row</returns>
    Row? Get();

    /// <summary>
    /// Get the current row. Only the data for indexed columns is available in this row.
    /// </summary>
    /// <returns>the search row</returns>
    SearchRow? GetSearchRow();

    /// <summary>
    /// Skip to the next row if one is available.
    /// </summary>
    /// <returns>true if another row is available</returns>
    bool Next();

    /// <summary>
    /// Skip to the previous row if one is available. No filtering is made here.
    /// </summary>
    /// <returns>true if another row is available</returns>
    bool Previous();
}
