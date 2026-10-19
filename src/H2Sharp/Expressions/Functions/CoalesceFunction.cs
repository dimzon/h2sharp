/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.CoalesceFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * optimize() asks TypeInfo.getHigherType(Typed[]) for the common type of its arguments. The
 * Typed interface is not ported; the port takes the argument types instead, which is the same
 * list of types the reference builds internally (TypeInfo.GetHigherType(TypeInfo[])).
 */
using System.Text;

using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A COALESCE, GREATEST, or LEAST function.
/// </summary>
public sealed class CoalesceFunction : FunctionN
{
    /// <summary>
    /// COALESCE().
    /// </summary>
    public const int Coalesce = 0;

    /// <summary>
    /// GREATEST() (non-standard).
    /// </summary>
    public const int Greatest = Coalesce + 1;

    /// <summary>
    /// LEAST() (non-standard).
    /// </summary>
    public const int Least = Greatest + 1;

    private static readonly string[] Names = { //
        "COALESCE", "GREATEST", "LEAST" //
    };

    private readonly int function;

    private bool ignoreNulls;

    /// <summary>
    /// Creates a new function.
    /// </summary>
    /// <param name="function">one of <see cref="Coalesce"/>, <see cref="Greatest"/>, <see cref="Least"/></param>
    public CoalesceFunction(int function)
        : this(function, new Expression[4])
    {
    }

    /// <summary>
    /// Creates a new function with the given arguments.
    /// </summary>
    /// <param name="function">one of <see cref="Coalesce"/>, <see cref="Greatest"/>, <see cref="Least"/></param>
    /// <param name="args">the arguments</param>
    public CoalesceFunction(int function, params Expression[] args)
        : base(args)
    {
        this.function = function;
    }

    /// <summary>
    /// Sets whether NULL values are ignored.
    /// </summary>
    /// <param name="ignoreNulls">whether NULL values are ignored</param>
    public void SetIgnoreNulls(bool ignoreNulls)
    {
        this.ignoreNulls = ignoreNulls;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v;
        switch (function)
        {
            case Coalesce:
                v = ValueNull.Instance;
                for (int i = 0, l = args.Length; i < l; i++)
                {
                    Value v2 = args[i].GetValue(session);
                    if (v2 != ValueNull.Instance)
                    {
                        v = v2.ConvertToType(type!);
                        break;
                    }
                }

                break;
            case Greatest:
            case Least:
                v = GreatestOrLeast(session);
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        return v;
    }

    private Value GreatestOrLeast(SessionLocal session)
    {
        Value v = ValueNull.Instance;
        Value? x = null;
        for (int i = 0, l = args.Length; i < l; i++)
        {
            Value v2 = args[i].GetValue(session);
            if (v2 != ValueNull.Instance)
            {
                v2 = v2.ConvertToType(type!);
                if (v == ValueNull.Instance)
                {
                    if (x == null)
                    {
                        v = v2;
                    }
                    else
                    {
                        int comp = session.CompareWithNull(x, v2, false);
                        if (comp == int.MinValue)
                        {
                            x = GetWithNull(x, v2);
                        }
                        else if (Test(comp))
                        {
                            v = v2;
                            x = null;
                        }
                    }
                }
                else
                {
                    int comp = session.CompareWithNull(v, v2, false);
                    if (comp == int.MinValue)
                    {
                        if (i + 1 == l)
                        {
                            return ValueNull.Instance;
                        }

                        x = GetWithNull(v, v2);
                        v = ValueNull.Instance;
                    }
                    else if (Test(comp))
                    {
                        v = v2;
                    }
                }
            }
            else if (!ignoreNulls)
            {
                return ValueNull.Instance;
            }
        }

        return v;
    }

    private static Value? GetWithNull(Value v, Value v2)
    {
        Value? x = v.GetValueWithFirstNull(v2);
        return x != null ? x : v;
    }

    private bool Test(int comp)
    {
        return function == Greatest ? comp < 0 : comp > 0;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = OptimizeArguments(session, true);
        TypeInfo[] types = new TypeInfo[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            types[i] = args[i].GetTypeInfo();
        }

        type = TypeInfo.GetHigherType(types);
        if (type.GetValueType() <= Value.Null)
        {
            type = TypeInfo.TypeVarchar;
        }

        if (allConst)
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    /// <inheritdoc/>
    public override string GetName()
    {
        return Names[function];
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        base.GetUnenclosedSql(builder, sqlFlags);
        if (function == Greatest || function == Least)
        {
            builder.Append(ignoreNulls ? " IGNORE NULLS" : " RESPECT NULLS");
        }

        return builder;
    }
}
