/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.NullIfFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A NULLIF function.
/// </summary>
public sealed class NullIfFunction : Function2
{
    /// <summary>
    /// Creates a new NULLIF function.
    /// </summary>
    /// <param name="arg1">the first argument</param>
    /// <param name="arg2">the second argument</param>
    public NullIfFunction(Expression arg1, Expression arg2)
        : base(arg1, arg2)
    {
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v = left.GetValue(session);
        if (session.CompareWithNull(v, right.GetValue(session), true) == 0)
        {
            v = ValueNull.Instance;
        }

        return v;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        right = right.Optimize(session);
        type = left.GetTypeInfo();
        TypeInfo.CheckComparable(type, right.GetTypeInfo());
        if (left.IsConstant() && right.IsConstant())
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    /// <inheritdoc/>
    public override string GetName()
    {
        return "NULLIF";
    }
}
