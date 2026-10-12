/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.IndexHints.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The hint that says which index to use for a given table.
 *
 * It is a list of allowed index names rather than a demand for one index, and that is deliberate:
 * a hint that named exactly one index would make the query fail when that index does not exist,
 * while a list narrows the planner's choice and lets it fall back. The planner is still the one
 * that decides; this only takes an index away.
 *
 * The set is a linked one because the order the names were written in is the order a caller
 * expects to see in the plan, and a plan printed in a different order than the statement is a
 * plan nobody can match against the statement they wrote.
 *
 * There is one factory rather than a public constructor, so that adding a kind of hint later does
 * not change how every existing one is made.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Indexes;

namespace H2Sharp.Tables;

/// <summary>
/// Contains the hints for which index to use for a specific table.
/// </summary>
public sealed class IndexHints
{
    private readonly List<string> allowedIndexes;

    private IndexHints(List<string> allowedIndexes)
    {
        this.allowedIndexes = allowedIndexes;
    }

    /// <summary>
    /// Create an index hint object.
    /// </summary>
    /// <param name="allowedIndexes">the set of allowed indexes</param>
    /// <returns>the hint object</returns>
    public static IndexHints CreateUseIndexHints(List<string> allowedIndexes)
    {
        ArgumentNullException.ThrowIfNull(allowedIndexes);
        return new IndexHints(allowedIndexes);
    }

    /// <summary>
    /// Gets the names of the indexes that may be used.
    /// </summary>
    /// <returns>the names, in the order they were written</returns>
    public IReadOnlyCollection<string> GetAllowedIndexes() => allowedIndexes;

    /// <inheritdoc/>
    public override string ToString() => "IndexHints{allowedIndexes=" + string.Join(", ", allowedIndexes) + "}";

    /// <summary>
    /// Allow an index to be used.
    /// </summary>
    /// <remarks>
    /// Named rather than taken as an Index, because the hint is a list of *names* and the check
    /// is whether a name is in it. Taking the index and asking for its name would make this a
    /// question about objects that already exist, and the point of a hint is that it is written
    /// before the planner has chosen one.
    /// </remarks>
    /// <param name="indexName">the name of the index</param>
    /// <returns>whether it was allowed</returns>
    public bool AllowIndex(string? indexName)
    {
        return indexName is not null
            && new HashSet<string>(allowedIndexes, StringComparer.OrdinalIgnoreCase).Contains(indexName);
    }
}
