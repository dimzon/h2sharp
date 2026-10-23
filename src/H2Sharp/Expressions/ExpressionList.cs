/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.ExpressionList.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>A list of expressions whose result is an array or a row.</summary>
public sealed class ExpressionList : Expression
{
    private readonly Expression[] list;
    private readonly bool isArray;
    private TypeInfo type = null!;

    /// <summary>Creates an array or row expression.</summary>
    public ExpressionList(Expression[] list, bool isArray)
    {
        this.list = list;
        this.isArray = isArray;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value[] values = new Value[list.Length];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = list[i].GetValue(session);
        }

        return isArray
            ? ValueArray.Get((TypeInfo)type.GetExtTypeInfo()!, values, session)
            : ValueRow.Get(type, values);
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => type;

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        foreach (Expression expression in list)
        {
            expression.MapColumns(resolver, level, state);
        }
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = true;
        for (int i = 0; i < list.Length; i++)
        {
            Expression expression = list[i].Optimize(session);
            if (!expression.IsConstant())
            {
                allConst = false;
            }

            list[i] = expression;
        }

        InitializeType();
        return allConst ? ValueExpression.Get(GetValue(session)) : this;
    }

    // The port's TypeInfo / ExtTypeInfoRow overloads take TypeInfo[] instead of Java Typed[].
    // Same element types and promotion, collected explicitly for those existing interfaces.
    internal void InitializeType()
    {
        TypeInfo[] types = new TypeInfo[list.Length];
        for (int i = 0; i < types.Length; i++)
        {
            types[i] = list[i].GetTypeInfo();
        }

        type = isArray
            ? TypeInfo.GetTypeInfo(Value.Array, list.Length, 0, TypeInfo.GetHigherType(types))
            : TypeInfo.GetTypeInfo(Value.Row, 0, 0, new ExtTypeInfoRow(types));
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        foreach (Expression expression in list)
        {
            expression.SetEvaluatable(tableFilter, value);
        }
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => WriteExpressions(builder.Append(isArray ? "ARRAY [" : "ROW ("), list, sqlFlags)
            .Append(isArray ? ']' : ')');

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        foreach (Expression expression in list)
        {
            expression.UpdateAggregate(session, stage);
        }
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        foreach (Expression expression in list)
        {
            if (!expression.IsEverything(visitor))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        int cost = 1;
        foreach (Expression expression in list)
        {
            cost += expression.GetCost();
        }

        return cost;
    }

    /// <inheritdoc/>
    public override TypeInfo? GetTypeIfStaticallyKnown(SessionLocal session)
    {
        TypeInfo[] types = new TypeInfo[list.Length];
        for (int i = 0; i < types.Length; i++)
        {
            TypeInfo? elementType = list[i].GetTypeIfStaticallyKnown(session);
            if (elementType is null)
            {
                return null;
            }

            types[i] = elementType;
        }

        return isArray
            ? TypeInfo.GetTypeInfo(Value.Array, list.Length, 0, TypeInfo.GetHigherType(types))
            : TypeInfo.GetTypeInfo(Value.Row, 0, 0, new ExtTypeInfoRow(types));
    }

    /// <inheritdoc/>
    public override bool IsConstant()
    {
        foreach (Expression expression in list)
        {
            if (!expression.IsConstant())
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount() => list.Length;

    /// <inheritdoc/>
    public override Expression GetSubexpression(int index) => list[index];

    /// <summary>Whether this constructs an array.</summary>
    public bool IsArray() => isArray;

    /// <summary>Copies the expression list in the specified order.</summary>
    public ExpressionList CloneWithOrder(int[] newOrder)
    {
        if (newOrder.Length != list.Length)
        {
            throw DbException.GetInternalError("Length of the new orders is different than list size.");
        }

        Expression[] newList = new Expression[list.Length];
        for (int i = 0; i < list.Length; i++)
        {
            newList[i] = list[newOrder[i]];
        }

        ExpressionList clone = new(newList, isArray);
        clone.InitializeType();
        return clone;
    }
}
