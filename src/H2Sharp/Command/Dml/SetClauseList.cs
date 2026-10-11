/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.SetClauseList.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The SET of an UPDATE: what each column becomes.
 *
 * One column and one value is the common case, but the reference also accepts an index into an
 * array column — SET TAGS[2] = 'x' — and that is why the list holds an array of indexes per
 * assignment rather than a column and an expression. The two kinds are told apart here by the
 * index array being empty, and they are printed differently, because the text of an UPDATE has
 * to come back out as it went in.
 *
 * Not ported: the actions themselves, which need the row being updated.
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Expressions;
using H2Sharp.Tables;

namespace H2Sharp.Command.Dml;

/// <summary>
/// What an UPDATE sets each of its columns to.
/// </summary>
public sealed class SetClauseList
{
    private readonly Table table;

    private readonly List<SetAction> actions = [];

    /// <summary>
    /// Creates the list for a table.
    /// </summary>
    /// <param name="table">the table being updated</param>
    public SetClauseList(Table table)
    {
        this.table = table;
    }

    /// <summary>
    /// Gets the table being updated.
    /// </summary>
    /// <returns>the table</returns>
    public Table GetTable()
    {
        return table;
    }

    /// <summary>
    /// Adds an assignment to one column, possibly into an element of an array column.
    /// </summary>
    /// <param name="column">the column</param>
    /// <param name="arrayIndexes">the index of the array element, or null</param>
    /// <param name="expression">what to set it to</param>
    public void AddSingle(Column column, Expression[]? arrayIndexes, Expression expression)
        => throw new TODOException("SetClauseList.addSingle: not ported");

    /// <summary>
    /// Adds one assignment that sets several columns at once.
    /// </summary>
    /// <param name="columns">the columns</param>
    /// <param name="allIndexes">the indexes of each assignment's array element</param>
    /// <param name="expression">what to set them to</param>
    public void AddMultiple(List<Column> columns, List<Expression[]> allIndexes, Expression expression)
        => throw new TODOException("SetClauseList.addMultiple: not ported");

    /// <summary>
    /// Appends the SQL of this list, as it appears in the UPDATE.
    /// </summary>
    /// <param name="builder">the builder</param>
    /// <param name="sqlFlags">the SQL flags</param>
    /// <returns>the builder</returns>
    public StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => throw new TODOException("SetClauseList.getSQL: not ported");

    /// <summary>
    /// One assignment of this list.
    /// </summary>
    private abstract class SetAction
    {
        /// <summary>
        /// Appends the SQL of this assignment.
        /// </summary>
        /// <param name="builder">the builder</param>
        /// <param name="sqlFlags">the SQL flags</param>
        /// <returns>the builder</returns>
        internal abstract StringBuilder GetSql(StringBuilder builder, int sqlFlags);
    }
}
