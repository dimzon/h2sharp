/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.CommandWithValues.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A data change statement whose rows are written out as VALUES.
 *
 * The values are kept as expressions rather than as values, and that is the reference's rule
 * with a reason behind it: a row may name a parameter, and a parameter's value is not known
 * until the statement runs, so an INSERT prepared once and run twice must evaluate its rows
 * twice. Reading the expressions here and the values in the update is what makes that work.
 *
 * Not ported: the members refuse.
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Util;

namespace H2Sharp.Command.Dml;

/// <summary>
/// A data change statement whose rows are written as VALUES.
/// </summary>
public abstract class CommandWithValues : DataChangeStatement
{
    /// <summary>
    /// The value expressions of every row, in the order they were written.
    /// </summary>
    protected readonly List<Expression[]> ValuesExpressionList = Utils.NewSmallArrayList<Expression[]>();

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    protected CommandWithValues(SessionLocal session)
        : base(session)
    {
    }

    /// <summary>
    /// Adds one row's value expressions.
    /// </summary>
    /// <param name="expr">the expressions of the row's values</param>
    public void AddRow(Expression[] expr)
    {
        ValuesExpressionList.Add(expr);
    }
}
