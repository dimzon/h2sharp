/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.SetFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A SET function.
/// </summary>
public sealed class SetFunction : Function2
{
    /// <summary>
    /// Creates a SET function.
    /// </summary>
    /// <param name="arg1">the assigned variable</param>
    /// <param name="arg2">the assigned value</param>
    public SetFunction(Expression arg1, Expression arg2)
        : base(arg1, arg2)
    {
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Variable variable = (Variable)left;
        Value value = right.GetValue(session);
        session.SetVariable(variable.GetName(), value);
        return value;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        right = right.Optimize(session);
        type = right.GetTypeInfo();
        if (left is not Variable)
        {
            throw DbException.Get(
                Api.ErrorCode.CanOnlyAssignToVariable1,
                left.GetSql(new StringBuilder(), HasSql.TraceSqlFlags).ToString());
        }

        return this;
    }

    /// <inheritdoc/>
    public override string GetName() => "SET";

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        if (!base.IsEverything(visitor))
        {
            return false;
        }

        switch (visitor.GetVisitorType())
        {
            case ExpressionVisitor.Deterministic:
            case ExpressionVisitor.QueryComparable:
            case ExpressionVisitor.ReadOnly:
                return false;
            default:
                return true;
        }
    }
}
