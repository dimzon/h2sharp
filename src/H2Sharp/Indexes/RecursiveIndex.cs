/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.index.RecursiveIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;

namespace H2Sharp.Indexes;

/// <summary>A recursive index.</summary>
public sealed class RecursiveIndex : QueryExpressionIndex
{
    private readonly CTE cte;

    /// <summary>Creates a recursive index.</summary>
    /// <param name="table">the query expression table</param>
    /// <param name="query">the recursive query</param>
    public RecursiveIndex(CTE table, Query query)
        : base(table)
    {
        cte = table;
        this.query = query;
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
        => 1000d;

    /// <inheritdoc/>
    public override Cursor? GetCursor(SessionLocal session, SearchRow? first, SearchRow? last, bool reverse)
    {
        ResultInterface? recursiveResult = cte.GetRecursiveResult();
        if (recursiveResult is not null)
        {
            recursiveResult.Reset();
            return new QueryExpressionCursor(this, recursiveResult, first, last);
        }

        if (!query.IsUnion())
        {
            throw DbException.Get(ErrorCode.SyntaxError2, "recursive queries without UNION");
        }

        SelectUnion union = (SelectUnion)query;
        Query left = union.GetLeft();
        left.SetNeverLazy(true);
        ResultInterface leftResult = left.Run(0);
        LocalResult result = union.GetEmptyResult();
        result.SetMaxMemoryRows(int.MaxValue);
        while (leftResult.Next())
        {
            result.AddRow(leftResult.CurrentRow());
        }

        Query right = union.GetRight();
        right.SetNeverLazy(true);
        leftResult.Reset();
        cte.SetRecursiveResult(leftResult);
        while (true)
        {
            ResultInterface rightResult = right.Run(0);
            if (!rightResult.HasNext())
            {
                rightResult.Close();
                break;
            }

            while (rightResult.Next())
            {
                result.AddRow(rightResult.CurrentRow());
            }

            rightResult.Reset();
            cte.SetRecursiveResult(rightResult);
        }

        cte.SetRecursiveResult(null);
        result.Done();
        return new QueryExpressionCursor(this, result, first, last);
    }
}
