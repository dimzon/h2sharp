/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.Function2.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;

using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// Function with two arguments.
/// </summary>
public abstract class Function2 : Operation2, INamedExpression
{
    /// <summary>
    /// Creates a function with the given arguments.
    /// </summary>
    /// <param name="left">the first argument</param>
    /// <param name="right">the second argument</param>
    protected Function2(Expression left, Expression right)
        : base(left, right)
    {
    }

    /// <inheritdoc/>
    public abstract string GetName();

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v1 = left.GetValue(session);
        if (v1 == ValueNull.Instance)
        {
            return ValueNull.Instance;
        }

        Value v2 = right.GetValue(session);
        if (v2 == ValueNull.Instance)
        {
            return ValueNull.Instance;
        }

        return GetValue(session, v1, v2);
    }

    /// <summary>
    /// Returns the value of this function.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="v1">the value of first argument</param>
    /// <param name="v2">the value of second argument</param>
    /// <returns>the resulting value</returns>
    protected virtual Value GetValue(SessionLocal session, Value v1, Value v2)
    {
        throw DbException.GetInternalError();
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        left.GetUnenclosedSql(builder.Append(GetName()).Append('('), sqlFlags).Append(", ");
        return right.GetUnenclosedSql(builder, sqlFlags).Append(')');
    }
}
