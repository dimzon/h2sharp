/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.CastSpecification.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A cast of an expression to a type, with the format template the reference reads after the
 * type: CAST(x AS DATE FORMAT 'yyyy-MM-dd') writes the value with the template and reads it
 * back with it.
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
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

    private Expression? template;

    private readonly TypeInfo castToType;

    /// <summary>
    /// Creates a cast.
    /// </summary>
    /// <param name="expr">the expression to cast</param>
    /// <param name="castToType">the type to cast to</param>
    public CastSpecification(Expression expr, TypeInfo castToType)
        : this(expr, castToType, null)
    {
    }

    /// <summary>
    /// Creates a cast with a format template.
    /// </summary>
    /// <param name="expr">the expression to cast</param>
    /// <param name="castToType">the type to cast to</param>
    /// <param name="template">the format template, or null when there is none</param>
    public CastSpecification(Expression expr, TypeInfo castToType, Expression? template)
    {
        this.expr = expr;
        this.castToType = castToType;
        this.template = template;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value v = expr.GetValue(session);
        if (template is not null)
        {
            v = GetValueWithTemplate(v, template.GetValue(session), session);
        }

        return v.CastToValue(castToType, session.CastDataProvider);
    }

    /// <summary>
    /// Converts the value with the template, which is the reference's getValueWithTemplate.
    /// </summary>
    /// <param name="value">the value</param>
    /// <param name="template">the template</param>
    /// <param name="session">the session</param>
    /// <returns>the converted value</returns>
    private Value GetValueWithTemplate(Value value, Value template, SessionLocal session)
    {
        if (value is ValueNull)
        {
            return ValueNull.Instance;
        }

        int valueType = value.GetValueType();
        if (DataType.IsDateTimeType(valueType))
        {
            if (DataType.IsCharacterStringType(castToType.GetValueType()))
            {
                string? formatted = DateTimeTemplate.Of(template.GetString() ?? string.Empty)
                    .Format(value);
                return ValueVarchar.Get(formatted ?? string.Empty, session.CastDataProvider);
            }
        }
        else if (DataType.IsCharacterStringType(valueType))
        {
            if (DataType.IsDateTimeType(castToType.GetValueType()))
            {
                return DateTimeTemplate.Of(template.GetString() ?? string.Empty)
                    .Parse(value.GetString() ?? string.Empty, castToType, session.CastDataProvider);
            }
        }

        StringBuilder builder = new();
        _ = value.GetTypeInfo().GetSql(builder, HasSql.TraceSqlFlags).Append(" to ");
        _ = castToType.GetSql(builder, HasSql.DefaultSqlFlags);
        throw DbException.GetUnsupportedException("CAST with template from " + builder);
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
        template?.MapColumns(resolver, level, state);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        expr = expr.Optimize(session);
        if (template is not null)
        {
            template = template.Optimize(session);
        }

        if (expr.IsConstant() && (template is null || template.IsConstant()))
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
            && (template is null || template.IsConstant())
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
        template?.SetEvaluatable(tableFilter, b);
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append("CAST(");
        expr.GetUnenclosedSql(builder, expr is ValueExpression ? sqlFlags | HasSql.NoCasts : sqlFlags)
            .Append(" AS ");
        castToType.GetSql(builder, sqlFlags);
        if (template is not null)
        {
            template.GetSql(builder.Append(" FORMAT "), sqlFlags);
        }

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
        template?.UpdateAggregate(session, stage);
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        return expr.IsEverything(visitor) && (template is null || template.IsEverything(visitor));
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        return expr.GetCost() + (template is null ? 0 : template.GetCost());
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount()
    {
        return template is null ? 1 : 2;
    }

    /// <inheritdoc/>
    public override Expression? GetSubexpression(int index)
    {
        if (index == 0)
        {
            return expr;
        }

        return index == 1 && template is not null
            ? template
            : throw new System.IndexOutOfRangeException();
    }

    /// <inheritdoc/>
    public override TypeInfo? GetTypeIfStaticallyKnown(SessionLocal session) => castToType;
}
