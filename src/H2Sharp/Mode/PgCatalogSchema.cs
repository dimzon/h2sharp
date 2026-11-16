/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.mode.PgCatalogSchema.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The pg_catalog schema holds one generated table for every PostgreSQL compatibility view.
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Modes;

/// <summary>
/// The <c>pg_catalog</c> schema.
/// </summary>
public sealed class PgCatalogSchema : MetaSchema
{
    private Dictionary<string, Table>? tables;

    /// <summary>
    /// Creates the schema.
    /// </summary>
    /// <param name="database">the database</param>
    public PgCatalogSchema(Database database)
        : base(database, Constants.PgCatalogSchemaId, Constants.SchemaPgCatalog)
    {
    }

    /// <inheritdoc/>
    protected override Dictionary<string, Table> GetMap(SessionLocal? session)
        => tables ??= FillMap();

    private Dictionary<string, Table> FillMap()
    {
        Dictionary<string, Table> map = new(GetDatabase().IdentifierComparer);
        for (int type = 0; type < PgCatalogTable.MetaTableTypeCount; type++)
        {
            PgCatalogTable table = new(this, Constants.PgCatalogSchemaId - type, type);
            map[table.GetTableName()] = table;
        }

        return map;
    }
}
