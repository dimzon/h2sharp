/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.query.Optimizer.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The optimizer finds the best order to read the tables in.
 *
 * Seven filters or fewer are tried every way there is — 5040 orders for seven tables — and past
 * that the first tables are tried every way while the rest are picked greedy, one cheapest next
 * at a time. Past brute force entirely, chance takes over: shuffle, measure, keep the better,
 * and remember which pairs were already swapped so that the same shuffle is never measured
 * twice. That ladder — exhaustive, then greedy, then genetic — is not three optimizers but one
 * budget: measuring a plan costs a fraction of running it, and the optimizer spends that
 * fraction until spending more would cost more than the worst plan it could still find.
 *
 * canStop is where the budget lives. Every 128th plan it asks whether the time spent measuring
 * has passed a hundred microseconds times the best cost seen: a cheap plan found early stops
 * the search, because measuring longer than the plan would run is measuring for its own sake.
 * Simple queries never reach it — one table has one order, and there is nothing to choose.
 *
 * The class is internal, as it is package-private in the reference: it is a step of preparing
 * a query, not API, and no caller outside the command layer has a plan to ask it for.
 */
using System;
using System.Collections;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Tables;
using H2Sharp.Util;

namespace H2Sharp.Command.Query;

/// <summary>
/// The optimizer is responsible to find the best execution plan for a given query.
/// </summary>
internal sealed class Optimizer
{
    private const int MaxBruteForceFilters = 7;

    private const int MaxBruteForce = 2000;

    private const int MaxGenetic = 500;

    private long startNs;

    private BitArray? switched;

    //  possible plans for filters, if using brute force:
    //  1 filter 1 plan
    //  2 filters 2 plans
    //  3 filters 6 plans
    //  4 filters 24 plans
    //  5 filters 120 plans
    //  6 filters 720 plans
    //  7 filters 5040 plans
    //  8 filters 40320 plan
    //  9 filters 362880 plans
    // 10 filters 3628800 filters
    private readonly TableFilter[] filters;

    private readonly Expression? condition;

    private readonly SessionLocal session;

    private Plan? bestPlan;

    private TableFilter? topFilter;

    private double cost;

    private Random? random;

    private readonly AllColumnsForPlan allColumnsSet;

    internal Optimizer(TableFilter[] filters, Expression? condition, SessionLocal session)
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(session);
        this.filters = filters;
        this.condition = condition;
        this.session = session;
        allColumnsSet = new AllColumnsForPlan(filters);
    }

    /// <summary>
    /// How many filters to calculate using brute force. The remaining filters are selected
    /// using a greedy algorithm which has a runtime of (1 + 2 + ... + n) = (n * (n-1) / 2) for
    /// n filters. The brute force algorithm has a runtime of n * (n-1) * ... * (n-m) when
    /// calculating m brute force of n total. The combined runtime is (brute force) * (greedy).
    /// </summary>
    /// <param name="filterCount">the number of filters total</param>
    /// <returns>the number of filters to calculate using brute force</returns>
    private static int GetMaxBruteForceFilters(int filterCount)
    {
        int i = 0;
        int j = filterCount;
        int total = filterCount;
        while (j > 0 && total * (j * (j - 1) / 2) < MaxBruteForce)
        {
            j--;
            total *= j;
            i++;
        }

        return i;
    }

    private void CalculateBestPlan(bool isSelectCommand)
    {
        cost = -1;
        if (filters.Length == 1)
        {
            TestPlan(filters, isSelectCommand);
        }
        else
        {
            startNs = Utils.CurrentNanoTime();
            if (filters.Length <= MaxBruteForceFilters)
            {
                CalculateBruteForceAll(isSelectCommand);
            }
            else
            {
                CalculateBruteForceSome(isSelectCommand);
                random = new Random(0);
                CalculateGenetic(isSelectCommand);
            }
        }
    }

    private void CalculateFakePlan()
    {
        cost = -1;
        bestPlan = new Plan(filters, filters.Length, condition);
    }

    private bool CanStop(int x)
    {
        return (x & 127) == 0
            // don't calculate for simple queries (no rows or so)
            && cost >= 0
            // 100 microseconds * cost
            && Utils.CurrentNanoTime() - startNs > cost * 100_000L;
    }

    private void CalculateBruteForceAll(bool isSelectCommand)
    {
        TableFilter[] list = new TableFilter[filters.Length];
        Permutations<TableFilter> p = Permutations<TableFilter>.Create(filters, list);
        for (int x = 0; !CanStop(x) && p.Next(); x++)
        {
            TestPlan(list, isSelectCommand);
        }
    }

    private void CalculateBruteForceSome(bool isSelectCommand)
    {
        int bruteForce = GetMaxBruteForceFilters(filters.Length);
        TableFilter[] list = new TableFilter[filters.Length];
        Permutations<TableFilter> p = Permutations<TableFilter>.Create(filters, list, bruteForce);
        for (int x = 0; !CanStop(x) && p.Next(); x++)
        {
            // find out what filters are not used yet
            foreach (TableFilter f in filters)
            {
                f.SetUsed(false);
            }

            for (int i = 0; i < bruteForce; i++)
            {
                list[i].SetUsed(true);
            }

            // fill the remaining elements with the unused elements (greedy)
            for (int i = bruteForce; i < filters.Length; i++)
            {
                double costPart = -1.0;
                int bestPart = -1;
                for (int j = 0; j < filters.Length; j++)
                {
                    if (!filters[j].IsUsed())
                    {
                        if (i == filters.Length - 1)
                        {
                            bestPart = j;
                            break;
                        }

                        list[i] = filters[j];
                        Plan part = new(list, i + 1, condition);
                        double costNow = part.CalculateCost(session, allColumnsSet, isSelectCommand);
                        if (costPart < 0 || costNow < costPart)
                        {
                            costPart = costNow;
                            bestPart = j;
                        }
                    }
                }

                filters[bestPart].SetUsed(true);
                list[i] = filters[bestPart];
            }

            TestPlan(list, isSelectCommand);
        }
    }

    private void CalculateGenetic(bool isSelectCommand)
    {
        TableFilter[] best = new TableFilter[filters.Length];
        TableFilter[] list = new TableFilter[filters.Length];
        for (int x = 0; x < MaxGenetic; x++)
        {
            if (CanStop(x))
            {
                break;
            }

            bool generateRandom = (x & 127) == 0;
            if (!generateRandom)
            {
                Array.Copy(best, list, filters.Length);
                if (!ShuffleTwo(list))
                {
                    generateRandom = true;
                }
            }

            if (generateRandom)
            {
                switched = new BitArray(filters.Length * filters.Length);
                Array.Copy(filters, best, filters.Length);
                ShuffleAll(best);
                Array.Copy(best, list, filters.Length);
            }

            if (TestPlan(list, isSelectCommand))
            {
                switched = new BitArray(filters.Length * filters.Length);
                Array.Copy(list, best, filters.Length);
            }
        }
    }

    private bool TestPlan(TableFilter[] list, bool isSelectCommand)
    {
        Plan p = new(list, list.Length, condition);
        double costNow = p.CalculateCost(session, allColumnsSet, isSelectCommand);
        if (cost < 0 || costNow < cost)
        {
            cost = costNow;
            bestPlan = p;
            return true;
        }

        return false;
    }

    private void ShuffleAll(TableFilter[] f)
    {
        ArgumentNullException.ThrowIfNull(random);
        for (int i = 0; i < f.Length - 1; i++)
        {
            int j = i + random.Next(f.Length - i);
            if (j != i)
            {
                TableFilter temp = f[i];
                f[i] = f[j];
                f[j] = temp;
            }
        }
    }

    private bool ShuffleTwo(TableFilter[] f)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(switched);
        int a = 0;
        int b = 0;
        int i = 0;
        for (; i < 20; i++)
        {
            a = random.Next(f.Length);
            b = random.Next(f.Length);
            if (a == b)
            {
                continue;
            }

            if (a < b)
            {
                int temp = a;
                a = b;
                b = temp;
            }

            int s = (a * f.Length) + b;
            if (switched.Get(s))
            {
                continue;
            }

            switched.Set(s, true);
            break;
        }

        if (i == 20)
        {
            return false;
        }

        TableFilter temp2 = f[a];
        f[a] = f[b];
        f[b] = temp2;
        return true;
    }

    /// <summary>
    /// Calculate the best query plan to use.
    /// </summary>
    /// <param name="parse">If we do not need to really get the best plan because it is a view parsing stage.</param>
    /// <param name="isSelectCommand">whether the caller is a select</param>
    internal void Optimize(bool parse, bool isSelectCommand)
    {
        if (parse)
        {
            CalculateFakePlan();
        }
        else
        {
            CalculateBestPlan(isSelectCommand);
            bestPlan!.RemoveUnusableIndexConditions();
        }

        TableFilter[] f2 = bestPlan!.GetFilters();
        topFilter = f2[0];
        for (int i = 0; i < f2.Length - 1; i++)
        {
            f2[i].AddJoin(f2[i + 1], outer: false, null);
        }

        if (parse)
        {
            return;
        }

        foreach (TableFilter f in f2)
        {
            PlanItem? item = bestPlan.GetItem(f);
            f.SetPlanItem(item);
        }
    }

    /// <summary>
    /// Gets the filter the whole plan hangs off.
    /// </summary>
    /// <returns>the top filter</returns>
    public TableFilter? GetTopFilter() => topFilter;

    /// <summary>
    /// Gets the cost of the best plan.
    /// </summary>
    /// <returns>the cost</returns>
    internal double GetCost() => cost;
}
