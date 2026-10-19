/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.aggregate.AbstractAggregate.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sliced the same way as its base: the argument mapping, optimization and group
 * routing are real, the ordered window loop refuses until OVER is read.
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// A base class for aggregate functions.
/// </summary>
public abstract class AbstractAggregate : Analysis.DataAnalysisOperation
{
    /// <summary>
    /// is this a DISTINCT aggregate
    /// </summary>
    protected readonly bool distinct;

    /// <summary>
    /// The arguments.
    /// </summary>
    protected readonly Expression[] args;

    /// <summary>
    /// FILTER condition for aggregate
    /// </summary>
    protected Expression? filterCondition;

    /// <summary>
    /// The type of the result.
    /// </summary>
    protected TypeInfo? type;

    /// <summary>
    /// Base constructor: the select, the aggregated expressions and the DISTINCT flag.
    /// </summary>
    /// <param name="select">the select statement</param>
    /// <param name="args">the aggregated expressions</param>
    /// <param name="distinct">if distinct is used</param>
    protected AbstractAggregate(Select select, Expression[] args, bool distinct)
        : base(select)
    {
        this.args = args;
        this.distinct = distinct;
    }

    /// <inheritdoc/>
    public override bool IsAggregate() => true;

    /// <summary>
    /// Returns the FILTER condition.
    /// </summary>
    /// <returns>the FILTER Condition</returns>
    public Expression? GetFilterCondition() => filterCondition;

    /// <summary>
    /// Sets the FILTER condition.
    /// </summary>
    /// <param name="filterCondition">FILTER condition, or null</param>
    public void SetFilterCondition(Expression? filterCondition) => this.filterCondition = filterCondition;

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => type!;

    /// <inheritdoc/>
    protected override void MapColumnsAnalysis(ColumnResolver resolver, int level, int innerState)
    {
        foreach (Expression arg in args)
        {
            arg.MapColumns(resolver, level, innerState);
        }

        filterCondition?.MapColumns(resolver, level, innerState);
        base.MapColumnsAnalysis(resolver, level, innerState);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        for (int i = 0; i < args.Length; i++)
        {
            args[i] = args[i].Optimize(session);
        }

        if (filterCondition is not null)
        {
            filterCondition = filterCondition.OptimizeCondition(session)!;
        }

        return base.Optimize(session);
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        foreach (Expression arg in args)
        {
            arg.SetEvaluatable(tableFilter, value);
        }

        filterCondition?.SetEvaluatable(tableFilter, value);
        base.SetEvaluatable(tableFilter, value);
    }

    /// <inheritdoc/>
    protected override void GetOrderedResultLoop(
        SessionLocal session, Dictionary<int, Value> result, List<Value?[]> ordered, int rowIdColumn)
        => throw new TODOException("AbstractAggregate.getOrderedResultLoop: OVER clause is not ported");

    /// <inheritdoc/>
    protected override void UpdateAggregate(SessionLocal session, SelectGroups groupData, int groupRowId)
    {
        if (filterCondition is null || filterCondition.GetBooleanValue(session))
        {
            UpdateAggregate(session, GetGroupData(groupData, false)!);
        }
    }

    /// <summary>
    /// Updates an aggregate value.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="aggregateData">the aggregate data</param>
    protected abstract void UpdateAggregate(SessionLocal session, object aggregateData);

    /// <inheritdoc/>
    protected override void UpdateGroupAggregates(SessionLocal session, int stage)
    {
        filterCondition?.UpdateAggregate(session, stage);
        base.UpdateGroupAggregates(session, stage);
    }

    /// <inheritdoc/>
    protected override StringBuilder AppendTailConditions(StringBuilder builder, int sqlFlags, bool forceOrderBy)
    {
        if (filterCondition is not null)
        {
            _ = builder.Append(" FILTER (WHERE ");
            filterCondition.GetUnenclosedSql(builder, sqlFlags).Append(')');
        }

        return base.AppendTailConditions(builder, sqlFlags, forceOrderBy);
    }

    /// <inheritdoc/>
    public override int GetSubexpressionCount() => args.Length;

    /// <inheritdoc/>
    public override Expression? GetSubexpression(int index) => args[index];

    /// <summary>
    /// Updates the provided aggregate data from the remembered expressions.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="aggregateData">aggregate data</param>
    /// <param name="array">values of expressions</param>
    protected abstract void UpdateFromExpressions(SessionLocal session, object aggregateData, Value?[] array);
}
