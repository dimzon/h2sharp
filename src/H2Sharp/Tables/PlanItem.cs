/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.PlanItem.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The plan item describes the index to be used, and the estimated cost when using it.
 *
 * It is one index and what the planner would rather do instead, because the cost of reading a
 * table is not only the reading: joining two of them means reading the second by looking up one
 * row at a time in the first, and that is a different plan from reading the second whole. Those
 * two "rather" plans are what joinPlan and nestedJoinPlan hold, and a planner that dropped them
 * would produce a plan that answers correctly and runs in the wrong order.
 *
 * The masks are the reason this is not a plain index name. A mask says which of the index's
 * columns the query has already fixed to constants — a comparison on the indexed column is worth
 * a scan, and a comparison on a later one is worth nothing — and it is the mask, not the index,
 * that decides whether the index can be used at all.
 *
 * The accessors are internal because in the reference they are package-private: only the planner
 * and the filter it hands the plan to are meant to see them. C# has no package privacy, so
 * "internal" is the closest thing to the boundary the reference draws.
 */
using H2Sharp.Indexes;

namespace H2Sharp.Tables;

/// <summary>
/// The plan item describes the index to be used, and the estimated cost when using it.
/// </summary>
public sealed class PlanItem
{
    /// <summary>
    /// The cost.
    /// </summary>
    internal double Cost;

    private int[]? masks;

    private Index? index;

    private PlanItem? joinPlan;

    private PlanItem? nestedJoinPlan;

    /// <summary>
    /// Sets the masks of the columns the query has fixed.
    /// </summary>
    /// <param name="masks">the masks</param>
    internal void SetMasks(int[]? masks) => this.masks = masks;

    /// <summary>
    /// Gets the masks of the columns the query has fixed.
    /// </summary>
    /// <returns>the masks, or null</returns>
    internal int[]? GetMasks() => masks;

    /// <summary>
    /// Sets the index this item is to read.
    /// </summary>
    /// <param name="index">the index</param>
    internal void SetIndex(Index? index) => this.index = index;

    /// <summary>
    /// Gets the index this item is to read.
    /// </summary>
    /// <returns>the index, or null</returns>
    public Index? GetIndex() => index;

    /// <summary>
    /// Gets the plan the planner would rather use for a join.
    /// </summary>
    /// <returns>the join plan, or null</returns>
    internal PlanItem? GetJoinPlan() => joinPlan;

    /// <summary>
    /// Gets the plan the planner would rather use for a nested join.
    /// </summary>
    /// <returns>the nested join plan, or null</returns>
    internal PlanItem? GetNestedJoinPlan() => nestedJoinPlan;

    /// <summary>
    /// Sets the plan the planner would rather use for a join.
    /// </summary>
    /// <param name="joinPlan">the join plan</param>
    internal void SetJoinPlan(PlanItem? joinPlan) => this.joinPlan = joinPlan;

    /// <summary>
    /// Sets the plan the planner would rather use for a nested join.
    /// </summary>
    /// <param name="nestedJoinPlan">the nested join plan</param>
    internal void SetNestedJoinPlan(PlanItem? plan) => nestedJoinPlan = plan;
}
