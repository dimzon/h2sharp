/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.Plan.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * One possible order to read the tables in, and what it costs.
 *
 * The time a query takes depends on the order its tables are read: joining a small table to a
 * big one is cheap, and the other way round is not, although both read the same rows. A plan is
 * one order plus the price of reading in it, and the optimizer's whole job is making several
 * and keeping the cheapest.
 *
 * The constructor walks every filter of the order through the visitor, collecting the filters
 * themselves and every join condition it meets. That walk is why a plan knows about tables the
 * query never named in FROM: a join condition names columns of both sides, and the plan has to
 * price the side it would otherwise never see.
 *
 * Costing prices each filter with the filters before it already marked evaluatable, because a
 * table read after three others is read once per row of those three — and a cost computed
 * without that would price every table as if it were read once. The join condition is asked
 * whether it is answerable yet for the same reason: a plan whose join cannot be evaluated is
 * not a slow plan, it is no plan, and its cost is infinity rather than a large number.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;

namespace H2Sharp.Tables;

/// <summary>
/// A possible query execution plan. The time required to execute a query depends on the order
/// the tables are accessed.
/// </summary>
public sealed class Plan
{
    private readonly TableFilter[] filters;

    private readonly Dictionary<TableFilter, PlanItem> planItems = [];

    private readonly Expression[] allConditions;

    private readonly TableFilter[] allFilters;

    /// <summary>
    /// Create a query plan with the given order.
    /// </summary>
    /// <param name="filters">the tables of the query</param>
    /// <param name="count">the number of table items</param>
    /// <param name="condition">the condition in the WHERE clause</param>
    public Plan(TableFilter[] filters, int count, Expression? condition)
    {
        ArgumentNullException.ThrowIfNull(filters);
        this.filters = new TableFilter[count];
        Array.Copy(filters, this.filters, count);
        List<Expression> allCond = [];
        List<TableFilter> all = [];
        if (condition is not null)
        {
            allCond.Add(condition);
        }

        for (int i = 0; i < count; i++)
        {
            TableFilter f = filters[i];
            f.Visit(new PlanCollector(all, allCond));
        }

        allConditions = [.. allCond];
        allFilters = [.. all];
    }

    /// <summary>
    /// Collects a filter and the join conditions it meets.
    /// </summary>
    private sealed class PlanCollector : TableFilter.TableFilterVisitor
    {
        private readonly List<TableFilter> all;

        private readonly List<Expression> allCond;

        internal PlanCollector(List<TableFilter> all, List<Expression> allCond)
        {
            this.all = all;
            this.allCond = allCond;
        }

        public void Accept(TableFilter f)
        {
            ArgumentNullException.ThrowIfNull(f);
            all.Add(f);
            if (f.GetJoinCondition() is not null)
            {
                allCond.Add(f.GetJoinCondition()!);
            }
        }
    }

    /// <summary>
    /// Get the plan item for the given table.
    /// </summary>
    /// <param name="filter">the table</param>
    /// <returns>the plan item</returns>
    public PlanItem? GetItem(TableFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return planItems.TryGetValue(filter, out PlanItem? item) ? item : null;
    }

    /// <summary>
    /// The list of tables.
    /// </summary>
    /// <returns>the array of tables</returns>
    public TableFilter[] GetFilters() => filters;

    /// <summary>
    /// Remove all index conditions that can not be used.
    /// </summary>
    public void RemoveUnusableIndexConditions()
    {
        for (int i = 0; i < allFilters.Length; i++)
        {
            TableFilter f = allFilters[i];
            f.SetEvaluatable(f, true);
            if (i < allFilters.Length - 1)
            {
                // the last table doesn't need the optimization,
                // otherwise the expression is calculated twice unnecessarily
                // (not that bad but not optimal)
                f.OptimizeFullCondition();
            }

            f.RemoveUnusableIndexConditions();
        }

        foreach (TableFilter f in allFilters)
        {
            f.SetEvaluatable(f, false);
        }
    }

    /// <summary>
    /// Calculate the cost of this query plan.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="allColumnsSet">calculates all columns on-demand</param>
    /// <param name="isSelectCommand">whether the caller is a select</param>
    /// <returns>the cost</returns>
    public double CalculateCost(
        SessionLocal session,
        Command.Query.AllColumnsForPlan allColumnsSet,
        bool isSelectCommand)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(allColumnsSet);
        double cost = 1;
        bool invalidPlan = false;
        for (int i = 0; i < allFilters.Length; i++)
        {
            TableFilter tableFilter = allFilters[i];
            PlanItem item = tableFilter.GetBestPlanItem(session, allFilters, i, allColumnsSet, isSelectCommand);
            planItems[tableFilter] = item;
            cost += cost * item.Cost;
            SetEvaluatable(tableFilter, true);
            Expression? on = tableFilter.GetJoinCondition();
            if (on is not null)
            {
                if (!on.IsEverything(ExpressionVisitor.EvaluableVisitor))
                {
                    invalidPlan = true;
                    break;
                }
            }
        }

        if (invalidPlan)
        {
            cost = double.PositiveInfinity;
        }

        foreach (TableFilter f in allFilters)
        {
            SetEvaluatable(f, false);
        }

        return cost;
    }

    /// <summary>
    /// Marks a filter and every condition of the plan as evaluatable or not.
    /// </summary>
    /// <param name="filter">the filter</param>
    /// <param name="b">whether they are evaluatable</param>
    private void SetEvaluatable(TableFilter filter, bool b)
    {
        ArgumentNullException.ThrowIfNull(filter);
        filter.SetEvaluatable(filter, b);
        foreach (Expression e in allConditions)
        {
            e.SetEvaluatable(filter, b);
        }
    }
}
