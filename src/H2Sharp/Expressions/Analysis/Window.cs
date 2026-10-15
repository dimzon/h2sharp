/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.analysis.Window.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A stub with the reference's shape: window queries are not ported, and no OVER clause
 * can reach here, so every member refuses with the missing piece named. It exists so the
 * aggregate base keeps the reference's field and signatures instead of growing a
 * window-less shadow of itself that a later port would have to tear out.
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Tables;

namespace H2Sharp.Expressions.Analysis;

/// <summary>
/// The OVER clause of a window function or window aggregate.
/// </summary>
public sealed class Window
{
    /// <summary>
    /// Maps the window's columns.
    /// </summary>
    /// <param name="resolver">the column resolver</param>
    /// <param name="level">the subquery nesting level</param>
    public void MapColumns(ColumnResolver resolver, int level)
        => throw new TODOException("Window.mapColumns: window queries are not ported");

    /// <summary>
    /// Tells the window's columns whether the table filter can return values now.
    /// </summary>
    /// <param name="tableFilter">the table filter</param>
    /// <param name="value">true if the table filter can return value</param>
    public void SetEvaluatable(TableFilter tableFilter, bool value)
        => throw new TODOException("Window.setEvaluatable: window queries are not ported");

    /// <summary>
    /// Updates the window's aggregates.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="stage">select stage</param>
    public void UpdateAggregate(SessionLocal session, int stage)
        => throw new TODOException("Window.updateAggregate: window queries are not ported");

    /// <summary>
    /// Appends the OVER clause SQL.
    /// </summary>
    /// <param name="builder">string builder</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <param name="forceOrderBy">whether a missing ORDER BY is generated</param>
    /// <returns>the builder</returns>
    public StringBuilder GetSql(StringBuilder builder, int sqlFlags, bool forceOrderBy)
        => throw new TODOException("Window.getSQL: window queries are not ported");
}
