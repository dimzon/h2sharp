using System;
using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// A simple row that contains data for only one column.
/// </summary>
/// <remarks>
/// A row of the index, where a row is mostly the key: the one column that is present is kept in a
/// single slot, and every other column reads back as absent. What "absent" means is worth stating,
/// because it is not the same as NULL and the two are not interchangeable here.
/// </remarks>
public class SimpleRowValue : SearchRow
{
    private int index;

    private readonly int virtualColumnCount;

    private Value? data;

    /// <summary>
    /// An empty row of a given width.
    /// </summary>
    /// <param name="columnCount">how many columns the row pretends to have</param>
    public SimpleRowValue(int columnCount) => virtualColumnCount = columnCount;

    /// <summary>
    /// An empty row of a given width, with the filled column named.
    /// </summary>
    /// <param name="columnCount">how many columns the row pretends to have</param>
    /// <param name="index">which column holds the value</param>
    public SimpleRowValue(int columnCount, int index)
    {
        virtualColumnCount = columnCount;
        this.index = index;
    }

    /// <inheritdoc/>
    public override int GetColumnCount() => virtualColumnCount;

    /// <inheritdoc/>
    public override Value? GetValue(int idx)
    {
        if (idx == RowidIndex)
        {
            return ValueBigint.Get(GetKey());
        }

        return idx == index ? data : null;
    }

    /// <inheritdoc/>
    public override void SetValue(int idx, Value v)
    {
        if (idx == RowidIndex)
        {
            SetKey(v.GetLong());
        }

        index = idx;
        data = v;
    }

    /// <inheritdoc/>
    public override string ToString()
        => "( /* " + key + " */ " + (data is null ? "null" : data.GetTraceSql()) + " )";

    /// <inheritdoc/>
    public override int GetMemory() => Constants.MemoryRow + (data is null ? 0 : data.GetMemory());

    /// <inheritdoc/>
    /// <remarks>
    /// The reference widens this one, because a row with a single slot has to answer it without the
    /// value it would otherwise compare. Absent, NULL and a value somewhere else are three
    /// different answers here.
    /// </remarks>
    public override bool IsNull(int index) => index != this.index || data is null || data == ValueNull.Instance;

    /// <inheritdoc/>
    public override void CopyFrom(SearchRow source)
    {
        SetKey(source.GetKey());
        SetValue(index, source.GetValue(index)!);
    }
}
