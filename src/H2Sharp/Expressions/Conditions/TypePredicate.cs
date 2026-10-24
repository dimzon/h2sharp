/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.TypePredicate.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// Type predicate (IS [NOT] OF).
/// </summary>
public sealed class TypePredicate : SimplePredicate
{
    private readonly TypeInfo[] typeList;

    private int[]? valueTypes;

    /// <summary>
    /// Creates a type predicate.
    /// </summary>
    /// <param name="left">the expression to test</param>
    /// <param name="not">whether this is IS NOT OF</param>
    /// <param name="whenOperand">whether this is the when operand of the simple case</param>
    /// <param name="typeList">the types to test against</param>
    public TypePredicate(Expression left, bool not, bool whenOperand, TypeInfo[] typeList)
        : base(left, not, whenOperand)
    {
        this.typeList = typeList;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        return GetWhenSql(left.GetSql(builder, sqlFlags, AutoParentheses), sqlFlags);
    }

    /// <inheritdoc/>
    public override StringBuilder GetWhenSql(StringBuilder builder, int sqlFlags)
    {
        builder.Append(" IS");
        if (not)
        {
            builder.Append(" NOT");
        }

        builder.Append(" OF (");
        for (int i = 0; i < typeList.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            typeList[i].GetSql(builder, sqlFlags);
        }

        return builder.Append(')');
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        int count = typeList.Length;
        valueTypes = new int[count];
        for (int i = 0; i < count; i++)
        {
            valueTypes[i] = typeList[i].GetValueType();
        }

        Array.Sort(valueTypes);
        return base.Optimize(session);
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value value = left.GetValue(session);
        if (value is ValueNull)
        {
            return ValueNull.Instance;
        }

        return ValueBoolean.Get((Array.BinarySearch(valueTypes!, value.GetValueType()) >= 0) ^ not);
    }

    /// <inheritdoc/>
    public override bool GetWhenValue(SessionLocal session, Value leftValue)
    {
        if (!whenOperand)
        {
            return base.GetWhenValue(session, leftValue);
        }

        if (leftValue is ValueNull)
        {
            return false;
        }

        return (Array.BinarySearch(valueTypes!, leftValue.GetValueType()) >= 0) ^ not;
    }

    /// <inheritdoc/>
    public override Expression? GetNotIfPossible(SessionLocal session)
    {
        if (whenOperand)
        {
            return null;
        }

        return new TypePredicate(left, !not, false, typeList);
    }
}
