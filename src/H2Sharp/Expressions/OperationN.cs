/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.OperationN.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;

using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// Operation with many arguments.
/// </summary>
public abstract class OperationN : Expression, IExpressionWithVariableParameters
{
    /// <summary>
    /// The array of arguments.
    /// </summary>
    protected Expression[] args;

    /// <summary>
    /// The number of arguments.
    /// </summary>
    protected int argsCount;

    /// <summary>
    /// The type of the result.
    /// </summary>
    protected TypeInfo? type;

    /// <summary>
    /// Creates an operation with the given arguments.
    /// </summary>
    /// <param name="args">the arguments</param>
    protected OperationN(Expression[] args)
    {
        this.args = args;
    }

    /// <inheritdoc/>
    public void AddParameter(Expression param)
    {
        int capacity = args.Length;
        if (argsCount >= capacity)
        {
            Array.Resize(ref args, capacity * 2);
        }

        args[argsCount++] = param;
    }

    /// <inheritdoc/>
    public void DoneWithParameters()
    {
        if (args.Length != argsCount)
        {
            Array.Resize(ref args, argsCount);
        }
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => type!;

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        foreach (Expression e in args)
        {
            e.MapColumns(resolver, level, state);
        }
    }

    /// <summary>
    /// Optimizes arguments.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="allConst">whether operation is deterministic</param>
    /// <returns>whether operation is deterministic and all arguments are constants</returns>
    protected bool OptimizeArguments(SessionLocal session, bool allConst)
    {
        for (int i = 0, l = args.Length; i < l; i++)
        {
            Expression e = args[i].Optimize(session);
            args[i] = e;
            if (allConst && !e.IsConstant())
            {
                allConst = false;
            }
        }

        return allConst;
    }

    /// <summary>
    /// Inlines subexpressions if possible.
    /// </summary>
    /// <param name="tester">the predicate to check whether subexpression can be inlined</param>
    protected void InlineSubexpressions(Predicate<Expression> tester)
    {
        for (int i = 0, sourceLength = args.Length; i < sourceLength; i++)
        {
            Expression e = args[i];
            if (tester(e))
            {
                InlineSubexpressions(tester, i, sourceLength, e);
                break;
            }
        }
    }

    private void InlineSubexpressions(Predicate<Expression> tester, int sourceOffset, int sourceLength, Expression match1)
    {
        int l1 = match1.GetSubexpressionCount();
        bool many = false, forceCopy = false;
        int targetLength = sourceLength;
        if (l1 != 1)
        {
            forceCopy = true;
            targetLength += l1 - 1;
        }

        for (int i = sourceOffset + 1; i < sourceLength; i++)
        {
            Expression e = args[i];
            if (tester(e))
            {
                many = true;
                int l2 = e.GetSubexpressionCount();
                if (l2 != 1)
                {
                    forceCopy = true;
                    targetLength += l2 - 1;
                }
            }
        }

        Expression[] source = args;
        if (forceCopy)
        {
            args = new Expression[targetLength];
            Array.Copy(source, 0, args, 0, sourceOffset);
        }

        CopyArgs(match1, sourceOffset, l1);
        if (many)
        {
            for (int targetOffset = sourceOffset + l1; ++sourceOffset < sourceLength;)
            {
                Expression e = source[sourceOffset];
                if (tester(e))
                {
                    int l2 = e.GetSubexpressionCount();
                    CopyArgs(e, targetOffset, l2);
                    targetOffset += l2;
                }
                else
                {
                    args[targetOffset++] = e;
                }
            }
        }
        else if (forceCopy)
        {
            Array.Copy(source, sourceOffset + 1, args, sourceOffset + l1, sourceLength - sourceOffset - 1);
        }
    }

    private void CopyArgs(Expression e, int offset, int count)
    {
        if (e is OperationN)
        {
            Array.Copy(((OperationN)e).args, 0, args, offset, count);
        }
        else
        {
            for (int j = 0; j < count; j++)
            {
                args[offset + j] = e.GetSubexpression(j)!;
            }
        }
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        foreach (Expression e in args)
        {
            e.SetEvaluatable(tableFilter, value);
        }
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        foreach (Expression e in args)
        {
            e.UpdateAggregate(session, stage);
        }
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        foreach (Expression e in args)
        {
            if (!e.IsEverything(visitor))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        int cost = args.Length + 1;
        foreach (Expression e in args)
        {
            cost += e.GetCost();
        }

        return cost;
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount() => args.Length;

    /// <inheritdoc/>
    public override Expression GetSubexpression(int index) => args[index];
}
