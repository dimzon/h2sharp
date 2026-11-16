/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.schema.MetaSchema.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A schema whose tables are generated from the database metadata. They are looked up before
 * the user tables, and they never make the schema empty.
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Tables;

namespace H2Sharp.Schemas;

/// <summary>
/// A metadata schema.
/// </summary>
public abstract class MetaSchema : Schema
{
    /// <summary>
    /// Creates a metadata schema.
    /// </summary>
    /// <param name="database">the database</param>
    /// <param name="id">the object id</param>
    /// <param name="schemaName">the schema name</param>
    protected MetaSchema(Database database, int id, string schemaName)
        : base(database, id, schemaName, system: true)
    {
    }

    /// <summary>
    /// Returns the map of generated tables.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the tables, by name</returns>
    protected abstract Dictionary<string, Table> GetMap(SessionLocal? session);

    /// <inheritdoc/>
    public override Table? FindTableOrView(SessionLocal? session, string name)
        => GetMap(session).GetValueOrDefault(name) ?? base.FindTableOrView(session, name);

    /// <inheritdoc/>
    public override List<Table> GetAllTablesAndViews(SessionLocal session)
    {
        List<Table> userTables = base.GetAllTablesAndViews(session);
        Dictionary<string, Table> systemTables = GetMap(session);
        if (userTables.Count == 0)
        {
            return [.. systemTables.Values];
        }

        List<Table> result = new(systemTables.Count + userTables.Count);
        result.AddRange(systemTables.Values);
        result.AddRange(userTables);
        return result;
    }

    /// <inheritdoc/>
    public override bool IsEmpty() => false;
}
