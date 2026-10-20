/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.VirtualConstructedTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A base class for virtual tables that construct all their content at once.
 */
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Result;

namespace H2Sharp.Tables;

/// <summary>
/// A base class for virtual tables that construct all their content at once.
/// </summary>
/// <remarks>
/// The rows of such a table are not stored: they are computed when the table is read, and
/// the index that reads them asks the table for a result and walks it. That is what makes
/// the table of a data change possible: the rows it holds are the rows the statement changed,
/// and they exist only as long as the statement is running.
/// </remarks>
public abstract class VirtualConstructedTable : Table
{
    /// <summary>
    /// Reads the rows of the table.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the result</returns>
    public abstract ResultInterface GetResult(SessionLocal session);

    /// <inheritdoc/>
    public override Index? GetScanIndex(SessionLocal session) => new VirtualConstructedTableIndex(this);
}
