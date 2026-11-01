/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.SignalFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text.RegularExpressions;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// A SIGNAL function: throws an exception with the given SQLState and message.
/// </summary>
public sealed class SignalFunction : Function2
{
    private static readonly Regex SignalPattern = new("^[0-9A-Z]{5}$", RegexOptions.Compiled);

    /// <summary>
    /// Creates a new SIGNAL function.
    /// </summary>
    /// <param name="arg1">the SQLState</param>
    /// <param name="arg2">the message</param>
    public SignalFunction(Expression arg1, Expression arg2)
        : base(arg1, arg2)
    {
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value v1, Value v2)
    {
        string sqlState = v1.GetString() ?? string.Empty;
        if (sqlState.StartsWith("00", StringComparison.Ordinal) || !SignalPattern.IsMatch(sqlState))
        {
            throw DbException.GetInvalidValueException("SQLSTATE", sqlState);
        }

        throw DbException.FromUser(sqlState, v2.GetString() ?? string.Empty);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        right = right.Optimize(session);
        type = TypeInfo.TypeNull;
        return this;
    }

    /// <inheritdoc/>
    public override string GetName() => "SIGNAL";
}
