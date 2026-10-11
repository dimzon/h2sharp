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
 * A stub, and only the shell is here: CAST is a function in the reference, and the function
 * machinery (type inference across overloads, argument parsing, the catalogue of every function
 * H2 has) is a package of its own that is not ported yet. What this keeps is the shape the rest
 * of the expression tree already depends on — an expression that converts its value to a type —
 * so that "cast a condition to boolean" compiles and refuses, instead of the caller inventing a
 * different way to do it.
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
    private readonly Expression expr;

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
        => throw new TODOException("CastSpecification.getValue: the conversion rules are not ported");

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
        _ = expr.Optimize(session);
        return this;
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool b)
    {
        expr.SetEvaluatable(tableFilter, b);
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        return expr.GetSql(builder, sqlFlags, WithoutParentheses)
            .Append(" ATOMIC")
            .Append(CastType);
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
