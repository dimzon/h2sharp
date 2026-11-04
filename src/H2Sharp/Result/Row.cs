using System;
using System.Collections.Generic;
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// Represents a row in a table.
/// </summary>
public abstract class Row : SearchRow
{
    /// <summary>
    /// Creates a new row.
    /// </summary>
    /// <param name="data">values of columns, or null</param>
    /// <param name="memory">used memory</param>
    /// <returns>the allocated row</returns>
    public static Row Get(Value?[] data, int memory) => new DefaultRow(data, memory);

    /// <summary>
    /// Creates a new row with the specified key.
    /// </summary>
    /// <param name="data">values of columns, or null</param>
    /// <param name="memory">used memory</param>
    /// <param name="key">the key</param>
    /// <returns>the allocated row</returns>
    public static Row Get(Value?[] data, int memory, long key)
    {
        Row r = new DefaultRow(data, memory);
        r.SetKey(key);
        return r;
    }

    /// <summary>
    /// Get values.
    /// </summary>
    /// <returns>values</returns>
    public abstract Value?[] GetValueList();

    /// <summary>
    /// Check whether values of this row are equal to values of other row.
    /// </summary>
    /// <param name="other">the other row</param>
    /// <returns><c>true</c> if values are equal, <c>false</c> otherwise</returns>
    // The reference's Arrays.equals compares element-wise and allocates nothing. SequenceEqual
    // reached the same comparison through IEnumerable, which boxed the struct array enumerator
    // twice per call on every UPDATE; EqualityComparer<Value?>.Default is what SequenceEqual
    // used under the hood, so the loop below changes none of that comparison and drops both boxes.
    public bool HasSameValues(Row other)
    {
        Value?[] a = GetValueList(), b = other.GetValueList();
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (!EqualityComparer<Value?>.Default.Equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Check whether this row and the specified row share the same underlying data with values.
    /// </summary>
    /// <remarks>
    /// This method must return <c>false</c> when values are not equal and may return either
    /// <c>true</c> or <c>false</c> when they are equal. It may be used only for optimizations and
    /// should not perform any slow checks, such as equality checks for all pairs of values.
    /// </remarks>
    /// <param name="other">the other row</param>
    /// <returns><c>true</c> if rows share the same underlying data, <c>false</c> otherwise or when
    /// unknown</returns>
    public virtual bool HasSharedData(Row other) => false;
}
