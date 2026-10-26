/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.UnaryOperation.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// Unary operation. Only negation operation is currently supported.
/// </summary>
public class UnaryOperation : Operation1
{
    /// <summary>
    /// Creates a unary operation over the given argument.
    /// </summary>
    /// <param name="arg">the argument</param>
    public UnaryOperation(Expression arg)
        : base(arg)
    {
    }

    /// <inheritdoc/>
    public override bool NeedParentheses() => true;

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        // The space stays: without it the text could become "--1", which is a remark.
        return arg.GetSql(builder.Append("- "), sqlFlags, Expression.AutoParentheses);
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value a = arg.GetValue(session).ConvertToType(type!);
        return a is ValueNull ? a : a.Negate();
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        arg = arg.Optimize(session);
        type = arg.GetTypeInfo();
        if (type.GetValueType() == Value.Unknown)
        {
            type = TypeInfo.TypeNumericFloatingPoint;
        }
        else if (type.GetValueType() == Value.Enum)
        {
            type = TypeInfo.TypeInteger;
        }

        if (arg.IsConstant())
        {
            return ValueExpression.Get(GetValue(session));
        }

        return this;
    }
}
