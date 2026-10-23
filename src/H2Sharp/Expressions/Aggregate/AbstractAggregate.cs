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
using H2Sharp.Expressions.Analysis;

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
    {
        WindowFrame? frame = over!.GetWindowFrame();
        bool grouped = frame is null || frame.GetUnits() != WindowFrameUnits.Rows && frame.GetExclusion().IsGroupOrNoOthers();
        if (frame is null) { AggregateFastPartition(session, result, ordered, rowIdColumn, grouped); return; }
        bool variable = frame.IsVariableBounds() && CheckVariableBounds(frame, ordered);
        if (variable) grouped = false;
        else if (frame.GetExclusion() == WindowFrameExclusion.ExcludeNoOthers)
        {
            bool unboundedFollowing = frame.GetFollowing()?.GetBoundType() == WindowFrameBoundType.UnboundedFollowing;
            if (frame.GetStarting().GetBoundType() == WindowFrameBoundType.UnboundedPreceding)
            {
                if (unboundedFollowing) AggregateWholePartition(session, result, ordered, rowIdColumn);
                else AggregateFastPartition(session, result, ordered, rowIdColumn, grouped);
                return;
            }
            if (unboundedFollowing) { AggregateFastPartitionInReverse(session, result, ordered, rowIdColumn, grouped); return; }
        }
        for (int i = 0; i < ordered.Count;)
        {
            object data = CreateAggregateData();
            foreach (Value?[] row in WindowFrame.Iterator(over, session, ordered, GetOverOrderBySort(), i, false)) UpdateFromExpressions(session, data, row);
            i = ProcessGroup(result, GetAggregatedValue(session, data), ordered, rowIdColumn, i, ordered.Count, grouped);
        }
    }
    private static bool CheckVariableBounds(WindowFrame frame, List<Value?[]> ordered)
    {
        WindowFrameBound bound = frame.GetStarting();
        if (bound.IsVariable())
        {
            int offset = bound.GetExpressionIndex();
            for (int i = 1; i < ordered.Count; i++) if (!ordered[0][offset]!.Equals(ordered[i][offset])) return true;
        }
        bound = frame.GetFollowing()!;
        if (bound is not null && bound.IsVariable())
        {
            int offset = bound.GetExpressionIndex();
            for (int i = 1; i < ordered.Count; i++) if (!ordered[0][offset]!.Equals(ordered[i][offset])) return true;
        }
        return false;
    }
    private void AggregateFastPartition(SessionLocal session, Dictionary<int, Value> result, List<Value?[]> ordered, int rowIdColumn, bool grouped)
    {
        object data = CreateAggregateData();
        int lastIncluded = -1;
        Value? value = null;
        for (int i = 0; i < ordered.Count;)
        {
            int newLast = WindowFrame.GetEndIndex(over!, session, ordered, GetOverOrderBySort(), i);
            if (newLast > lastIncluded)
            {
                for (int j = lastIncluded + 1; j <= newLast; j++) UpdateFromExpressions(session, data, ordered[j]);
                lastIncluded = newLast;
                value = GetAggregatedValue(session, data);
            }
            value ??= GetAggregatedValue(session, data);
            i = ProcessGroup(result, value, ordered, rowIdColumn, i, ordered.Count, grouped);
        }
    }
    private void AggregateFastPartitionInReverse(SessionLocal session, Dictionary<int, Value> result, List<Value?[]> ordered, int rowIdColumn, bool grouped)
    {
        object data = CreateAggregateData();
        int firstIncluded = ordered.Count;
        Value? value = null;
        for (int i = firstIncluded - 1; i >= 0;)
        {
            int newLast = over!.GetWindowFrame()!.GetStartIndex(session, ordered, GetOverOrderBySort(), i);
            if (newLast < firstIncluded)
            {
                for (int j = firstIncluded - 1; j >= newLast; j--) UpdateFromExpressions(session, data, ordered[j]);
                firstIncluded = newLast;
                value = GetAggregatedValue(session, data);
            }
            value ??= GetAggregatedValue(session, data);
            Value?[] last = ordered[i], current = last;
            do { result[current[rowIdColumn]!.GetInt()] = value; }
            while (--i >= 0 && grouped && overOrderBySort!.Compare(last, current = ordered[i]) == 0);
        }
    }
    private int ProcessGroup(Dictionary<int, Value> result, Value value, List<Value?[]> ordered, int rowIdColumn, int i, int size, bool grouped)
    {
        Value?[] first = ordered[i], current = first;
        do { result[current[rowIdColumn]!.GetInt()] = value; }
        while (++i < size && grouped && overOrderBySort!.Compare(first, current = ordered[i]) == 0);
        return i;
    }
    private void AggregateWholePartition(SessionLocal session, Dictionary<int, Value> result, List<Value?[]> ordered, int rowIdColumn)
    {
        object data = CreateAggregateData();
        foreach (Value?[] row in ordered) UpdateFromExpressions(session, data, row);
        Value value = GetAggregatedValue(session, data);
        foreach (Value?[] row in ordered) result[row[rowIdColumn]!.GetInt()] = value;
    }

    /// <inheritdoc/>
    protected override void UpdateAggregate(SessionLocal session, SelectGroups groupData, int groupRowId)
    {
        if (filterCondition is null || filterCondition.GetBooleanValue(session))
        {
            if (over is not null)
            {
                if (over.IsOrdered()) UpdateOrderedAggregate(session, groupData, groupRowId, over.GetOrderBy());
                else UpdateAggregate(session, GetWindowData(session, groupData, false));
            }
            else UpdateAggregate(session, GetGroupData(groupData, false)!);
        }
        else if (over is not null && over.IsOrdered()) UpdateOrderedAggregate(session, groupData, groupRowId, over.GetOrderBy());
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
