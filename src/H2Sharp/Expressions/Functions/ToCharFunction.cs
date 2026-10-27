/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * TO_CHAR for date-time values. The date-time form delegates to the .NET
 * pattern implementation in DateTimeFormatFunction; the Oracle number format
 * model is not part of this port yet.
 */
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>Oracle-compatible TO_CHAR for date-time values.</summary>
public sealed class ToCharFunction : FunctionN
{
    /// <summary>Creates the function.</summary>
    public ToCharFunction(Expression value, Expression? format, Expression? nls)
        : base([value, format ?? ValueExpression.Null, nls ?? ValueExpression.Null])
    {
    }

    /// <inheritdoc/>
    public override string GetName() => "TO_CHAR";

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value value = args[0].GetValue(session);
        if (value is ValueNull)
        {
            return ValueNull.Instance;
        }

        Value format = args[1].GetValue(session);
        if (format is ValueNull)
        {
            return ValueNull.Instance;
        }

        // args[2] is the NLS parameter; .NET formatting uses the current culture here.
        return ValueVarchar.Get(
            DateTimeFormatFunction.Format(value, format.GetString()!, locale: null, timeZone: null),
            session);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = OptimizeArguments(session, true);
        type = TypeInfo.TypeVarchar;
        if (allConst && !session.IsParsingCreateView())
        {
            return TypedValueExpression.GetTypedIfNull(GetValue(session), type);
        }

        return this;
    }
}
