using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// The default implementation of a row in a table.
/// </summary>
public class DefaultRow : Row
{
    /// <summary>
    /// The constant that means "memory usage is unknown and needs to be calculated first".
    /// </summary>
    /// <remarks>
    /// The reference declares this on both this class and <see cref="SearchRow"/>, with the same
    /// value, so a reader who has met one has met the other. It is repeated rather than inherited
    /// on purpose: shadowing is what the reference does.
    /// </remarks>
    public new const int MemoryCalculate = -1;

    /// <summary>
    /// The values of the row (one entry per column).
    /// </summary>
    protected readonly Value?[] data;

    private int memory;

    /// <summary>
    /// A row of a given width with nothing in it.
    /// </summary>
    /// <param name="columnCount">how many columns</param>
    internal DefaultRow(int columnCount)
        : this(new Value?[columnCount], MemoryCalculate)
    {
    }

    /// <summary>
    /// A row over the given values, with its memory not yet calculated.
    /// </summary>
    /// <param name="data">values of columns, or null</param>
    public DefaultRow(Value?[] data)
        : this(data, MemoryCalculate)
    {
    }

    /// <summary>
    /// A row over the given values, with a known memory.
    /// </summary>
    /// <param name="data">values of columns, or null</param>
    /// <param name="memory">used memory</param>
    public DefaultRow(Value?[] data, int memory)
    {
        this.data = data;
        this.memory = memory;
    }

    /// <inheritdoc/>
    public override Value? GetValue(int i)
        => i == RowidIndex ? ValueBigint.Get(key) : data[i];

    /// <inheritdoc/>
    public override void SetValue(int i, Value v)
    {
        if (i == RowidIndex)
        {
            key = v.GetLong();
        }
        else
        {
            data[i] = v;
        }
    }

    /// <inheritdoc/>
    public override int GetColumnCount() => data.Length;

    /// <inheritdoc/>
    public override int GetMemory()
    {
        if (memory != MemoryCalculate)
        {
            return memory;
        }

        return memory = CalculateMemory();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        StringBuilder builder = new();
        _ = builder.Append("( /* key:").Append(key).Append(" */ ");
        for (int i = 0, length = data.Length; i < length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            Value? v = data[i];
            _ = builder.Append(v is null ? "null" : v.GetTraceSql());
        }

        return _ = builder.Append(')').ToString();
    }

    /// <summary>
    /// Calculate the estimated memory used for this row, in bytes.
    /// </summary>
    /// <returns>the memory</returns>
    protected int CalculateMemory()
    {
        int m = Constants.MemoryRow + Constants.MemoryArray + (data.Length * Constants.MemoryPointer);
        foreach (Value? v in data)
        {
            if (v is not null)
            {
                m += v.GetMemory();
            }
        }

        return m;
    }

    /// <inheritdoc/>
    public override Value?[] GetValueList() => data;

    /// <inheritdoc/>
    public override bool HasSharedData(Row other) => other is DefaultRow row && ReferenceEquals(data, row.data);

    /// <inheritdoc/>
    public override void CopyFrom(SearchRow source)
    {
        SetKey(source.GetKey());
        for (int i = 0; i < GetColumnCount(); i++)
        {
            SetValue(i, source.GetValue(i)!);
        }
    }
}
