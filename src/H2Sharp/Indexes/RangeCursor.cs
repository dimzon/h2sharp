using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.Indexes;

/// <summary>
/// The cursor implementation for the range index.
/// </summary>
/// <remarks>
/// Package-private in the reference and internal here, because nothing outside the package builds one.
/// The oracle program is generated into this same package so that it can, which is the reason this
/// class is worth testing before the rest of the index exists.
/// </remarks>
internal sealed class RangeCursor : Cursor
{
    private bool beforeFirst = true;

    private long current;

    private Row? currentRow;

    private readonly long start, end, step;

    /// <summary>
    /// A cursor over the numbers from <paramref name="start"/> to <paramref name="end"/>.
    /// </summary>
    /// <param name="start">first value</param>
    /// <param name="end">last value, inclusive</param>
    /// <param name="step">the step, whose sign decides the direction</param>
    internal RangeCursor(long start, long end, long step)
    {
        this.start = start;
        this.end = end;
        this.step = step;
    }

    /// <inheritdoc/>
    public Row? Get() => currentRow;

    /// <inheritdoc/>
    public SearchRow? GetSearchRow() => currentRow;

    /// <inheritdoc/>
    /// <remarks>
    /// The row is built <em>before</em> the question is asked, so a cursor that answers "no" is left
    /// holding a row from beyond the end. That is the reference's order and it is kept: a caller that
    /// stops on "no" is not reading the row, and a caller that does read it is out of contract either
    /// way.
    /// </remarks>
    public bool Next()
    {
        if (beforeFirst)
        {
            beforeFirst = false;
            current = start;
        }
        else
        {
            current += step;
        }

        currentRow = Row.Get([ValueBigint.Get(current)], 1);
        return step > 0 ? current <= end : current >= end;
    }

    /// <inheritdoc/>
    public bool Previous() => throw DbException.GetInternalError($"{this}");
}
