/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.index.SpatialIndex.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A spatial index. Spatial indexes are used to speed up searching spatial/geometric data.
 *
 * It is an interface and not a class because there is exactly one question a spatial index
 * answers that no other index does — which rows intersect a geometry — and everything else
 * about it is an index like any other. A class here would promise storage for geometries,
 * and there is none to promise yet.
 */
using H2Sharp.Engine;
using H2Sharp.Result;

namespace H2Sharp.Indexes;

/// <summary>
/// A spatial index. Spatial indexes are used to speed up searching spatial/geometric data.
/// </summary>
public interface SpatialIndex
{
    /// <summary>
    /// Find a row or a list of rows and create a cursor to iterate over the result.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="first">the lower bound</param>
    /// <param name="last">the upper bound</param>
    /// <param name="reverse">if true, iterate in reverse (descending) order</param>
    /// <param name="intersection">the geometry which values should intersect with, or null for anything</param>
    /// <returns>the cursor to iterate over the results</returns>
    Cursor? FindByGeometry(SessionLocal session, SearchRow? first, SearchRow? last, bool reverse, SearchRow? intersection);
}
