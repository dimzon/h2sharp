using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// The base class for rows stored in a table, and for partial rows stored in the index.
/// </summary>
/// <remarks>
/// A row is itself a value, which is why it answers the value questions. Two of those answers are a
/// refusal, and the refusal is the point: a row compares equal to nothing and equals nothing, because
/// two rows holding equal values are the same contents and are not the same row. Implementing either
/// would put a row into a hash table or an equality test where it does not belong.
/// <para>
/// The row identifier lives at index <see cref="RowidIndex"/>, which is negative, so it is
/// unreachable from a loop over the columns and cannot collide with one.
/// </para>
/// </remarks>
public abstract class SearchRow : Value
{
    /// <summary>
    /// Index of a virtual "_ROWID_" column within a row or a table
    /// </summary>
    public const int RowidIndex = -1;

    /// <summary>
    /// If the key is this value, then the key is considered equal to all other keys, when comparing.
    /// </summary>
    public static long MatchAllRowKey = long.MinValue + 1;

    /// <summary>
    /// The constant that means "memory usage is unknown and needs to be calculated first".
    /// </summary>
    public const int MemoryCalculate = -1;

    /// <summary>
    /// The row key.
    /// </summary>
    protected long key;

    /// <summary>
    /// Get the column count.
    /// </summary>
    /// <returns>the column count</returns>
    public abstract int GetColumnCount();

    /// <summary>
    /// Determine if specified column contains NULL.
    /// </summary>
    /// <param name="index">column index</param>
    /// <returns>true if NULL</returns>
    public virtual bool IsNull(int index) => GetValue(index) == ValueNull.Instance;

    /// <summary>
    /// Get the value for the column.
    /// </summary>
    /// <param name="index">the column number (starting with 0)</param>
    /// <returns>the value</returns>
    public abstract Value? GetValue(int index);

    /// <summary>
    /// Set the value for given column.
    /// </summary>
    /// <param name="index">the column number (starting with 0)</param>
    /// <param name="v">the new value</param>
    public abstract void SetValue(int index, Value v);

    /// <summary>
    /// Set the unique key of the row.
    /// </summary>
    /// <param name="key">the key</param>
    public void SetKey(long key) => this.key = key;

    /// <summary>
    /// Get the unique key of the row.
    /// </summary>
    /// <returns>the key</returns>
    public long GetKey() => key;

    /// <summary>
    /// Copy all relevant values from the source to this row.
    /// </summary>
    /// <param name="source">source of column values</param>
    public abstract void CopyFrom(SearchRow source);

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeRowEmpty;

    /// <inheritdoc/>
    public override int GetValueType() => Value.Row;

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append("ROW (");
        for (int index = 0, count = GetColumnCount(); index < count; index++)
        {
            if (index != 0)
            {
                _ = builder.Append(", ");
            }

            _ = GetValue(index)!.GetSql(builder, sqlFlags);
        }

        return _ = builder.Append(')');
    }

    /// <inheritdoc/>
    public override string? GetString() => GetTraceSql();

    /// <inheritdoc/>
    public override int GetHashCode() => throw new NotSupportedException();

    /// <inheritdoc/>
    public override bool Equals(object? other) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value v, CompareMode mode, CastDataProvider? provider)
        => throw new NotSupportedException();
}
