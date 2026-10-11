/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.ValueExpression.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A literal.
 *
 * Three of the instances below are singletons and it matters why: TRUE, FALSE and NULL are what
 * the optimiser substitutes for a condition that folded to a constant, and they are also what a
 * condition that folded to *false* becomes. That substitution is why a WHERE clause can
 * disappear from a plan at all — if the "false" case built a fresh expression every time, the
 * optimiser's own identity checks would miss it.
 *
 * The value is converted on construction, not on use. A literal is written once and read many
 * times, and the conversion is the expensive half; the cost of converting again per row would be
 * paid on every row of every scan that mentions the literal.
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// A constant value of a known type.
/// </summary>
public class ValueExpression : Expression
{
    /// <summary>The literal NULL.</summary>
    public static readonly ValueExpression Null = new(ValueNull.Instance);

    /// <summary>The literal TRUE.</summary>
    public static readonly ValueExpression True = new(ValueBoolean.True);

    /// <summary>The literal FALSE.</summary>
    public static readonly ValueExpression False = new(ValueBoolean.False);

    private readonly Value value;

    /// <summary>
    /// Creates an expression for the given value.
    /// </summary>
    /// <param name="value">the value</param>
    protected ValueExpression(Value value)
    {
        this.value = value;
    }

    /// <summary>
    /// Gets the value.
    /// </summary>
    /// <param name="value">the value</param>
    /// <returns>the expression</returns>
    public static ValueExpression Get(Value value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value switch
        {
            ValueNull => Null,
            ValueBoolean b => b.GetBoolean() ? True : False,
            _ => new ValueExpression(value),
        };
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        return value;
    }

    /// <summary>
    /// Gets the value this expression stands for, without a session.
    /// </summary>
    /// <remarks>
    /// Only a literal has a value that needs no run behind it. Every other expression has to ask
    /// the session it is being read in, and one that asks is a bug in the parser, not a value.
    /// </remarks>
    /// <returns>the value</returns>
    public Value GetValueFromLiteral()
    {
        return GetValue(null!);
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        return value.GetTypeInfo();
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        return this;
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        return value.GetSql(builder, sqlFlags);
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return true;
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        return 0;
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeIfStaticallyKnown(SessionLocal session)
    {
        return GetTypeInfo();
    }

    /// <inheritdoc/>
    public override bool IsConstant()
    {
        return true;
    }

    /// <inheritdoc/>
    public override bool IsNullConstant()
    {
        return value is ValueNull;
    }
}
