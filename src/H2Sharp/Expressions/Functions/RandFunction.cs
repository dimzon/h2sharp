/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.RandFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A RAND, SECURE_RAND, or RANDOM_UUID function.
/// </summary>
public sealed class RandFunction : Function0_1
{
    /// <summary>
    /// RAND() (non-standard).
    /// </summary>
    public const int Rand = 0;

    /// <summary>
    /// SECURE_RAND() (non-standard).
    /// </summary>
    public const int SecureRand = Rand + 1;

    /// <summary>
    /// RANDOM_UUID() (non-standard).
    /// </summary>
    public const int RandomUuid = SecureRand + 1;

    private static readonly string[] Names = [ //
        "RAND", "SECURE_RAND", "RANDOM_UUID" //
    ];

    private readonly int function;

    /// <summary>
    /// Creates a random function.
    /// </summary>
    /// <param name="arg">the argument, or null</param>
    /// <param name="function">one of <see cref="Rand"/>, <see cref="SecureRand"/>, or <see cref="RandomUuid"/></param>
    public RandFunction(Expression? arg, int function)
        : base(arg)
    {
        this.function = function;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value? v;
        if (arg != null)
        {
            v = arg.GetValue(session);
            if (v == ValueNull.Instance)
            {
                return ValueNull.Instance;
            }
        }
        else
        {
            v = null;
        }

        switch (function)
        {
            case Rand:
                {
                    Random random = session.GetRandom();
                    if (v != null)
                    {
                        random = session.ReseedRandom(v.GetInt());
                    }

                    v = ValueDouble.Get(random.NextDouble());
                    break;
                }

            case SecureRand:
                v = ValueVarbinary.GetNoCopy(MathUtils.SecureRandomBytes(v!.GetInt()));
                break;
            case RandomUuid:
                // A test that holds the UUIDs still sets a factory on the session, and the
                // statement gets the UUID the factory answers with.
                int version = v != null ? v.GetInt() : 4;
                v = session.UuidProvider is not null
                    ? ValueUuid.Get(session.UuidProvider())
                    : session.Extensions.UuidGenerator is not null
                        ? ValueUuid.Get(session.Extensions.UuidGenerator(version))
                        : ValueUuid.GetNewRandom(version);
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        return v;
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        if (arg != null)
        {
            arg = arg.Optimize(session);
        }

        switch (function)
        {
            case Rand:
                type = TypeInfo.TypeDouble;
                break;
            case SecureRand:
                {
                    Value? v;
                    type = arg!.IsConstant() && (v = arg.GetValue(session)) != ValueNull.Instance
                        ? TypeInfo.GetTypeInfo(Value.Varbinary, Math.Max(v.GetInt(), 1), 0, null)
                        : TypeInfo.TypeVarbinary;
                    break;
                }

            case RandomUuid:
                type = TypeInfo.TypeUuid;
                break;
            default:
                throw DbException.GetInternalError("function=" + function);
        }

        return this;
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        if (visitor.GetVisitorType() == ExpressionVisitor.Deterministic)
        {
            return false;
        }

        return base.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override string GetName() => Names[function];
}
