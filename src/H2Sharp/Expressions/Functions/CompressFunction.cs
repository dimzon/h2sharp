/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.CompressFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Tools;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A COMPRESS or EXPAND function.
/// </summary>
public sealed class CompressFunction : Function1_2
{
    /// <summary>
    /// COMPRESS() (non-standard).
    /// </summary>
    public const int Compress = 0;

    /// <summary>
    /// EXPAND() (non-standard).
    /// </summary>
    public const int Expand = Compress + 1;

    private static readonly string[] Names = [
        "COMPRESS", "EXPAND"
    ];

    private readonly int function;

    /// <summary>
    /// Creates a compression function.
    /// </summary>
    /// <param name="arg1">the first argument</param>
    /// <param name="arg2">the second argument, or null</param>
    /// <param name="function">one of <see cref="Compress"/> or <see cref="Expand"/></param>
    public CompressFunction(Expression arg1, Expression? arg2, int function)
        : base(arg1, arg2)
    {
        this.function = function;
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value v1, Value? v2)
    {
        switch (function)
        {
            case Compress:
                v1 = ValueVarbinary.GetNoCopy(
                    CompressTool.GetInstance().Compress(
                        v1.GetBytesNoCopy()!, v2 != null ? v2.GetString() : null));
                break;
            case Expand:
                v1 = ValueVarbinary.GetNoCopy(CompressTool.GetInstance().Expand(v1.GetBytesNoCopy()!));
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        return v1;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        if (right != null)
        {
            right = right.Optimize(session);
        }

        type = TypeInfo.TypeVarbinary;
        if (left.IsConstant() && (right == null || right.IsConstant()))
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }

    /// <inheritdoc/>
    public override string GetName() => Names[function];
}
