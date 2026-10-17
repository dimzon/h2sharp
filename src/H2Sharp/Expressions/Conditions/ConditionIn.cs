/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.ConditionIn.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Collections.Generic;
using System.Text;

using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// Abstract IN predicate with a list of values.
/// </summary>
/// <remarks>
/// Public even though the reference keeps it package-private: C# does not allow a public class
/// to derive from an internal one, and the concrete IN predicates below are public there.
/// </remarks>
public abstract class ConditionIn : Condition
{
    protected Expression left;

    protected readonly bool not;

    protected readonly bool whenOperand;

    protected readonly List<Expression> valueList;

    /// <summary>
    /// Creates an IN predicate.
    /// </summary>
    /// <param name="left">the expression before IN</param>
    /// <param name="not">whether the result should be negated</param>
    /// <param name="whenOperand">whether this is a when operand</param>
    /// <param name="valueList">the value list (at least one element)</param>
    protected ConditionIn(Expression left, bool not, bool whenOperand, List<Expression> valueList)
    {
        this.left = left;
        this.not = not;
        this.whenOperand = whenOperand;
        this.valueList = valueList;
    }

    /// <inheritdoc/>
    public sealed override Value GetValue(SessionLocal session)
        => GetValue(session, left.GetValue(session));

    /// <inheritdoc/>
    public sealed override bool GetWhenValue(SessionLocal session, Value left)
    {
        if (!whenOperand)
        {
            return base.GetWhenValue(session, left);
        }

        return GetValue(session, left).GetBoolean();
    }

    /// <summary>
    /// Evaluates the predicate for one left value.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="left">the left value</param>
    /// <returns>the result</returns>
    protected abstract Value GetValue(SessionLocal session, Value left);

    /// <inheritdoc/>
    public sealed override bool IsWhenConditionOperand() => whenOperand;

    /// <inheritdoc/>
    /// <remarks>
    /// The reference asks <c>Settings.optimizeInList</c> here; <c>Settings</c> is not ported, and
    /// the index-condition path is the setting's default, so it is taken unconditionally.
    /// </remarks>
    public sealed override void CreateIndexConditions(SessionLocal session, TableFilter filter)
    {
        if (not || whenOperand)
        {
            return;
        }

        if (left is ExpressionColumn l && filter == l.GetTableFilter())
        {
            CreateIndexConditions(filter, l, valueList);
        }
    }

    /// <summary>
    /// Creates a unique index condition for the column.
    /// </summary>
    /// <param name="filter">the table filter</param>
    /// <param name="l">the column expression</param>
    /// <param name="valueList">the value list</param>
    protected abstract void CreateIndexConditions(TableFilter filter, ExpressionColumn l, List<Expression> valueList);

    /// <inheritdoc/>
    public sealed override bool NeedParentheses() => true;

    /// <inheritdoc/>
    public sealed override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => GetWhenSql(left.GetSql(builder, sqlFlags, AutoParentheses), sqlFlags);

    /// <inheritdoc/>
    public sealed override StringBuilder GetWhenSql(StringBuilder builder, int sqlFlags)
    {
        if (not)
        {
            _ = builder.Append(" NOT");
        }

        return Expression.WriteExpressions(builder.Append(" IN("), valueList, sqlFlags).Append(')');
    }

    /// <inheritdoc/>
    public sealed override int GetSubexpressionCount() => 1 + valueList.Count;

    /// <inheritdoc/>
    public sealed override Expression GetSubexpression(int index)
    {
        if (index == 0)
        {
            return left;
        }

        if (index > 0 && index <= valueList.Count)
        {
            return valueList[index - 1];
        }

        throw new System.IndexOutOfRangeException();
    }
}
