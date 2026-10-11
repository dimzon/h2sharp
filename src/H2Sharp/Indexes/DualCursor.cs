using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.Indexes;

/// <summary>
/// The cursor implementation for the DUAL index.
/// </summary>
/// <remarks>
/// The DUAL table has exactly one row and no columns, so this cursor yields that one empty row and
/// then stops. Package-private in the reference and internal here, for the same reason as
/// <see cref="RangeCursor"/>.
/// </remarks>
internal sealed class DualCursor : Cursor
{
    private Row? currentRow;

    /// <inheritdoc/>
    public Row? Get() => currentRow;

    /// <inheritdoc/>
    public SearchRow? GetSearchRow() => currentRow;

    /// <inheritdoc/>
    /// <remarks>
    /// Unlike <see cref="SingleRowCursor"/>, this one keeps its row after answering "no". The
    /// difference is not an accident in the reference and not worth smoothing over here: one cursor is
    /// consumed by a loop that stops, the other is asked twice by a caller that wants to know there is
    /// no more.
    /// </remarks>
    public bool Next()
    {
        if (currentRow is null)
        {
            currentRow = Row.Get(Value.EmptyValues, 1);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool Previous() => throw DbException.GetInternalError($"{this}");
}
