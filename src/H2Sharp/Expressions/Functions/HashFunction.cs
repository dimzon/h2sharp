/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.HashFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>The HASH(algorithm, value [, iterations]) function.</summary>
public sealed class HashFunction : FunctionN
{
    /// <summary>Creates a HASH function.</summary>
    /// <param name="algorithm">the algorithm expression</param>
    /// <param name="value">the value expression</param>
    /// <param name="iterations">the iteration expression, or null</param>
    public HashFunction(Expression algorithm, Expression value, Expression? iterations)
        : base(iterations is null ? [algorithm, value] : [algorithm, value, iterations])
    {
    }

    /// <inheritdoc/>
    public override string GetName() => "HASH";

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value? v1, Value? v2, Value? v3)
    {
        string algorithm = v1!.GetString()
            ?? throw DbException.GetInvalidValueException("algorithm", v1);
        int iterations = v3 is null ? 1 : v3.GetInt();
        if (iterations <= 0)
        {
            throw DbException.GetInvalidValueException("iterations", iterations);
        }

        using HashAlgorithm hash = session.Extensions.CreateHashAlgorithm(algorithm);
        byte[] digest;
        switch (v2!.GetValueType())
        {
            case Value.Char:
            case Value.Varchar:
            case Value.VarcharIgnorecase:
                digest = hash.ComputeHash(Encoding.UTF8.GetBytes(v2.GetString()!));
                break;
            case Value.Blob:
            case Value.Clob:
                using (Stream input = v2.GetInputStream())
                {
                    digest = hash.ComputeHash(input);
                }

                break;
            default:
                digest = hash.ComputeHash(v2.GetBytes() ?? []);
                break;
        }

        for (int i = 1; i < iterations; i++)
        {
            digest = hash.ComputeHash(digest);
        }

        return ValueVarbinary.GetNoCopy(digest);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = OptimizeArguments(session, true);
        type = TypeInfo.TypeVarbinary;
        return allConst ? TypedValueExpression.GetTypedIfNull(GetValue(session), type) : this;
    }
}
