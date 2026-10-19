/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.Function1.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;

using H2Sharp.Expressions;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// Function with one argument.
/// </summary>
public abstract class Function1 : Operation1, INamedExpression
{
    /// <summary>
    /// Creates a function with the given argument.
    /// </summary>
    /// <param name="arg">the argument</param>
    protected Function1(Expression arg)
        : base(arg)
    {
    }

    /// <inheritdoc/>
    public abstract string GetName();

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        return arg.GetUnenclosedSql(builder.Append(GetName()).Append('('), sqlFlags).Append(')');
    }
}
