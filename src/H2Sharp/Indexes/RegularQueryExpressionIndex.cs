/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.index.RegularQueryExpressionIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A regular query expression index: the index of a derived table, which runs the query the
 * table stands for.
 */
using System.Collections.Generic;
using H2Sharp.Command;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Indexes;

/// <summary>
/// A regular query expression index.
/// </summary>
public sealed class RegularQueryExpressionIndex : QueryExpressionIndex
{
    private readonly int[]? indexMasks;

    private readonly List<Parameter>? originalParameters;

    /// <summary>
    /// Creates the index of a derived table over the query the statement parsed.
    /// </summary>
    /// <param name="table">the table</param>
    /// <param name="query">the query the rows come from</param>
    public RegularQueryExpressionIndex(QueryExpressionTable table, Query query)
        : base(table)
    {
        this.query = query;
    }

    /// <summary>
    /// Creates the index of a derived table, pushing the index conditions into a fresh query.
    /// </summary>
    /// <param name="table">the table</param>
    /// <param name="querySql">the query SQL</param>
    /// <param name="originalParameters">the original parameters, or null</param>
    /// <param name="session">the session</param>
    /// <param name="masks">the masks of the index columns</param>
    public RegularQueryExpressionIndex(
        QueryExpressionTable table,
        string querySql,
        List<Parameter>? originalParameters,
        SessionLocal session,
        int[]? masks)
        : base(table)
    {
        this.originalParameters = originalParameters;
        indexMasks = masks;
        Query q = session.PrepareQueryExpression(querySql);
        if (masks is not null && q.AllowGlobalConditions())
        {
            q = AddConditions(table, querySql, originalParameters, session, masks, q);
        }

        q.PreparePlan(session, null);
        query = q;
    }

    private Query AddConditions(
        QueryExpressionTable table,
        string querySql,
        List<Parameter>? originalParameters,
        SessionLocal session,
        int[] masks,
        Query q)
    {
        int firstIndexParam = table.GetParameterOffset(originalParameters);
        List<int> paramColumnIndex = [];
        int indexColumnCount = 0;
        for (int i = 0; i < masks.Length; i++)
        {
            int mask = masks[i];
            if (mask == 0)
            {
                continue;
            }

            indexColumnCount++;
            int bitCount = System.Numerics.BitOperations.PopCount((uint)mask);
            for (int j = 0; j < bitCount; j++)
            {
                paramColumnIndex.Add(i);
            }
        }

        List<Column> columnList = new(paramColumnIndex.Count);
        for (int i = 0; i < paramColumnIndex.Count;)
        {
            int idx = paramColumnIndex[i];
            columnList.Add(table.GetColumn(idx));
            int mask = masks[idx];
            if ((mask & IndexCondition.Equality) != 0)
            {
                Parameter param = new(firstIndexParam + i);
                q.AddGlobalCondition(param, idx, Comparison.EqualNullSafe);
                i++;
            }

            if ((mask & IndexCondition.Start) != 0)
            {
                Parameter param = new(firstIndexParam + i);
                q.AddGlobalCondition(param, idx, Comparison.BiggerEqual);
                i++;
            }

            if ((mask & IndexCondition.End) != 0)
            {
                Parameter param = new(firstIndexParam + i);
                q.AddGlobalCondition(param, idx, Comparison.SmallerEqual);
                i++;
            }

            if ((mask & IndexCondition.SpatialIntersects) != 0)
            {
                Parameter param = new(firstIndexParam + i);
                q.AddGlobalCondition(param, idx, Comparison.SpatialIntersects);
                i++;
            }
        }

        columns = [.. columnList];
        indexColumns = new IndexColumn[indexColumnCount];
        columnIds = new int[indexColumnCount];
        for (int type = 0, indexColumnId = 0; type < 2; type++)
        {
            for (int i = 0; i < masks.Length; i++)
            {
                int mask = masks[i];
                if (mask == 0)
                {
                    continue;
                }

                if (type == 0)
                {
                    if ((mask & IndexCondition.Equality) == 0)
                    {
                        // The first columns need to be equality conditions.
                        continue;
                    }
                }
                else if ((mask & IndexCondition.Equality) != 0)
                {
                    // After that only range conditions.
                    continue;
                }

                Column column = table.GetColumn(i);
                indexColumns[indexColumnId] = new IndexColumn(column);
                columnIds[indexColumnId] = column.GetColumnId();
                indexColumnId++;
            }
        }

        string sql = q.GetPlanSql(HasSql.DefaultSqlFlags);
        if (sql != querySql)
        {
            q = session.PrepareQueryExpression(sql);
        }

        return q;
    }

    /// <inheritdoc/>
    public override double GetCost(
        SessionLocal session,
        int[]? masks,
        TableFilter[]? filters,
        int filter,
        SortOrder? sortOrder,
        AllColumnsForPlan? allColumnsSet,
        bool isSelectCommand)
        => (double)query.GetCost();

    /// <inheritdoc/>
    public override Cursor? GetCursor(SessionLocal session, SearchRow? first, SearchRow? last, bool reverse)
    {
        List<Parameter>? paramList = query.GetParameters();
        if (originalParameters is not null)
        {
            foreach (Parameter orig in originalParameters)
            {
                int idx = orig.GetIndex();
                Value value = orig.GetValue(session);
                SetParameter(paramList, idx, value);
            }
        }

        if (indexMasks is not null)
        {
            int len;
            if (first is not null)
            {
                len = first.GetColumnCount();
            }
            else if (last is not null)
            {
                len = last.GetColumnCount();
            }
            else
            {
                len = 0;
            }

            int idx = ((QueryExpressionTable)table!).GetParameterOffset(originalParameters);
            for (int i = 0; i < len; i++)
            {
                int mask = indexMasks[i];
                if ((mask & IndexCondition.Equality) != 0)
                {
                    SetParameter(paramList, idx++, first!.GetValue(i)!);
                }

                if ((mask & IndexCondition.Start) != 0)
                {
                    SetParameter(paramList, idx++, first!.GetValue(i)!);
                }

                if ((mask & IndexCondition.End) != 0)
                {
                    SetParameter(paramList, idx++, last!.GetValue(i)!);
                }

                if ((mask & IndexCondition.SpatialIntersects) != 0)
                {
                    // Spatial bounds are not ported.
                    throw new TODOException(
                        "RegularQueryExpressionIndex.find: spatial index conditions are not ported");
                }
            }
        }

        return new QueryExpressionCursor(this, query.Run(0), first, last);
    }

    private static void SetParameter(List<Parameter>? paramList, int x, Value value)
    {
        if (paramList is null || x >= paramList.Count)
        {
            // The parameter may be optimized away as in
            // select * from (select null as x) where x=1;
            return;
        }

        paramList[x].SetValue(value);
    }
}
