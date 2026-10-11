/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.schema.Schema.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A schema as created by the SQL statement CREATE SCHEMA.
 *
 * Partially ported, and the part that is here is the one a table needs: the registry of tables
 * and views, and the unique-name rule that keeps a second CREATE TABLE from silently replacing
 * the first. What is not here — domains, synonyms, sequences, triggers, constraints, constants,
 * functions, the owner and rights — refuses, because each of those is a class of its own that
 * has not been ported, and a schema that could hold them without them would be a second kind of
 * schema.
 *
 * The map is case-insensitive, and that is not a detail: H2 stores object names folded to upper
 * case and the fold is the *database's* fold, not the current culture's, or a Turkish locale
 * would give a table two names. So the comparer is OrdinalIgnoreCase rather than
 * CurrentCultureIgnoreCase, for the same reason Util/CaseFolding exists.
 *
 * Concurrency is kept where it matters and is not invented where it does not. The reference uses
 * a concurrent map because tables are looked up while other sessions create them; here the
 * lookup path is a ConcurrentDictionary with the ordinal-ignoring comparer, which gives both.
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Tables;

namespace H2Sharp.Schemas;

/// <summary>
/// A schema as created by the SQL statement CREATE SCHEMA.
/// </summary>
/// <remarks>
/// A <see cref="DbObject"/> as in the reference, which is what gives it an object id, a place in
/// the database's object table, and — the reason it matters here — <c>getSQL</c>: an object that
/// belongs to a schema prints its name qualified by it, and a schema that is not itself an object
/// has nothing to print that qualification with.
/// </remarks>
public sealed class Schema : DbObject
{
    private readonly ConcurrentDictionary<string, Table> tablesAndViews =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly bool system;

    private readonly HashSet<string> temporaryUniqueNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a schema.
    /// </summary>
    /// <param name="database">the database</param>
    /// <param name="id">the object id</param>
    /// <param name="schemaName">the schema name</param>
    /// <param name="system">whether this is a system schema (such a schema cannot be dropped)</param>
    public Schema(Database database, int id, string schemaName, bool system)
        : base(database, id, schemaName, Trace.Schema)
    {
        this.system = system;
    }

    /// <summary>
    /// Gets the database this schema belongs to.
    /// </summary>
    public new Database GetDatabase()
    {
        return base.GetDatabase()!;
    }

    /// <summary>
    /// Gets the object id.
    /// </summary>
    public new int GetId()
    {
        return base.GetId();
    }

    /// <summary>
    /// Gets the schema name.
    /// </summary>
    public new string GetName()
    {
        return base.GetName()!;
    }

    /// <summary>
    /// Gets the schema name, the same thing as <see cref="GetName"/>.
    /// </summary>
    /// <returns>the schema name</returns>
    public string GetSchemaName()
    {
        return GetName();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A system schema is created by the database itself and has no statement that makes it, so
    /// there is nothing to print for one. That null is not an oversight to be filled in later:
    /// it is what tells INFORMATION_SCHEMA and a dump that this schema was never written by
    /// anybody.
    /// </remarks>
    public override string GetCreateSql()
    {
        if (system)
        {
            return null!;
        }

        return "CREATE SCHEMA IF NOT EXISTS " + GetSql(0) + " AUTHORIZATION "
            + GetOwnerName();
    }

    /// <inheritdoc/>
    public override int GetTypeId() => DbObject.Schema;

    /// <inheritdoc/>
    public override void RemoveChildrenAndResources(SessionLocal session)
        => throw new TODOException("Schema.removeChildrenAndResources: the schema holds more than tables");

    /// <summary>
    /// The name of the user that owns this schema.
    /// </summary>
    /// <returns>the owner name</returns>
    private string GetOwnerName()
        => throw new TODOException("Schema.getCreateSQL: the owner of a schema is not ported");

    /// <summary>
    /// Whether this is a system schema.
    /// </summary>
    public bool IsSystem()
    {
        return system;
    }

    /// <summary>
    /// Check if this schema can be dropped. System schemas can not be dropped.
    /// </summary>
    /// <returns>true if it can be dropped</returns>
    public bool CanDrop()
    {
        return !system;
    }

    /// <summary>
    /// Try to find a table or view with this name. This method returns null if no object with
    /// this name exists.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="name">the object name</param>
    /// <returns>the object or null</returns>
    public Table? FindTableOrView(SessionLocal? session, string name)
    {
        _ = tablesAndViews.TryGetValue(name, out Table? table);
        return table;
    }

    /// <summary>
    /// Get the table or view with the given name.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="name">the table or view name</param>
    /// <returns>the table or view</returns>
    public Table GetTableOrView(SessionLocal? session, string name)
    {
        Table? table = FindTableOrView(session, name);
        return table ?? throw DbException.Get(ErrorCode.TableOrViewNotFound1, name);
    }

    /// <summary>
    /// Adds a table or view.
    /// </summary>
    /// <remarks>
    /// A name already in use is refused, and that is the check that makes a second
    /// CREATE TABLE an error rather than a silent replacement of a table that already had rows
    /// in it.
    /// </remarks>
    /// <param name="table">the table or view</param>
    public void Add(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        string tableName = table.GetTableName();
        if (!tablesAndViews.TryAdd(tableName, table))
        {
            throw DbException.Get(ErrorCode.TableOrViewAlreadyExists1, tableName);
        }
    }

    /// <summary>
    /// Removes a table or view.
    /// </summary>
    /// <param name="table">the table or view</param>
    public void Remove(Table table)
    {
        _ = tablesAndViews.TryRemove(table.GetTableName(), out _);
    }

    /// <summary>
    /// Gets all tables and views.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>all tables and views</returns>
    public List<Table> GetAllTablesAndViews(SessionLocal session)
        => [.. tablesAndViews.Values];

    /// <summary>
    /// Whether this schema holds nothing.
    /// </summary>
    public bool IsEmpty()
    {
        return tablesAndViews.IsEmpty;
    }

    /// <summary>
    /// Creates a unique table name from the given prefix, by appending a number.
    /// </summary>
    /// <remarks>
    /// The set of names handed out but not yet stored is what keeps two threads creating tables
    /// at the same time from being handed the same name — the reference keeps that set for the
    /// same reason, and the moment between "name chosen" and "table stored" is exactly the
    /// window in which a plain map would collide.
    /// </remarks>
    /// <param name="prefix">the prefix</param>
    /// <param name="mapping">the map the name must be unique in</param>
    /// <returns>the unique name</returns>
    public string GetUniqueName(string prefix, IDictionary<string, Table> mapping)
    {
        while (true)
        {
            string name = prefix;
            for (int i = 0, len = mapping.Count + temporaryUniqueNames.Count; i < len; i++)
            {
                _ = temporaryUniqueNames.Add(name);
                if (!mapping.ContainsKey(name))
                {
                    return name;
                }

                name = prefix + "_" + i;
            }
        }
    }
}
