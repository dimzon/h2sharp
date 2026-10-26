/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Functions;
using H2Sharp.Values;

namespace H2Sharp.Extensions;

/// <summary>A scalar CLR function registered on a closed connection.</summary>
internal sealed class ClrFunction : FunctionN
{
    private readonly UdfDefinition definition;

    internal ClrFunction(UdfDefinition definition, Expression[] args)
        : base(args)
    {
        this.definition = definition;
        type = definition.ResultType;
    }

    /// <inheritdoc/>
    public override string GetName() => definition.Name;

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value[] values = new Value[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            values[i] = args[i].GetValue(session);
        }

        return definition.Invoke(values);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = true;
        for (int i = 0; i < args.Length; i++)
        {
            args[i] = args[i].Optimize(session);
            allConst &= args[i].IsConstant();
        }

        type = definition.ResultType;
        if (definition.Deterministic && allConst)
        {
            return ValueExpression.Get(GetValue(session));
        }

        return this;
    }
}
