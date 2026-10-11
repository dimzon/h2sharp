/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.TypedValueExpression.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * An expression representing a constant value with a type cast.
 *
 * UNKNOWN exists because a condition has to be able to *be* NULL while still having the type
 * BOOLEAN. A plain ValueExpression of NULL has type NULL, and a condition whose type is NULL is
 * a different thing in generated SQL and in the optimiser's eyes. So the unknown answer gets its
 * own singleton, typed BOOLEAN, and every comparison that folds to unknown produces it.
 *
 * getTypedIfNull is the other half and it is the one that matters for correctness: it returns a
 * plain value expression unless the value is NULL. Typing NULL as BOOLEAN is only right where a
 * boolean is required; everywhere else it would make "WHERE NULL" look like a boolean and the
 * optimiser would fold it as one.
 */
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// An expression representing a constant value with a type cast.
/// </summary>
public class TypedValueExpression : ValueExpression
{
    /// <summary>
    /// The expression represents the SQL UNKNOWN value.
    /// </summary>
    public static readonly TypedValueExpression Unknown =
        new(ValueNull.Instance, TypeInfo.TypeBoolean);

    private readonly TypeInfo type;

    private TypedValueExpression(Value value, TypeInfo type)
        : base(value)
    {
        this.type = type;
    }

    /// <summary>
    /// Create a new expression with the given value and type.
    /// </summary>
    /// <param name="value">the value</param>
    /// <param name="type">the value type</param>
    /// <returns>the expression</returns>
    public static ValueExpression Get(Value value, TypeInfo type)
    {
        return GetImpl(value, type, true);
    }

    /// <summary>
    /// Create a new typed value expression with the given value and type if
    /// value is NULL, or a plain value expression otherwise.
    /// </summary>
    /// <param name="value">the value</param>
    /// <param name="type">the value type</param>
    /// <returns>the expression</returns>
    public static ValueExpression GetTypedIfNull(Value value, TypeInfo type)
    {
        return GetImpl(value, type, false);
    }

    private static ValueExpression GetImpl(Value value, TypeInfo type, bool typed)
    {
        if (!typed && value is not ValueNull)
        {
            return Get(value);
        }

        return value is ValueNull && type == TypeInfo.TypeBoolean && !typed
            ? ValueExpression.Get(value)
            : new TypedValueExpression(value, type);
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        return type;
    }
}
