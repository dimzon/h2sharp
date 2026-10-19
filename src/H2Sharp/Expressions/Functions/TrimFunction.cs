/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.TrimFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;

using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A TRIM, LTRIM, RTRIM, or BTRIM function.
/// </summary>
public sealed class TrimFunction : Function1_2
{
    /// <summary>
    /// The LEADING flag.
    /// </summary>
    public const int Leading = 1;

    /// <summary>
    /// The TRAILING flag.
    /// </summary>
    public const int Trailing = 2;

    /// <summary>
    /// The multi-character flag.
    /// </summary>
    public const int MultiCharacter = 4;

    private readonly int flags;

    /// <summary>
    /// Creates a trim function.
    /// </summary>
    /// <param name="from">the string to trim</param>
    /// <param name="space">the characters to remove, or null for a space</param>
    /// <param name="flags">the flags</param>
    public TrimFunction(Expression from, Expression? space, int flags)
        : base(from, space)
    {
        this.flags = flags;
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value v1, Value? v2)
    {
        return ValueVarchar.Get(
            StringUtils.Trim(
                v1.GetString()!,
                (flags & Leading) != 0,
                (flags & Trailing) != 0,
                v2 != null ? v2.GetString() : " "),
            session);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        if (right != null)
        {
            right = right.Optimize(session);
        }

        type = TypeInfo.GetTypeInfo(Value.Varchar, left.GetTypeInfo().GetPrecision(), 0, null);
        if (left.IsConstant() && (right == null || right.IsConstant()))
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append(GetName()).Append('(');
        if ((flags & MultiCharacter) != 0)
        {
            _ = left.GetUnenclosedSql(builder, sqlFlags);
            if (right != null)
            {
                _ = right.GetUnenclosedSql(builder.Append(", "), sqlFlags);
            }
        }
        else
        {
            bool needFrom = false;
            switch (flags)
            {
                case Leading:
                    _ = builder.Append("LEADING ");
                    needFrom = true;
                    break;
                case Trailing:
                    _ = builder.Append("TRAILING ");
                    needFrom = true;
                    break;
            }

            if (right != null)
            {
                _ = right.GetUnenclosedSql(builder, sqlFlags);
                needFrom = true;
            }

            if (needFrom)
            {
                _ = builder.Append(" FROM ");
            }

            _ = left.GetUnenclosedSql(builder, sqlFlags);
        }

        return builder.Append(')');
    }

    /// <inheritdoc/>
    public override string GetName()
    {
        switch (flags)
        {
            case Leading | MultiCharacter:
                return "LTRIM";
            case Trailing | MultiCharacter:
                return "RTRIM";
            case Leading | Trailing | MultiCharacter:
                return "BTRIM";
            default:
                return "TRIM";
        }
    }
}
