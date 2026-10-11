/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.result.LocalResult.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The result a statement fills on this side.
 *
 * It is both a result and a target: a statement walks it with Next() and writes into it with
 * AddRow(), which is why it implements both interfaces and why the methods that end the filling
 * — Done(), LimitWereApplied() — are separate from AddRow(). The work between them is what
 * makes this the largest result in the reference and the hardest to port: rows spill to a
 * temporary store when there are more than maxMemoryRows of them, DISTINCT is answered by asking
 * the map rather than by keeping a set, and OFFSET/LIMIT/WITH TIES are applied once, at the end,
 * after sorting.
 *
 * None of that is ported, so every member here refuses. The shape is kept because it is the
 * shape the command layer programs against: a query returns one of these, and the caller asks it
 * for columns, rows and a count. A stub that answered "no rows" instead would be a result that
 * looks like an empty query, which is the one answer this class must never give.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Result;

/// <summary>
/// The result of a statement on the server side.
/// </summary>
public sealed class LocalResult : ResultInterface, ResultTarget
{
    /// <summary>
    /// Creates a result for a table scan.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="table">the table</param>
    /// <returns>the result</returns>
    public static LocalResult ForTable(SessionLocal session, Table table)
        => throw new TODOException("LocalResult.forTable: not ported");

    /// <summary>
    /// Creates an empty result with no columns.
    /// </summary>
    public LocalResult()
        => throw new TODOException("LocalResult: the result is not ported");

    private LocalResult(SessionLocal session)
        => throw new TODOException("LocalResult: the result is not ported");

    /// <summary>
    /// Creates a result for the given expressions.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="expressions">the expressions of the columns</param>
    /// <param name="visibleColumnCount">the number of columns the caller sees</param>
    /// <param name="resultColumnCount">the number of columns, including hidden ones</param>
    public LocalResult(SessionLocal session, Expression[] expressions, int visibleColumnCount, int resultColumnCount)
        => throw new TODOException("LocalResult: the result is not ported");

    /// <inheritdoc/>
    public bool IsLazy() => false;

    /// <summary>
    /// Sets the number of rows to keep in memory before spilling to a temporary store.
    /// </summary>
    /// <param name="maxValue">the number of rows</param>
    public void SetMaxMemoryRows(int maxValue)
        => throw new TODOException("LocalResult.setMaxMemoryRows: not ported");

    /// <summary>
    /// Marks this result as the one that collects the rows a data change statement changed.
    /// </summary>
    public void SetForDataChangeDeltaTable()
        => throw new TODOException("LocalResult.setForDataChangeDeltaTable: not ported");

    /// <inheritdoc/>
    public ResultInterface? CreateShallowCopy(Session targetSession)
        => throw new TODOException("LocalResult.createShallowCopy: not ported");

    /// <summary>
    /// Sets the order of the rows.
    /// </summary>
    /// <param name="sort">the order</param>
    public void SetSortOrder(SortOrder sort)
        => throw new TODOException("LocalResult.setSortOrder: not ported");

    /// <summary>
    /// Removes duplicate rows.
    /// </summary>
    public void SetDistinct()
        => throw new TODOException("LocalResult.setDistinct: not ported");

    /// <summary>
    /// Removes duplicate rows, by the given column indexes only.
    /// </summary>
    /// <param name="distinctIndexes">the columns that decide whether two rows are the same</param>
    public void SetDistinct(int[] distinctIndexes)
        => throw new TODOException("LocalResult.setDistinct: not ported");

    /// <summary>
    /// Marks this result as the one behind an IN predicate's value list.
    /// </summary>
    /// <param name="inPredicateSortTypes">the sort types of the values</param>
    public void SetInPredicateValueListResult(int[] inPredicateSortTypes)
        => throw new TODOException("LocalResult.setInPredicateValueListResult: not ported");

    /// <summary>
    /// Whether this result removes duplicates.
    /// </summary>
    /// <returns>true if it does</returns>
    private bool IsAnyDistinct()
        => throw new TODOException("LocalResult.isAnyDistinct: not ported");

    /// <summary>
    /// Whether these values are already in this result.
    /// </summary>
    /// <param name="values">the values</param>
    /// <returns>true if they are</returns>
    public bool ContainsDistinct(Value?[] values)
        => throw new TODOException("LocalResult.containsDistinct: not ported");

    /// <summary>
    /// Whether any row of this result contains NULL.
    /// </summary>
    /// <returns>true if it does</returns>
    public bool ContainsNull()
        => throw new TODOException("LocalResult.containsNull: not ported");

    /// <summary>
    /// Forgets these values, so that they may be added again.
    /// </summary>
    /// <param name="values">the values</param>
    public void RemoveDistinct(Value?[] values)
        => throw new TODOException("LocalResult.removeDistinct: not ported");

    /// <inheritdoc/>
    public void Reset()
        => throw new TODOException("LocalResult.reset: not ported");

    /// <summary>
    /// Gets the current row as a row of a table.
    /// </summary>
    /// <returns>the row</returns>
    public Row CurrentRowForTable()
        => throw new TODOException("LocalResult.currentRowForTable: not ported");

    /// <inheritdoc/>
    public Value?[] CurrentRow()
        => throw new TODOException("LocalResult.currentRow: not ported");

    /// <inheritdoc/>
    public bool Next()
        => throw new TODOException("LocalResult.next: not ported");

    /// <inheritdoc/>
    public long GetRowId()
        => throw new TODOException("LocalResult.getRowId: not ported");

    /// <inheritdoc/>
    public bool IsAfterLast()
        => throw new TODOException("LocalResult.isAfterLast: not ported");

    /// <summary>
    /// Copies any lob of this row, so that the row can be kept after the statement ends.
    /// </summary>
    /// <param name="values">the values</param>
    private void CloneLobs(Value?[] values)
        => throw new TODOException("LocalResult.cloneLobs: lobs are not ported");

    /// <summary>
    /// Gets the row that stands for these values when duplicates are removed.
    /// </summary>
    /// <param name="values">the values</param>
    /// <returns>the row</returns>
    private ValueRow GetDistinctRow(Value?[] values)
        => throw new TODOException("LocalResult.getDistinctRow: not ported");

    /// <summary>
    /// Moves the rows to the external store, when there are too many for memory.
    /// </summary>
    private void CreateExternalResult()
        => throw new TODOException("LocalResult.createExternalResult: the temp store is not ported");

    /// <summary>
    /// Adds a row read from a table.
    /// </summary>
    /// <param name="row">the row</param>
    public void AddRowForTable(Row row)
        => throw new TODOException("LocalResult.addRowForTable: not ported");

    /// <inheritdoc/>
    public void AddRow(params Value?[] values)
        => throw new TODOException("LocalResult.addRow: not ported");

    /// <summary>
    /// The part of AddRow that stores the row, with no check of its own.
    /// </summary>
    /// <param name="values">the values</param>
    private void AddRowInternal(params Value?[] values)
        => throw new TODOException("LocalResult.addRowInternal: not ported");

    /// <summary>
    /// Writes the rows held in memory to the disk store.
    /// </summary>
    private void AddRowsToDisk()
        => throw new TODOException("LocalResult.addRowsToDisk: the temp store is not ported");

    /// <inheritdoc/>
    public int GetVisibleColumnCount()
        => throw new TODOException("LocalResult.getVisibleColumnCount: not ported");

    /// <summary>
    /// Called when no more rows will be added, which is when sorting and limits are applied.
    /// </summary>
    public void Done()
        => throw new TODOException("LocalResult.done: not ported");

    /// <summary>
    /// Applies the offset and the limit to the rows.
    /// </summary>
    private void ApplyOffsetAndLimit()
        => throw new TODOException("LocalResult.applyOffsetAndLimit: not ported");

    /// <summary>
    /// Drops the rows before the offset and after the limit from the disk store.
    /// </summary>
    /// <param name="offset">the offset</param>
    /// <param name="limit">the limit</param>
    private void TrimExternal(long offset, long limit)
        => throw new TODOException("LocalResult.trimExternal: the temp store is not ported");

    /// <inheritdoc/>
    public long GetRowCount()
        => throw new TODOException("LocalResult.getRowCount: not ported");

    /// <inheritdoc/>
    public void LimitsWereApplied()
        => throw new TODOException("LocalResult.limitsWereApplied: not ported");

    /// <inheritdoc/>
    public bool HasNext()
        => throw new TODOException("LocalResult.hasNext: not ported");

    /// <summary>
    /// Sets the maximum number of rows.
    /// </summary>
    /// <param name="limit">the limit</param>
    public void SetLimit(long limit)
        => throw new TODOException("LocalResult.setLimit: not ported");

    /// <summary>
    /// Sets whether the limit is a percentage.
    /// </summary>
    /// <param name="fetchPercent">true for a percentage</param>
    public void SetFetchPercent(bool fetchPercent)
        => throw new TODOException("LocalResult.setFetchPercent: not ported");

    /// <summary>
    /// Sets the order in which rows tied with the last one are kept.
    /// </summary>
    /// <param name="withTiesSortOrder">the order</param>
    public void SetWithTies(SortOrder withTiesSortOrder)
        => throw new TODOException("LocalResult.setWithTies: not ported");

    /// <inheritdoc/>
    public bool NeedToClose()
        => throw new TODOException("LocalResult.needToClose: not ported");

    /// <inheritdoc/>
    public void Close()
        => throw new TODOException("LocalResult.close: not ported");

    /// <inheritdoc/>
    public string GetAlias(int i)
        => throw new TODOException("LocalResult.getAlias: not ported");

    /// <inheritdoc/>
    public string? GetTableName(int i)
        => throw new TODOException("LocalResult.getTableName: not ported");

    /// <inheritdoc/>
    public string? GetSchemaName(int i)
        => throw new TODOException("LocalResult.getSchemaName: not ported");

    /// <inheritdoc/>
    public string GetColumnName(int i)
        => throw new TODOException("LocalResult.getColumnName: not ported");

    /// <inheritdoc/>
    public TypeInfo GetColumnType(int i)
        => throw new TODOException("LocalResult.getColumnType: not ported");

    /// <inheritdoc/>
    public int GetNullable(int i)
        => throw new TODOException("LocalResult.getNullable: not ported");

    /// <inheritdoc/>
    public bool IsIdentity(int i)
        => throw new TODOException("LocalResult.isIdentity: not ported");

    /// <summary>
    /// Sets the offset of the first row.
    /// </summary>
    /// <param name="offset">the offset</param>
    public void SetOffset(long offset)
        => throw new TODOException("LocalResult.setOffset: not ported");

    /// <inheritdoc/>
    public override string ToString()
        => throw new TODOException("LocalResult.toString: not ported");

    /// <inheritdoc/>
    public bool IsClosed()
        => throw new TODOException("LocalResult.isClosed: not ported");

    /// <inheritdoc/>
    public int GetFetchSize()
        => throw new TODOException("LocalResult.getFetchSize: not ported");

    /// <inheritdoc/>
    public void SetFetchSize(int fetchSize)
        => throw new TODOException("LocalResult.setFetchSize: not ported");

    /// <summary>
    /// Releases what this result holds.
    /// </summary>
    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }
}
