/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.analysis.DataAnalysisOperation.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sliced for the aggregate road first: the select link, the aggregate marker, the
 * map-columns state machine, the group dispatch and the group-data read are real,
 * because COUNT walks through all of them. The window frame machinery (ordered
 * aggregates, partitions, remembered expressions) refuses until OVER is read, and the
 * OVER branch of optimize with it: no OVER clause can reach here yet.
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Analysis;

/// <summary>
/// A base class for data analysis operations such as aggregates and window
/// functions.
/// </summary>
public abstract class DataAnalysisOperation : Expression
{
    /// <summary>
    /// Reset stage. Used to reset internal data to its initial state.
    /// </summary>
    public const int StageReset = 0;

    /// <summary>
    /// Group stage, used for explicit or implicit GROUP BY operation.
    /// </summary>
    public const int StageGroup = 1;

    /// <summary>
    /// Window processing stage.
    /// </summary>
    public const int StageWindow = 2;

    /// <summary>
    /// SELECT
    /// </summary>
    protected readonly Select select;

    /// <summary>
    /// OVER clause
    /// </summary>
    protected Window? over;

    /// <summary>
    /// Sort order for OVER
    /// </summary>
    protected Result.SortOrder? overOrderBySort;
    private int numFrameExpressions;
    private int lastGroupRowId;

    /// <summary>Creates a sort order for remembered expressions.</summary>
    protected static Result.SortOrder CreateOrder(SessionLocal session, List<QueryOrderBy> orderBy, int offset)
    {
        int[] indexes = new int[orderBy.Count], sortTypes = new int[orderBy.Count];
        for (int i = 0; i < orderBy.Count; i++) { indexes[i] = i + offset; sortTypes[i] = orderBy[i].sortType; }
        return new Result.SortOrder(session, indexes, sortTypes, null);
    }

    /// <summary>
    /// Base constructor: every analysis operation belongs to the select that read it.
    /// </summary>
    /// <param name="select">the select statement</param>
    protected DataAnalysisOperation(Select select)
    {
        this.select = select;
    }

    /// <summary>
    /// Returns the OVER condition.
    /// </summary>
    /// <returns>the OVER condition</returns>
    public Window? GetOverCondition() => over;

    /// <summary>
    /// Sets the OVER condition.
    /// </summary>
    /// <param name="over">OVER condition, or null</param>
    public void SetOverCondition(Window? over) => this.over = over;

    /// <summary>
    /// Checks whether this expression is an aggregate function.
    /// </summary>
    /// <returns>true if this is an aggregate function (including aggregates with
    /// OVER clause), false if this is a window function</returns>
    public abstract bool IsAggregate();

    /// <summary>
    /// Returns the sort order for OVER clause.
    /// </summary>
    /// <returns>the sort order for OVER clause</returns>
    protected Result.SortOrder? GetOverOrderBySort() => overOrderBySort;

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        if (over is not null)
        {
            if (state != MapInitial)
            {
                throw Message.DbException.Get(
                    Api.ErrorCode.InvalidUseOfAggregateFunction1, GetTraceSql());
            }

            state = MapInWindow;
        }
        else
        {
            if (state == MapInAggregate)
            {
                throw Message.DbException.Get(
                    Api.ErrorCode.InvalidUseOfAggregateFunction1, GetTraceSql());
            }

            state = MapInAggregate;
        }

        MapColumnsAnalysis(resolver, level, state);
    }

    /// <summary>
    /// Map the columns of the resolver to expression columns.
    /// </summary>
    /// <param name="resolver">the column resolver</param>
    /// <param name="level">the subquery nesting level</param>
    /// <param name="innerState">one of the Expression MAP_IN_* values</param>
    protected virtual void MapColumnsAnalysis(ColumnResolver resolver, int level, int innerState)
    {
        if (over is not null)
        {
            over.MapColumns(resolver, level);
        }
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        if (over is not null)
        {
            over.Optimize(session);
            List<QueryOrderBy>? orderBy = over.GetOrderBy();
            if (orderBy is not null) overOrderBySort = CreateOrder(session, orderBy, GetNumExpressions());
            else if (!IsAggregate()) overOrderBySort = new Result.SortOrder(session, new int[GetNumExpressions()]);
            WindowFrame? frame = over.GetWindowFrame();
            if (frame is not null)
            {
                int orderSize = orderBy?.Count ?? 0;
                int index = GetNumExpressions() + orderSize, n = 0;
                WindowFrameBound bound = frame.GetStarting();
                if (bound.IsParameterized())
                {
                    CheckOrderBy(frame.GetUnits(), orderSize);
                    if (bound.IsVariable()) { bound.SetExpressionIndex(index); n++; }
                }
                bound = frame.GetFollowing()!;
                if (bound is not null && bound.IsParameterized())
                {
                    CheckOrderBy(frame.GetUnits(), orderSize);
                    if (bound.IsVariable()) { bound.SetExpressionIndex(index + n); n++; }
                }
                numFrameExpressions = n;
            }
        }

        return this;
    }

    private void CheckOrderBy(WindowFrameUnits units, int size)
    {
        if (units == WindowFrameUnits.Range && size != 1)
        {
            string sql = GetTraceSql();
            throw Message.DbException.GetSyntaxError(sql, sql.Length - 1, "exactly one sort key is required for RANGE units");
        }
        if (units == WindowFrameUnits.Groups && size < 1)
        {
            string sql = GetTraceSql();
            throw Message.DbException.GetSyntaxError(sql, sql.Length - 1, "a sort key is required for GROUPS units");
        }
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        if (over is not null)
        {
            over.SetEvaluatable(tableFilter, value);
        }
    }

    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage)
    {
        if (stage == StageReset)
        {
            UpdateGroupAggregates(session, StageReset);
            lastGroupRowId = 0;
            return;
        }

        bool window = stage == StageWindow;
        if (window != (over is not null))
        {
            if (!window && select.IsWindowQuery()) UpdateGroupAggregates(session, stage);
            return;
        }

        SelectGroups? groupData = select.GetGroupDataIfCurrent(window);
        if (groupData is null)
        {
            // this is a different level (the enclosing query)
            return;
        }

        int rowId = groupData.GetCurrentGroupRowId();
        if (lastGroupRowId == rowId) return;
        lastGroupRowId = rowId;
        if (over is not null && !select.IsGroupQuery()) over.UpdateAggregate(session, stage);
        UpdateAggregate(session, groupData, rowId);
    }

    /// <summary>
    /// Update a row of an aggregate.
    /// </summary>
    /// <param name="session">the database session</param>
    /// <param name="groupData">data for the aggregate group</param>
    /// <param name="groupRowId">row id of group</param>
    protected abstract void UpdateAggregate(SessionLocal session, SelectGroups groupData, int groupRowId);

    /// <summary>
    /// Invoked to update arguments of this aggregate.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="stage">select stage</param>
    protected virtual void UpdateGroupAggregates(SessionLocal session, int stage)
    {
        over?.UpdateAggregate(session, stage);
    }

    /// <summary>Gets or creates partition data.</summary>
    protected object GetWindowData(SessionLocal session, SelectGroups groupData, bool forOrderBy)
    {
        Value? key = over!.GetCurrentKey(session);
        PartitionData? partition = groupData.GetWindowExprData(this, key);
        if (partition is null)
        {
            object data = forOrderBy ? new List<Value?[]>() : CreateAggregateData();
            groupData.SetWindowExprData(this, key, new PartitionData(data));
            return data;
        }
        return partition.GetData()!;
    }

    /// <summary>
    /// Returns the number of expressions, excluding OVER clause.
    /// </summary>
    /// <returns>the number of expressions</returns>
    protected abstract int GetNumExpressions();

    /// <summary>
    /// Stores current values of expressions into the specified array.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="array">array to store values of expressions</param>
    protected abstract void RememberExpressions(SessionLocal session, Value?[] array);

    /// <summary>
    /// Create aggregate data object specific to the subclass.
    /// </summary>
    /// <returns>aggregate-specific data object.</returns>
    protected abstract object CreateAggregateData();

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        if (over is null)
        {
            return true;
        }

        switch (visitor.GetVisitorType())
        {
            case ExpressionVisitor.QueryComparable:
            case ExpressionVisitor.OptimizableAggregate:
            case ExpressionVisitor.Deterministic:
            case ExpressionVisitor.Independent:
            case ExpressionVisitor.DecrementQueryLevel:
                return false;
            default:
                return true;
        }
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        SelectGroups? groupData = select.GetGroupDataIfCurrent(over is not null);
        if (groupData is null)
        {
            throw Message.DbException.Get(Api.ErrorCode.InvalidUseOfAggregateFunction1, GetTraceSql());
        }

        return over is null ? GetAggregatedValue(session, GetGroupData(groupData, true)) : GetWindowResult(session, groupData);
    }

    private Value GetWindowResult(SessionLocal session, SelectGroups groupData)
    {
        bool ordered = over!.IsOrdered();
        Value? key = over.GetCurrentKey(session);
        PartitionData? partition = groupData.GetWindowExprData(this, key);
        object? data;
        if (partition is null)
        {
            data = ordered ? new List<Value?[]>() : CreateAggregateData();
            partition = new PartitionData(data);
            groupData.SetWindowExprData(this, key, partition);
        }
        else data = partition.GetData();
        if (ordered || !IsAggregate()) return GetOrderedResult(session, groupData, partition, data!) ?? GetAggregatedValue(session, null);
        Value? result = partition.GetResult();
        if (result is null) { result = GetAggregatedValue(session, data); partition.SetResult(result); }
        return result;
    }

    /// <summary>
    /// Get the aggregate group data object from the collector object.
    /// </summary>
    /// <param name="groupData">the collector object</param>
    /// <param name="ifExists">if true, return null if object not found, if false, return new
    /// object if nothing found</param>
    /// <returns>group data object</returns>
    protected object? GetGroupData(SelectGroups groupData, bool ifExists)
    {
        object? data = groupData.GetCurrentGroupExprData(this);
        if (data is null && !ifExists)
        {
            data = CreateAggregateData();
            groupData.SetCurrentGroupExprData(this, data);
        }

        return data;
    }

    /// <summary>
    /// Returns aggregated value.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="aggregateData">the aggregate data</param>
    /// <returns>aggregated value.</returns>
    protected abstract Value GetAggregatedValue(SessionLocal session, object? aggregateData);

    /// <summary>
    /// Update a row of an ordered aggregate.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="groupData">data for the aggregate group</param>
    /// <param name="groupRowId">row id of group</param>
    /// <param name="orderBy">list of order by expressions</param>
    protected void UpdateOrderedAggregate(
        SessionLocal session, SelectGroups groupData, int groupRowId, List<QueryOrderBy>? orderBy)
    {
        int ne = GetNumExpressions(), size = orderBy?.Count ?? 0;
        Value?[] array = new Value?[ne + size + numFrameExpressions + 1];
        RememberExpressions(session, array);
        for (int i = 0; i < size; i++) array[ne++] = orderBy![i].expression!.GetValue(session);
        if (numFrameExpressions > 0)
        {
            WindowFrame frame = over!.GetWindowFrame()!;
            WindowFrameBound bound = frame.GetStarting();
            if (bound.IsVariable()) array[ne++] = bound.GetValue()!.GetValue(session);
            bound = frame.GetFollowing()!;
            if (bound is not null && bound.IsVariable()) array[ne++] = bound.GetValue()!.GetValue(session);
        }
        array[ne] = ValueInteger.Get(groupRowId);
        ((List<Value?[]>)GetWindowData(session, groupData, true)).Add(array);
    }

    /// <summary>
    /// Returns result of this window function or window aggregate.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="groupData">data for the aggregate group</param>
    /// <param name="partition">the partition data</param>
    /// <param name="data">the aggregate data</param>
    /// <returns>result of this function</returns>
    protected Value? GetOrderedResult(
        SessionLocal session, SelectGroups groupData, object partition, object data)
    {
        PartitionData partitionData = (PartitionData)partition;
        Dictionary<int, Value>? result = partitionData.GetOrderedResult();
        if (result is null)
        {
            result = [];
            List<Value?[]> rows = (List<Value?[]>)data;
            int rowIdColumn = GetNumExpressions();
            List<QueryOrderBy>? orderBy = over!.GetOrderBy();
            if (orderBy is not null) { rowIdColumn += orderBy.Count; overOrderBySort!.Sort(rows); }
            rowIdColumn += numFrameExpressions;
            GetOrderedResultLoop(session, result, rows, rowIdColumn);
            partitionData.SetOrderedResult(result);
        }
        return result.GetValueOrDefault(groupData.GetCurrentGroupRowId());
    }

    /// <summary>
    /// Returns result of this window function or window aggregate. This method
    /// may not be called on window aggregate without window order clause.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="result">the map to append result to</param>
    /// <param name="ordered">ordered data</param>
    /// <param name="rowIdColumn">the index of row id value</param>
    protected abstract void GetOrderedResultLoop(
        SessionLocal session, Dictionary<int, Value> result, List<Value?[]> ordered, int rowIdColumn);

    /// <summary>
    /// Used to create SQL for the OVER and FILTER clauses.
    /// </summary>
    /// <param name="builder">string builder</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <param name="forceOrderBy">whether synthetic ORDER BY clause should be generated when it
    /// is missing</param>
    /// <returns>the builder object</returns>
    protected virtual StringBuilder AppendTailConditions(StringBuilder builder, int sqlFlags, bool forceOrderBy)
    {
        if (over is not null)
        {
            _ = builder.Append(' ');
            over.GetSql(builder, sqlFlags, forceOrderBy);
        }

        return builder;
    }
}
