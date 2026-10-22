/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.condition.PredicateWithSubquery.
 * Copyright 2026 H2Sharp contributors.
 */
using System.Text;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Util;

namespace H2Sharp.Expressions.Conditions;

/// <summary>Base class for predicates with a subquery.</summary>
/// <remarks>
/// Public because C# requires the base of a public class to be public; Java keeps this
/// class package-private. The query is protected in place of Java's package visibility.
/// </remarks>
public abstract class PredicateWithSubquery : Condition
{
    /// <summary>The subquery.</summary>
    protected readonly Query query;

    /// <summary>Creates a predicate over an initialized subquery.</summary>
    protected PredicateWithSubquery(Query query) => this.query = query;

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
        => query.MapColumns(resolver, level + 1, true);

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        query.Prepare();
        return this;
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
        => query.SetEvaluatable(tableFilter, value);

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => StringUtils.Indent(builder.Append('('), query.GetPlanSql(sqlFlags), 4, false).Append(')');

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
        => query.UpdateAggregate(session, stage);

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor) => query.IsEverything(visitor);

    /// <inheritdoc/>
    public override int GetCost() => query.GetCostAsExpression();
}
