/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.command.query.SelectGroups.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A stub with the reference's shape: grouped execution is not ported, and only the
 * quick aggregate path runs, which never touches group data. It exists so the aggregate
 * base keeps the reference's dispatch instead of a group-less shadow of itself.
 */
using H2Sharp.Values;

namespace H2Sharp.Command.Query;

/// <summary>
/// Group data for GROUP BY queries: one aggregate data set per group key.
/// </summary>
public sealed class SelectGroups
{
    /// <summary>
    /// The row id of the current group.
    /// </summary>
    /// <returns>the row id</returns>
    public int GetCurrentGroupRowId()
        => throw new TODOException("SelectGroups.getCurrentGroupRowId: grouped execution is not ported");

    /// <summary>
    /// Whether the named group is the current one.
    /// </summary>
    /// <param name="window">whether this is a window function</param>
    /// <returns>true when current</returns>
    public bool IsCurrentGroup(bool window)
        => throw new TODOException("SelectGroups.isCurrentGroup: grouped execution is not ported");

    /// <summary>
    /// The aggregate data of the current group for this expression, if any was stored.
    /// </summary>
    /// <param name="expression">the aggregate expression</param>
    /// <returns>the data, or null</returns>
    public object? GetCurrentGroupExprData(object expression)
        => throw new TODOException("SelectGroups.getCurrentGroupExprData: grouped execution is not ported");

    /// <summary>
    /// Stores aggregate data of the current group for this expression.
    /// </summary>
    /// <param name="expression">the aggregate expression</param>
    /// <param name="data">the data</param>
    public void SetCurrentGroupExprData(object expression, object data)
        => throw new TODOException("SelectGroups.setCurrentGroupExprData: grouped execution is not ported");

    /// <summary>
    /// Reads the group's values back after the walk.
    /// </summary>
    /// <returns>the next group key, or null when done</returns>
    public ValueRow? Next()
        => throw new TODOException("SelectGroups.next: grouped execution is not ported");
}
