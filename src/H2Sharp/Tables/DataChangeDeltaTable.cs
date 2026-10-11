/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.DataChangeDeltaTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The rows a statement changed, collected for the caller that asked for them.
 *
 * Three versions of a changed row exist, and which one a caller gets is what the nested enum is
 * for: the row as it was, the new row with its defaults evaluated but before any trigger ran,
 * and the row as it ended up. A trigger that rewrites a row would make OLD and FINAL differ, and
 * a generated-keys caller that was handed NEW would get a row the triggers never saw.
 *
 * The table itself is not ported; the enum is, because it is the parameter every data change
 * statement takes and a caller has to be able to name it.
 */
using H2Sharp.Engine;

namespace H2Sharp.Tables;

/// <summary>
/// Collects the rows that a data change statement changed, when the caller asked for them.
/// </summary>
public sealed class DataChangeDeltaTable
{
    /// <summary>
    /// Which version of a changed row the collector holds.
    /// </summary>
    public enum ResultOption
    {
        /// <summary>OLD row.</summary>
        OLD,

        /// <summary>NEW row with evaluated default expressions, but before triggers.</summary>
        NEW,

        /// <summary>FINAL rows after triggers.</summary>
        FINAL,
    }

    /// <summary>
    /// Collects the final row for INSERT operations.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="deltaTableName">the name of the delta table</param>
    /// <returns>the collector</returns>
    public static DataChangeDeltaTable CollectForInsert(SessionLocal session, string deltaTableName)
        => throw new TODOException("DataChangeDeltaTable.collectForInsert: not ported");
}
