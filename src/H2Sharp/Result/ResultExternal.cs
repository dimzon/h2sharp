using System.Collections.Generic;
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// This interface is used to extend the LocalResult class, if data does not fit in memory.
/// </summary>
public interface ResultExternal
{
    /// <summary>
    /// Reset the current position of this object.
    /// </summary>
    void Reset();

    /// <summary>
    /// Get the next row from the result.
    /// </summary>
    /// <returns>the next row or null</returns>
    Value[]? Next();

    /// <summary>
    /// Add a row to this object.
    /// </summary>
    /// <param name="values">the row to add</param>
    /// <returns>the new number of rows in this object</returns>
    int AddRow(Value[] values);

    /// <summary>
    /// Add a number of rows to the result.
    /// </summary>
    /// <param name="rows">the list of rows to add</param>
    /// <returns>the new number of rows in this object</returns>
    int AddRows(IEnumerable<Value[]> rows);

    /// <summary>
    /// Close this object and delete the temporary file.
    /// </summary>
    void Close();

    /// <summary>
    /// Remove the row with the given values from this object if such a row exists.
    /// </summary>
    /// <param name="values">the row</param>
    /// <returns>the new row count</returns>
    int RemoveRow(Value[] values);

    /// <summary>
    /// Check if the given row exists in this object.
    /// </summary>
    /// <param name="values">the row</param>
    /// <returns>true if it exists</returns>
    bool Contains(Value[] values);

    /// <summary>
    /// Create a shallow copy of this object if possible.
    /// </summary>
    /// <returns>the shallow copy, or null</returns>
    ResultExternal? CreateShallowCopy();
}
