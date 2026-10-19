/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.Function0_1.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;

using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// Function with one optional argument.
/// </summary>
public abstract class Function0_1 : Expression, INamedExpression
{
    /// <summary>
    /// The argument of the operation, or null.
    /// </summary>
    protected Expression? arg;

    /// <summary>
    /// The type of the result.
    /// </summary>
    protected TypeInfo? type;

    /// <summary>
    /// Creates a function with the given argument.
    /// </summary>
    /// <param name="arg">the argument, or null</param>
    protected Function0_1(Expression? arg)
    {
        this.arg = arg;
    }

    /// <inheritdoc/>
    public abstract string GetName();

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => type!;

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        arg?.MapColumns(resolver, level, state);
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        arg?.SetEvaluatable(tableFilter, value);
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        arg?.UpdateAggregate(session, stage);
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return arg == null || arg.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        int cost = 1;
        if (arg != null)
        {
            cost += arg.GetCost();
        }

        return cost;
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount()
    {
        return arg != null ? 1 : 0;
    }

    /// <inheritdoc/>
    public override Expression GetSubexpression(int index)
    {
        if (index == 0 && arg != null)
        {
            return arg;
        }

        throw new IndexOutOfRangeException();
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append(GetName()).Append('(');
        if (arg != null)
        {
            _ = arg.GetUnenclosedSql(builder, sqlFlags);
        }

        return builder.Append(')');
    }
}
