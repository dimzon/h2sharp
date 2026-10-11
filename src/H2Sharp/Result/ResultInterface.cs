/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.result.ResultInterface.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The shape a result has: rows to walk, and the columns they are made of.
 *
 * Everything a query returns comes back as one of these — the local result a
 * statement fills, the remote result a client reads, the merged result of a batch.
 * That is the reason it is an interface and not a class: the caller walks rows the
 * same way whichever side produced them.
 */
using System;
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// The result interface is used by the <c>LocalResult</c> and <c>ResultRemote</c> class.
/// A result may contain rows, or just an update count.
/// </summary>
public interface ResultInterface : IDisposable
{
    /// <summary>
    /// Go to the beginning of the result, that means before the first row.
    /// </summary>
    void Reset();

    /// <summary>
    /// Get the current row.
    /// </summary>
    /// <returns>the row</returns>
    Value?[] CurrentRow();

    /// <summary>
    /// Go to the next row.
    /// </summary>
    /// <returns>true if a row exists</returns>
    bool Next();

    /// <summary>
    /// Get the current row id, starting with 0. -1 is returned when <see cref="Next"/>
    /// was not called yet.
    /// </summary>
    /// <returns>the row id</returns>
    long GetRowId();

    /// <summary>
    /// Check if the current position is after last row.
    /// </summary>
    /// <returns>true if after last</returns>
    bool IsAfterLast();

    /// <summary>
    /// Get the number of visible columns. More columns may exist internally for sorting
    /// or grouping.
    /// </summary>
    /// <returns>the number of columns</returns>
    int GetVisibleColumnCount();

    /// <summary>
    /// Get the number of rows in this object.
    /// </summary>
    /// <returns>the number of rows</returns>
    long GetRowCount();

    /// <summary>
    /// Check if this result has more rows to fetch.
    /// </summary>
    /// <returns>true if it has</returns>
    bool HasNext();

    /// <summary>
    /// Check if this result set should be closed, for example because it is buffered
    /// using a temporary file.
    /// </summary>
    /// <returns>true if close should be called</returns>
    bool NeedToClose();

    /// <summary>
    /// Close the result and delete any temporary files.
    /// </summary>
    void Close();

    /// <summary>
    /// Get the column alias name for the column.
    /// </summary>
    /// <param name="i">the column number (starting with 0)</param>
    /// <returns>the alias name</returns>
    string GetAlias(int i);

    /// <summary>
    /// Get the schema name for the column, if one exists.
    /// </summary>
    /// <param name="i">the column number (starting with 0)</param>
    /// <returns>the schema name or null</returns>
    string? GetSchemaName(int i);

    /// <summary>
    /// Get the table name for the column, if one exists.
    /// </summary>
    /// <param name="i">the column number (starting with 0)</param>
    /// <returns>the table name or null</returns>
    string? GetTableName(int i);

    /// <summary>
    /// Get the column name.
    /// </summary>
    /// <param name="i">the column number (starting with 0)</param>
    /// <returns>the column name</returns>
    string GetColumnName(int i);

    /// <summary>
    /// Get the column data type.
    /// </summary>
    /// <param name="i">the column number (starting with 0)</param>
    /// <returns>the column data type</returns>
    TypeInfo GetColumnType(int i);

    /// <summary>
    /// Check if this is an identity column.
    /// </summary>
    /// <param name="i">the column number (starting with 0)</param>
    /// <returns>true for identity columns</returns>
    bool IsIdentity(int i);

    /// <summary>
    /// Check if this column is nullable.
    /// </summary>
    /// <param name="i">the column number (starting with 0)</param>
    /// <returns>Column.NULLABLE_*</returns>
    int GetNullable(int i);

    /// <summary>
    /// Set the fetch size for this result set.
    /// </summary>
    /// <param name="fetchSize">the new fetch size</param>
    void SetFetchSize(int fetchSize);

    /// <summary>
    /// Get the current fetch size for this result set.
    /// </summary>
    /// <returns>the fetch size</returns>
    int GetFetchSize();

    /// <summary>
    /// Check if this a lazy execution result.
    /// </summary>
    /// <returns>true if it is a lazy result</returns>
    bool IsLazy();

    /// <summary>
    /// Check if this result set is closed.
    /// </summary>
    /// <returns>true if it is</returns>
    bool IsClosed();

    /// <summary>
    /// Create a shallow copy of the result set. The data and a temporary table (if there is
    /// any) is not copied.
    /// </summary>
    /// <param name="targetSession">the session of the copy</param>
    /// <returns>the copy if possible, or null if copying is not possible</returns>
    ResultInterface? CreateShallowCopy(Engine.Session targetSession);
}
