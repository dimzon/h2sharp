/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.Procedure.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A prepared statement stored in the session under a name: PREPARE puts it there,
 * EXECUTE runs it, DEALLOCATE removes it.
 */
using H2Sharp.Command;

namespace H2Sharp.Engine;

/// <summary>
/// Represents a procedure. Procedures are implemented for PostgreSQL compatibility.
/// </summary>
public sealed class Procedure
{
    private readonly string name;

    private readonly Prepared prepared;

    /// <summary>
    /// Creates the procedure.
    /// </summary>
    /// <param name="name">the name EXECUTE and DEALLOCATE use</param>
    /// <param name="prepared">the statement PREPARE stored</param>
    public Procedure(string name, Prepared prepared)
    {
        this.name = name;
        this.prepared = prepared;
    }

    /// <summary>
    /// Gets the name of this procedure.
    /// </summary>
    /// <returns>the name</returns>
    public string GetName() => name;

    /// <summary>
    /// Gets the statement this procedure runs.
    /// </summary>
    /// <returns>the prepared statement</returns>
    public Prepared GetPrepared() => prepared;
}
