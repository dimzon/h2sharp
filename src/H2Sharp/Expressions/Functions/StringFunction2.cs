/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.StringFunction2.
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
/// A string function with two arguments.
/// </summary>
public sealed class StringFunction2 : Function2
{
    /// <summary>
    /// LEFT() (non-standard).
    /// </summary>
    public const int Left = 0;

    /// <summary>
    /// RIGHT() (non-standard).
    /// </summary>
    public const int Right = Left + 1;

    /// <summary>
    /// REPEAT() (non-standard).
    /// </summary>
    public const int Repeat = Right + 1;

    private static readonly string[] Names = ["LEFT", "RIGHT", "REPEAT"];

    private readonly int function;

    /// <summary>
    /// Creates a string function with two arguments.
    /// </summary>
    /// <param name="arg1">the first argument</param>
    /// <param name="arg2">the second argument</param>
    /// <param name="function">the function code</param>
    public StringFunction2(Expression arg1, Expression arg2, int function)
        : base(arg1, arg2)
    {
        this.function = function;
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value v1, Value v2)
    {
        string s = v1.GetString()!;
        int count = v2.GetInt();
        if (count <= 0)
        {
            return ValueVarchar.Get("", session);
        }

        int length = s.Length;
        switch (function)
        {
            case Left:
                if (count > length)
                {
                    count = length;
                }

                s = s[..count];
                break;
            case Right:
                if (count > length)
                {
                    count = length;
                }

                s = s[(length - count)..];
                break;
            case Repeat:
                {
                    StringBuilder builder = new(length * count);
                    while (count-- > 0)
                    {
                        _ = builder.Append(s);
                    }

                    s = builder.ToString();
                    break;
                }

            default:
                throw DbException.GetInternalError("function=" + function);
        }

        return ValueVarchar.Get(s, session);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        right = right.Optimize(session);
        switch (function)
        {
            case Left:
            case Right:
                type = TypeInfo.GetTypeInfo(Value.Varchar, left.GetTypeInfo().GetPrecision(), 0, null);
                break;
            case Repeat:
                type = TypeInfo.TypeVarchar;
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        if (left.IsConstant() && right.IsConstant())
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    /// <inheritdoc/>
    public override string GetName() => Names[function];
}
