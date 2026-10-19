/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.ConcatFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A CONCAT or CONCAT_WS function.
/// </summary>
public sealed class ConcatFunction : FunctionN
{
    /// <summary>
    /// CONCAT() (non-standard).
    /// </summary>
    public const int Concat = 0;

    /// <summary>
    /// CONCAT_WS() (non-standard).
    /// </summary>
    public const int ConcatWs = Concat + 1;

    private static readonly string[] Names = { //
        "CONCAT", "CONCAT_WS" //
    };

    private readonly int function;

    /// <summary>
    /// Creates a new function.
    /// </summary>
    /// <param name="function">one of <see cref="Concat"/>, <see cref="ConcatWs"/></param>
    public ConcatFunction(int function)
        : this(function, new Expression[4])
    {
    }

    /// <summary>
    /// Creates a new function with the given arguments.
    /// </summary>
    /// <param name="function">one of <see cref="Concat"/>, <see cref="ConcatWs"/></param>
    /// <param name="args">the arguments</param>
    public ConcatFunction(int function, params Expression[] args)
        : base(args)
    {
        this.function = function;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        int i = 0;
        string? separator = null;
        if (function == ConcatWs)
        {
            i = 1;
            separator = args[0].GetValue(session).GetString();
        }

        System.Text.StringBuilder builder = new();
        bool f = false;
        for (int l = args.Length; i < l; i++)
        {
            Value v = args[i].GetValue(session);
            if (v != ValueNull.Instance)
            {
                if (separator != null)
                {
                    if (f)
                    {
                        builder.Append(separator);
                    }

                    f = true;
                }

                builder.Append(v.GetString());
            }
        }

        return ValueVarchar.Get(builder.ToString(), session);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = OptimizeArguments(session, true);
        int i = 0;
        long extra = 0L;
        if (function == ConcatWs)
        {
            i = 1;
            extra = GetPrecision(0);
        }

        long precision = 0L;
        int l = args.Length;
        bool f = false;
        for (; i < l; i++)
        {
            if (args[i].IsNullConstant())
            {
                continue;
            }

            precision = DataType.AddPrecision(precision, GetPrecision(i));
            if (extra != 0L && f)
            {
                precision = DataType.AddPrecision(precision, extra);
            }

            f = true;
        }

        type = TypeInfo.GetTypeInfo(Value.Varchar, precision, 0, null);
        if (allConst)
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        InlineSubexpressions(t => t is ConcatFunction c && c.function == function);
        return this;
    }

    private long GetPrecision(int i)
    {
        TypeInfo t = args[i].GetTypeInfo();
        int valueType = t.GetValueType();
        if (valueType == Value.Null)
        {
            return 0L;
        }

        if (DataType.IsCharacterStringType(valueType))
        {
            return t.GetPrecision();
        }

        return long.MaxValue;
    }

    /// <inheritdoc/>
    public override string GetName()
    {
        return Names[function];
    }
}
