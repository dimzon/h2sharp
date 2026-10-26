/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.ConditionLocalAndGlobal.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// A global condition or combination of local and global conditions. May be used only as a
/// top-level expression in a WHERE, HAVING, or QUALIFY clause of a SELECT.
/// </summary>
public sealed class ConditionLocalAndGlobal : Condition
{
    private Expression? local;

    private Expression global;

    /// <summary>
    /// Creates a local-and-global condition.
    /// </summary>
    /// <param name="local">the local condition, or null</param>
    /// <param name="global">the global condition</param>
    public ConditionLocalAndGlobal(Expression? local, Expression global)
    {
        this.local = local;
        this.global = global ?? throw DbException.GetInternalError();
    }

    /// <inheritdoc/>
    public override bool NeedParentheses() => local is not null || global.NeedParentheses();

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        if (local is null)
        {
            return global.GetUnenclosedSql(builder, sqlFlags);
        }

        _ = local.GetSql(builder, sqlFlags, AutoParentheses);
        _ = builder.Append("\n    _LOCAL_AND_GLOBAL_ ");
        return global.GetSql(builder, sqlFlags, AutoParentheses);
    }

    /// <inheritdoc/>
    public override void CreateIndexConditions(SessionLocal session, TableFilter filter)
    {
        local?.CreateIndexConditions(session, filter);
        global.CreateIndexConditions(session, filter);
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        if (local is null)
        {
            return global.GetValue(session);
        }

        Value l = local.GetValue(session), r;
        if (l.IsFalse() || (r = global.GetValue(session)).IsFalse())
        {
            return ValueBoolean.False;
        }

        if (ReferenceEquals(l, ValueNull.Instance) || ReferenceEquals(r, ValueNull.Instance))
        {
            return ValueNull.Instance;
        }

        return ValueBoolean.True;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        global = global.Optimize(session);
        if (local is not null)
        {
            local = local.Optimize(session);
            Expression? e = ConditionAndOr.OptimizeIfConstant(
                session, ConditionAndOr.And, local, global);
            if (e is not null)
            {
                return e;
            }
        }

        return this;
    }

    /// <inheritdoc/>
    public override void AddFilterConditions(TableFilter filter)
    {
        local?.AddFilterConditions(filter);
        global.AddFilterConditions(filter);
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        local?.MapColumns(resolver, level, state);
        global.MapColumns(resolver, level, state);
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        local?.SetEvaluatable(tableFilter, value);
        global.SetEvaluatable(tableFilter, value);
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        local?.UpdateAggregate(session, stage);
        global.UpdateAggregate(session, stage);
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
        => (local is null || local.IsEverything(visitor)) && global.IsEverything(visitor);

    /// <inheritdoc/>
    public override int GetCost()
    {
        int cost = global.GetCost();
        if (local is not null)
        {
            cost += local.GetCost();
        }

        return cost;
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount() => local is null ? 1 : 2;

    /// <inheritdoc/>
    public override Expression GetSubexpression(int index)
    {
        switch (index)
        {
            case 0:
                return local ?? global;
            case 1:
                if (local is not null)
                {
                    return global;
                }

                break;
        }

        throw new IndexOutOfRangeException();
    }
}
