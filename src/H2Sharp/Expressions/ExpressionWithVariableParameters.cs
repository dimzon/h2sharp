/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.ExpressionWithVariableParameters.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Message;

namespace H2Sharp.Expressions;

/// <summary>
/// An expression with variable number of parameters.
/// </summary>
public interface IExpressionWithVariableParameters
{
    /// <summary>
    /// Adds the parameter expression.
    /// </summary>
    /// <param name="param">the expression</param>
    void AddParameter(Expression param);

    /// <summary>
    /// This method must be called after all the parameters have been set. It checks if the
    /// parameter count is correct when required by the implementation.
    /// </summary>
    /// <exception cref="H2SharpException">if the parameter count is incorrect</exception>
    void DoneWithParameters();
}
