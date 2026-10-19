/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.FunctionN.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;

using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// Function with many arguments.
/// </summary>
public abstract class FunctionN : OperationN, INamedExpression
{
    /// <summary>
    /// Creates a function with the given arguments.
    /// </summary>
    /// <param name="args">the arguments</param>
    protected FunctionN(Expression[] args)
        : base(args)
    {
    }

    /// <inheritdoc/>
    public abstract string GetName();

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value? v1, v2, v3;
        int count = args.Length;
        if (count >= 1)
        {
            v1 = args[0].GetValue(session);
            if (v1 == ValueNull.Instance)
            {
                return ValueNull.Instance;
            }

            if (count >= 2)
            {
                v2 = args[1].GetValue(session);
                if (v2 == ValueNull.Instance)
                {
                    return ValueNull.Instance;
                }

                if (count >= 3)
                {
                    v3 = args[2].GetValue(session);
                    if (v3 == ValueNull.Instance)
                    {
                        return ValueNull.Instance;
                    }
                }
                else
                {
                    v3 = null;
                }
            }
            else
            {
                v3 = v2 = null;
            }
        }
        else
        {
            v3 = v2 = v1 = null;
        }

        return GetValue(session, v1, v2, v3);
    }

    /// <summary>
    /// Returns the value of this function.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="v1">the value of first argument, or null</param>
    /// <param name="v2">the value of second argument, or null</param>
    /// <param name="v3">the value of third argument, or null</param>
    /// <returns>the resulting value</returns>
    protected virtual Value GetValue(SessionLocal session, Value? v1, Value? v2, Value? v3)
    {
        throw DbException.GetInternalError();
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        return WriteExpressions(builder.Append(GetName()).Append('('), args, sqlFlags).Append(')');
    }
}
