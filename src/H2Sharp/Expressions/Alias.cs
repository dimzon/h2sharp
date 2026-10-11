/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.Alias.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A column alias as in SELECT 'Hello' AS NAME ...
 *
 * An alias is not a wrapper that hides anything: it forwards every question to the expression
 * it names and only answers two questions itself — what the column is called, and what it
 * really is. The second one is getNonAliasExpression, and it is why every optimiser that
 * rewrites an expression has to look through aliases: without it, a wrapped column would be
 * compared to a bare one as if they were different columns.
 *
 * aliasColumnName is the flag that says whether this alias also *defines* the column name.
 * SELECT 1 AS A and a view that renames a column both produce aliases; only the first changes
 * what the column is called, and the reference keeps them apart so that a view's column names
 * are not silently rewritten by an alias that was only ever about readability.
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// A column alias as in SELECT 'Hello' AS NAME ...
/// </summary>
public sealed class Alias : Expression
{
    private readonly string alias;

    private Expression expr;

    private readonly bool aliasColumnName;

    /// <summary>
    /// Creates an alias.
    /// </summary>
    /// <param name="expression">the expression to alias</param>
    /// <param name="alias">the alias name</param>
    /// <param name="aliasColumnName">whether this alias defines the column name</param>
    public Alias(Expression expression, string alias, bool aliasColumnName)
    {
        expr = expression;
        this.alias = alias;
        this.aliasColumnName = aliasColumnName;
    }

    /// <inheritdoc/>
    public override Expression GetNonAliasExpression()
    {
        return expr;
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        return expr.GetValue(session);
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo()
    {
        return expr.GetTypeInfo();
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
        return this;
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool b)
    {
        expr.SetEvaluatable(tableFilter, b);
    }

    /// <inheritdoc/>
    public override bool IsIdentity()
    {
        return expr.IsIdentity();
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        _ = expr.GetUnenclosedSql(builder, sqlFlags).Append(" AS ");
        return ParserUtil.QuoteIdentifier(builder, alias, sqlFlags);
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        expr.UpdateAggregate(session, stage);
    }

    /// <inheritdoc/>
    public override string GetAlias(SessionLocal session, int columnIndex)
    {
        return alias;
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

    /// <summary>
    /// Whether this alias also defines the column name.
    /// </summary>
    public bool IsAliasColumnName()
    {
        return aliasColumnName;
    }
}
