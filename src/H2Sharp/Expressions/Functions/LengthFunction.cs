/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.LengthFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// CHAR_LENGTH(), or OCTET_LENGTH() function.
/// </summary>
public sealed class LengthFunction : Function1
{
    /// <summary>
    /// CHAR_LENGTH().
    /// </summary>
    public const int CharLength = 0;

    /// <summary>
    /// OCTET_LENGTH().
    /// </summary>
    public const int OctetLength = CharLength + 1;

    /// <summary>
    /// BIT_LENGTH() (non-standard).
    /// </summary>
    public const int BitLength = OctetLength + 1;

    private static readonly string[] Names = { //
        "CHAR_LENGTH", "OCTET_LENGTH", "BIT_LENGTH" //
    };

    private readonly int function;

    /// <summary>
    /// Creates a new length function.
    /// </summary>
    /// <param name="arg">the argument</param>
    /// <param name="function">one of <see cref="CharLength"/>, <see cref="OctetLength"/>, <see cref="BitLength"/></param>
    public LengthFunction(Expression arg, int function)
        : base(arg)
    {
        this.function = function;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v = arg.GetValue(session);
        if (v == ValueNull.Instance)
        {
            return ValueNull.Instance;
        }

        long l;
        switch (function)
        {
            case CharLength:
                switch (v.GetValueType())
                {
                    case Value.Binary:
                    case Value.Varbinary:
                    case Value.Blob:
                        // Binary strings do not have characters, return their length in bytes
                        // instead, as PostgreSQL and MySQL do.
                        l = v.OctetLength();
                        break;
                    default:
                        l = v.CharLength();
                        break;
                }

                break;
            case OctetLength:
                l = v.OctetLength();
                break;
            case BitLength:
                l = v.OctetLength() * 8;
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        return ValueBigint.Get(l);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        arg = arg.Optimize(session);
        type = TypeInfo.TypeBigint;
        if (arg.IsConstant())
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
}
