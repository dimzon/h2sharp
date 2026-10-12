/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.query.AllColumnsForPlan.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Every column every filter of a query reads, grouped by table.
 *
 * It is expensive to compute for a large query, so it is computed on demand and kept: the
 * planner asks per table while it prices indexes, and walking the whole query for every table
 * would make planning cost more than the scan it prices. The map is filled once, by a visitor
 * the expressions accept, which is why add() is public — it is the visitor that calls it, not
 * the planner.
 *
 * Get() refuses: filling the map needs the visitor's walk over the filters, which is not
 * ported. Add() is real, because it is only map maintenance and it is what the walk will call.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Tables;

namespace H2Sharp.Command.Query;

/// <summary>
/// This information is expensive to compute for large queries, so do so on-demand. Also store
/// the information pre-mapped by table to avoid expensive traversal.
/// </summary>
public sealed class AllColumnsForPlan
{
    private readonly Tables.TableFilter[] filters;

    private Dictionary<Tables.Table, List<Column>>? map;

    /// <summary>
    /// Creates the holder. Nothing is computed until <see cref="Get"/> is asked.
    /// </summary>
    /// <param name="filters">the filters of the query</param>
    public AllColumnsForPlan(Tables.TableFilter[] filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        this.filters = filters;
    }

    /// <summary>
    /// Called by ExpressionVisitor.
    /// </summary>
    /// <param name="newCol">new column to be added</param>
    public void Add(Column newCol)
    {
        ArgumentNullException.ThrowIfNull(newCol);
        Dictionary<Tables.Table, List<Column>> m = map ?? throw new TODOException(
            "AllColumnsForPlan.add: the map is filled by the visitor's walk, which is not ported");
        Tables.Table? table = newCol.GetTable();
        if (table is null)
        {
            return;
        }

        if (!m.TryGetValue(table, out List<Column>? cols))
        {
            cols = [];
            m[table] = cols;
        }

        if (!cols.Contains(newCol))
        {
            cols.Add(newCol);
        }
    }

    /// <summary>
    /// Used by index to calculate the cost of a scan.
    /// </summary>
    /// <param name="table">the table</param>
    /// <returns>all table's referenced columns</returns>
    public List<Column>? Get(Tables.Table table)
    {
        // The walk over the filters fills the map; until it is ported there is nothing to
        // answer with. The two reads below keep the reference's fields -- the filters to walk
        // and the map to fill -- rather than dropping state the walk will need.
        _ = filters;
        map ??= new();
        throw new TODOException("AllColumnsForPlan.get: the visitor's walk is not ported");
    }
}
