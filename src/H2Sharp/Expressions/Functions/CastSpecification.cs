/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.CastSpecification.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A cast of an expression to a type.
 *
 * A function in the reference, with the template form (CAST with FORMAT) left out: no reader
 * builds it, and a field nothing writes is a promise nothing checks.
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// An expression to explicitly convert a value to a type.
/// </summary>
public sealed class CastSpecification : Expression
{
    private Expression expr;

    private readonly TypeInfo castToType;

    /// <summary>
    /// Creates a cast.
    /// </summary>
    /// <param name="expr">the expression to cast</param>
    /// <param name="castToType">the type to cast to</param>
    public CastSpecification(Expression expr, TypeInfo castToType)
    {
        this.expr = expr;
        this.castToType = castToType;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v = expr.GetValue(session);
        return v.CastToValue(castToType, session.CastDataProvider);
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        return castToType;
    }

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        expr.MapColumns(resolver, level, state);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        expr = expr.Optimize(session);
        if (expr.IsConstant())
        {
            Value v = GetValue(session);
            if (v is ValueNull || CanOptimizeCast(
                expr.GetTypeInfo().GetValueType(), castToType.GetValueType()))
            {
                return TypedValueExpression.Get(v, castToType);
            }
        }

        return this;
    }

    /// <inheritdoc/>
    public override bool IsConstant()
        => expr is ValueExpression
            && CanOptimizeCast(expr.GetTypeInfo().GetValueType(), castToType.GetValueType());

    private static bool CanOptimizeCast(int src, int dst)
    {
        switch (src)
        {
            case Value.Time:
                switch (dst)
                {
                    case Value.TimeTz:
                    case Value.Timestamp:
                    case Value.TimestampTz:
                        return false;
                }

                break;
            case Value.TimeTz:
                switch (dst)
                {
                    case Value.Time:
                    case Value.Timestamp:
                    case Value.TimestampTz:
                        return false;
                }

                break;
            case Value.Date:
                if (dst == Value.TimestampTz)
                {
                    return false;
                }

                break;
            case Value.Timestamp:
                switch (dst)
                {
                    case Value.TimeTz:
                    case Value.TimestampTz:
                        return false;
                }

                break;
            case Value.TimestampTz:
                switch (dst)
                {
                    case Value.Time:
                    case Value.Date:
                    case Value.Timestamp:
                        return false;
                }

                break;
        }

        return true;
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool b)
    {
        expr.SetEvaluatable(tableFilter, b);
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append("CAST(");
        expr.GetUnenclosedSql(builder, expr is ValueExpression ? sqlFlags | HasSql.NoCasts : sqlFlags)
            .Append(" AS ");
        castToType.GetSql(builder, sqlFlags);
        return builder.Append(')');
    }

    /// <summary>
    /// Gets the type this cast converts to.
    /// </summary>
    public TypeInfo CastType => castToType;

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        expr.UpdateAggregate(session, stage);
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return expr.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        return expr.GetCost();
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount()
    {
        return 1;
    }

    /// <inheritdoc/>
    public override Expression? GetSubexpression(int index)
    {
        return index == 0 ? expr : throw new System.IndexOutOfRangeException();
    }
}
