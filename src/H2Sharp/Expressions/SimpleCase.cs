/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.SimpleCase.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;

using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// A simple case.
/// </summary>
public sealed class SimpleCase : Expression
{
    /// <summary>
    /// One WHEN of a simple case.
    /// </summary>
    public sealed class SimpleWhen
    {
        /// <summary>
        /// The compared operands.
        /// </summary>
        public Expression[] Operands;

        /// <summary>
        /// The result for this WHEN.
        /// </summary>
        public Expression Result;

        /// <summary>
        /// The next WHEN, or null.
        /// </summary>
        public SimpleWhen? Next;

        /// <summary>
        /// Creates a WHEN with one operand.
        /// </summary>
        /// <param name="operand">the operand</param>
        /// <param name="result">the result</param>
        public SimpleWhen(Expression operand, Expression result)
            : this([operand], result)
        {
        }

        /// <summary>
        /// Creates a WHEN with several operands.
        /// </summary>
        /// <param name="operands">the operands</param>
        /// <param name="result">the result</param>
        public SimpleWhen(Expression[] operands, Expression result)
        {
            Operands = operands;
            Result = result;
        }

        /// <summary>
        /// Sets the next WHEN.
        /// </summary>
        /// <param name="next">the next WHEN</param>
        public void SetWhen(SimpleWhen next) => Next = next;
    }

    private Expression operand;

    private SimpleWhen? when;

    private Expression? elseResult;

    private TypeInfo? type;

    /// <summary>
    /// Creates a simple case.
    /// </summary>
    /// <param name="operand">the operand</param>
    /// <param name="when">the first WHEN, or null</param>
    /// <param name="elseResult">the ELSE result, or null</param>
    public SimpleCase(Expression operand, SimpleWhen? when, Expression? elseResult)
    {
        this.operand = operand;
        this.when = when;
        this.elseResult = elseResult;
    }

    /// <summary>
    /// Sets the first WHEN.
    /// </summary>
    /// <param name="when">the first WHEN</param>
    public void SetWhen(SimpleWhen when) => this.when = when;

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v = operand.GetValue(session);
        for (SimpleWhen? when = this.when; when is not null; when = when.Next)
        {
            foreach (Expression e in when.Operands)
            {
                if (e.GetWhenValue(session, v))
                {
                    return when.Result.GetValue(session).CastToValue(type!, session);
                }
            }
        }

        if (elseResult is not null)
        {
            return elseResult.GetValue(session).CastToValue(type!, session);
        }

        return ValueNull.Instance;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        TypeInfo typeInfo = TypeInfo.TypeUnknown;
        operand = operand.Optimize(session);
        bool allConst = operand.IsConstant();
        Value? v = allConst ? operand.GetValue(session) : null;
        TypeInfo operandType = operand.GetTypeInfo();
        for (SimpleWhen? when = this.when; when is not null; when = when.Next)
        {
            Expression[] operands = when.Operands;
            for (int i = 0; i < operands.Length; i++)
            {
                Expression e = operands[i].Optimize(session);
                if (!e.IsWhenConditionOperand())
                {
                    TypeInfo.CheckComparable(operandType, e.GetTypeInfo());
                }

                if (allConst)
                {
                    if (e.IsConstant())
                    {
                        if (e.GetWhenValue(session, v!))
                        {
                            return when.Result.Optimize(session);
                        }
                    }
                    else
                    {
                        allConst = false;
                    }
                }

                operands[i] = e;
            }

            when.Result = when.Result.Optimize(session);
            typeInfo = CombineTypes(typeInfo, when.Result);
        }

        if (elseResult is not null)
        {
            elseResult = elseResult.Optimize(session);
            if (allConst)
            {
                return elseResult;
            }

            typeInfo = CombineTypes(typeInfo, elseResult);
        }
        else if (allConst)
        {
            return ValueExpression.Null;
        }

        if (typeInfo.GetValueType() == Value.Unknown)
        {
            typeInfo = TypeInfo.TypeVarchar;
        }

        type = typeInfo;
        return this;
    }

    internal static TypeInfo CombineTypes(TypeInfo typeInfo, Expression e)
    {
        if (!e.IsNullConstant())
        {
            TypeInfo t = e.GetTypeInfo();
            int valueType = t.GetValueType();
            if (valueType != Value.Unknown && valueType != Value.Null)
            {
                typeInfo = TypeInfo.GetHigherType(typeInfo, t);
            }
        }

        return typeInfo;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        operand.GetUnenclosedSql(builder.Append("CASE "), sqlFlags);
        for (SimpleWhen? when = this.when; when is not null; when = when.Next)
        {
            _ = builder.Append(" WHEN");
            Expression[] operands = when.Operands;
            for (int i = 0, len = operands.Length; i < len; i++)
            {
                if (i > 0)
                {
                    _ = builder.Append(',');
                }

                operands[i].GetWhenSql(builder, sqlFlags);
            }

            when.Result.GetUnenclosedSql(builder.Append(" THEN "), sqlFlags);
        }

        if (elseResult is not null)
        {
            elseResult.GetUnenclosedSql(builder.Append(" ELSE "), sqlFlags);
        }

        return builder.Append(" END");
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => type!;

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        operand.MapColumns(resolver, level, state);
        for (SimpleWhen? when = this.when; when is not null; when = when.Next)
        {
            foreach (Expression e in when.Operands)
            {
                e.MapColumns(resolver, level, state);
            }

            when.Result.MapColumns(resolver, level, state);
        }

        elseResult?.MapColumns(resolver, level, state);
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        operand.SetEvaluatable(tableFilter, value);
        for (SimpleWhen? when = this.when; when is not null; when = when.Next)
        {
            foreach (Expression e in when.Operands)
            {
                e.SetEvaluatable(tableFilter, value);
            }

            when.Result.SetEvaluatable(tableFilter, value);
        }

        elseResult?.SetEvaluatable(tableFilter, value);
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        operand.UpdateAggregate(session, stage);
        for (SimpleWhen? when = this.when; when is not null; when = when.Next)
        {
            foreach (Expression e in when.Operands)
            {
                e.UpdateAggregate(session, stage);
            }

            when.Result.UpdateAggregate(session, stage);
        }

        elseResult?.UpdateAggregate(session, stage);
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        if (!operand.IsEverything(visitor))
        {
            return false;
        }

        for (SimpleWhen? when = this.when; when is not null; when = when.Next)
        {
            foreach (Expression e in when.Operands)
            {
                if (!e.IsEverything(visitor))
                {
                    return false;
                }
            }

            if (!when.Result.IsEverything(visitor))
            {
                return false;
            }
        }

        return elseResult is null || elseResult.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        int cost = 1, resultCost = 0;
        cost += operand.GetCost();
        for (SimpleWhen? when = this.when; when is not null; when = when.Next)
        {
            foreach (Expression e in when.Operands)
            {
                cost += e.GetCost();
            }

            resultCost = Math.Max(resultCost, when.Result.GetCost());
        }

        if (elseResult is not null)
        {
            resultCost = Math.Max(resultCost, elseResult.GetCost());
        }

        return cost + resultCost;
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount()
    {
        int count = 1;
        for (SimpleWhen? when = this.when; when is not null; when = when.Next)
        {
            count += when.Operands.Length + 1;
        }

        if (elseResult is not null)
        {
            count++;
        }

        return count;
    }

    /// <inheritdoc/>
    public override Expression GetSubexpression(int index)
    {
        if (index >= 0)
        {
            if (index == 0)
            {
                return operand;
            }

            int ptr = 1;
            for (SimpleWhen? when = this.when; when is not null; when = when.Next)
            {
                Expression[] operands = when.Operands;
                int count = operands.Length;
                int offset = index - ptr;
                if (offset < count)
                {
                    return operands[offset];
                }

                ptr += count;
                if (index == ptr++)
                {
                    return when.Result;
                }
            }

            if (elseResult is not null && index == ptr)
            {
                return elseResult;
            }
        }

        throw new IndexOutOfRangeException();
    }
}
