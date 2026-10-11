/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.Condition.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Represents a condition returning a boolean value, or NULL.
 *
 * A condition is not a boolean: it is a boolean *or NULL*, and that third value is the whole
 * reason this class exists separately from a plain expression. "Everything can be a condition"
 * is the design, so the type here is BOOLEAN and not a special "condition" type — but the NULL
 * has to survive every comparison, which is why the compare helpers below answer with a Value
 * and never with a bool.
 */
using H2Sharp.Engine;
using H2Sharp.Expressions.Functions;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>
/// Represents a condition returning a boolean value, or NULL.
/// </summary>
public abstract class Condition : Expression
{
    /// <summary>
    /// Add a cast around the expression (if necessary) so that the type is boolean.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="expression">the expression</param>
    /// <returns>the new expression</returns>
    public static Expression CastToBoolean(SessionLocal session, Expression expression)
    {
        if (expression.GetTypeInfo().GetValueType() == Value.Boolean)
        {
            return expression;
        }

        return new CastSpecification(expression, TypeInfo.TypeBoolean);
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        return TypeInfo.TypeBoolean;
    }
}
