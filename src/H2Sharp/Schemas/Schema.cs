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
 * and views, the registry of indexes, and the unique-name rule that keeps a second CREATE TABLE
 * from silently replacing the first and gives an index the name the JVM gives it. What is not
 * here — domains, synonyms, sequences, triggers, constraints, constants, functions, the owner
 * and rights — refuses, because each of those is a class of its own that has not been ported,
 * and a schema that could hold them without them would be a second kind of schema.
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
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Tables;
using Index = H2Sharp.Indexes.Index;

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

    private readonly ConcurrentDictionary<string, Index> indexes =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Constraint> constraints =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Sequence> sequences =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Domain> domains =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Constant> constants =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, TableSynonym> synonyms =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly bool system;

    private readonly string ownerName;

    private readonly HashSet<string> temporaryUniqueNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a schema.
    /// </summary>
    /// <param name="database">the database</param>
    /// <param name="id">the object id</param>
    /// <param name="schemaName">the schema name</param>
    /// <param name="system">whether this is a system schema (such a schema cannot be dropped)</param>
    /// <param name="ownerName">the owner name, or null for SA</param>
    public Schema(Database database, int id, string schemaName, bool system, string? ownerName = null)
        : base(database, id, schemaName, Trace.Schema)
    {
        this.system = system;
        this.ownerName = ownerName ?? "SA";
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
    public override string? GetCreateSql()
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
    {
        Database database = GetDatabase();
        foreach (Constraint constraint in GetAllConstraints().ToArray())
        {
            database.RemoveSchemaObject(session, constraint);
        }

        while (true)
        {
            List<Table> tables = GetAllTablesAndViews(session);
            if (tables.Count == 0) break;
            database.RemoveSchemaObject(session, tables[0]);
        }

        foreach (TableSynonym synonym in GetAllSynonyms().ToArray())
        {
            database.RemoveSchemaObject(session, synonym);
        }

        foreach (Domain domain in GetAllDomains().ToArray())
        {
            database.RemoveSchemaObject(session, domain);
        }

        foreach (Constant constant in GetAllConstants().ToArray())
        {
            database.RemoveSchemaObject(session, constant);
        }

        foreach (Index index in GetAllIndexes().ToArray())
        {
            database.RemoveSchemaObject(session, index);
        }

        foreach (Sequence sequence in GetAllSequences().ToArray())
        {
            database.RemoveSchemaObject(session, sequence);
        }

        database.RemoveMeta(session, GetId());
        Invalidate();
    }

    /// <summary>
    /// Gets the owner name used by CREATE SCHEMA.
    /// </summary>
    /// <remarks>
    /// D-004 has no users or roles, so every schema is owned by the session user the port
    /// creates, SA. The reference reads the owner object here; when users are ported this
    /// becomes the owner's SQL name.
    /// </remarks>
    /// <returns>the owner name</returns>
    public string GetOwnerName() => ownerName;

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
        return table ?? session?.FindLocalTempTable(name);
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
    /// Finds a table or view, resolving a synonym to its backing table.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="name">the object name</param>
    /// <param name="resolveMaterializedView">whether to return the underlying table of a
    /// materialized view</param>
    /// <returns>the table or view, or null</returns>
    public Table? ResolveTableOrView(
        SessionLocal? session, string name, bool resolveMaterializedView = true)
    {
        Table? table = FindTableOrView(session, name);
        if (table is null)
        {
            TableSynonym? synonym = FindSynonym(name);
            if (synonym is not null)
            {
                return synonym.GetSynonymFor();
            }
        }

        if (resolveMaterializedView && table is MaterializedView materializedView)
        {
            return materializedView.GetUnderlyingTable();
        }

        return table;
    }

    /// <summary>
    /// Try to find a constraint with this name. This method returns null if no constraint with
    /// this name exists.
    /// </summary>
    /// <remarks>
    /// The reference also looks the name up among the session's local temporary table
    /// constraints; session temporary tables are not ported here, so that half of the lookup
    /// has nothing it could find.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="name">the constraint name</param>
    /// <returns>the constraint or null</returns>
    public Constraint? FindConstraint(SessionLocal session, string name)
    {
        _ = session;
        _ = constraints.TryGetValue(name, out Constraint? constraint);
        return constraint;
    }

    /// <summary>
    /// Try to find an index with this name.
    /// </summary>
    /// <remarks>
    /// The reference also looks the name up among the session's local temporary table
    /// indexes; session temporary tables are not ported here, so that half of the lookup has
    /// nothing it could find.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="name">the object name</param>
    /// <returns>the index or null</returns>
    public Index? FindIndex(SessionLocal session, string name)
    {
        _ = session;
        _ = indexes.TryGetValue(name, out Index? index);
        return index;
    }

    /// <summary>
    /// Try to find a sequence with this name.
    /// </summary>
    /// <param name="name">the object name</param>
    /// <returns>the sequence or null</returns>
    public Sequence? FindSequence(string name)
    {
        _ = sequences.TryGetValue(name, out Sequence? sequence);
        return sequence;
    }

    /// <summary>
    /// Gets the sequence with this name, as the reference's getSequence does.
    /// </summary>
    /// <param name="name">the sequence name</param>
    /// <returns>the sequence</returns>
    public Sequence GetSequence(string name)
    {
        Sequence? sequence = FindSequence(name);
        if (sequence is null)
        {
            throw DbException.Get(ErrorCode.SequenceNotFound1, name);
        }

        return sequence;
    }

    /// <summary>
    /// Gets all the sequences of this schema.
    /// </summary>
    /// <returns>the sequences</returns>
    public List<Sequence> GetAllSequences() => [.. sequences.Values];

    /// <summary>Finds a domain by name.</summary>
    public Domain? FindDomain(string name) => domains.TryGetValue(name, out Domain? domain) ? domain : null;

    /// <summary>Gets all domains.</summary>
    public List<Domain> GetAllDomains() => [.. domains.Values];

    /// <summary>Finds a synonym by name.</summary>
    public TableSynonym? FindSynonym(string name)
        => synonyms.TryGetValue(name, out TableSynonym? synonym) ? synonym : null;

    /// <summary>Gets all synonyms of this schema.</summary>
    public List<TableSynonym> GetAllSynonyms() => [.. synonyms.Values];

    /// <summary>Creates a synonym from its definition, as Schema.createSynonym.</summary>
    public TableSynonym CreateSynonym(Command.Ddl.CreateSynonymData data)
    {
        lock (GetDatabase())
        {
            GetDatabase().LockMeta(data.Session);
            data.Schema = this;
            return new TableSynonym(data);
        }
    }

    /// <summary>Finds a constant by name.</summary>
    public Constant? FindConstant(string name)
        => constants.TryGetValue(name, out Constant? constant) ? constant : null;

    /// <summary>Gets all constants of this schema.</summary>
    public List<Constant> GetAllConstants() => [.. constants.Values];

    /// <summary>Gets all constraints of this schema.</summary>
    public List<Constraint> GetAllConstraints() => [.. constraints.Values];

    /// <summary>Gets a name for a new constraint of the domain.</summary>
    public string GetUniqueDomainConstraintName(SessionLocal session, Domain domain)
        => GetUniqueName(domain.GetName()!, constraints, "CONSTRAINT_");

    /// <summary>Renames a schema object.</summary>
    public void Rename(SchemaObject obj, string newName)
    {
        string oldName = obj.GetName()!;
        switch (obj)
        {
            case Domain domain:
                _ = domains.TryRemove(oldName, out _);
                domain.Rename(newName);
                if (!domains.TryAdd(newName, domain))
                {
                    throw DbException.GetInternalError("object already exists: " + newName);
                }

                break;
            case Constraint constraint:
                _ = constraints.TryRemove(oldName, out _);
                constraint.Rename(newName);
                if (!constraints.TryAdd(newName, constraint))
                {
                    throw DbException.GetInternalError("object already exists: " + newName);
                }

                break;
            case Index index:
                _ = indexes.TryRemove(oldName, out _);
                index.Rename(newName);
                if (!indexes.TryAdd(newName, index))
                    throw DbException.GetInternalError("object already exists: " + newName);
                break;
            default:
                throw new TODOException("Schema.rename: the object kind is not ported");
        }
    }

    /// <summary>Renames a table (D-011: Table is not a SchemaObject in this hierarchy).</summary>
    public void Rename(Table table, string newName)
    {
        string oldName = table.GetTableName();
        if (oldName == newName || tablesAndViews.ContainsKey(newName))
            throw DbException.GetInternalError("object already exists: " + newName);
        if (!tablesAndViews.TryRemove(oldName, out _))
            throw DbException.GetInternalError("not found: " + oldName);
        table.Rename(newName);
        tablesAndViews[newName] = table;
    }

    /// <summary>Creates a table from its definition, as Schema.createTable.</summary>
    public Table CreateTable(Command.Ddl.CreateTableData data)
    {
        lock (GetDatabase())
        {
            if (!data.Temporary || data.GlobalTemporary) GetDatabase().LockMeta(data.Session);
            data.Schema = this;
            if (data.TableEngine is not null)
            {
                throw new TODOException(
                    "Schema.createTable: custom table engines are not ported: " + data.TableEngine);
            }

            // The port's existing default store table factory is the MVTable constructor.
            MvStore.Db.MVTable table = new(GetDatabase(), this, data.Id, data.TableName,
                [.. data.Columns], data.PersistIndexes, data.PersistData);
            table.SetTemporary(data.Temporary);
            table.SetGlobalTemporary(data.GlobalTemporary);
            return table;
        }
    }

    /// <summary>
    /// Adds a schema object to this schema.
    /// </summary>
    /// <remarks>
    /// The reference has one method taking a SchemaObject, because there every schema object is
    /// one; here a table does not share that base, so it has its own overload and the two kinds
    /// are registered by the type. A duplicate index name is an internal error, as in the
    /// reference — an index name is generated by this schema, so a taken one means two writers
    /// of the same name, not a user mistake. A duplicate table name is the user-visible
    /// TableOrViewAlreadyExists1 that CREATE TABLE answers with.
    /// </remarks>
    /// <param name="obj">the object to add</param>
    public void Add(SchemaObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        switch (obj)
        {
            case Index index:
                {
                    string name = index.GetName()!;
                    if (!indexes.TryAdd(name, index))
                    {
                        throw DbException.GetInternalError("object already exists: " + name);
                    }

                    FreeUniqueName(name);
                    return;
                }

            case Constraint constraint:
                {
                    string name = constraint.GetName()!;
                    if (!constraints.TryAdd(name, constraint))
                    {
                        throw DbException.GetInternalError("object already exists: " + name);
                    }

                    FreeUniqueName(name);
                    return;
                }

            case Sequence sequence:
                {
                    string name = sequence.GetName()!;
                    if (!sequences.TryAdd(name, sequence))
                    {
                        throw DbException.GetInternalError("object already exists: " + name);
                    }

                    FreeUniqueName(name);
                    return;
                }

            case Domain domain:
                {
                    string name = domain.GetName()!;
                    if (!domains.TryAdd(name, domain))
                    {
                        throw DbException.GetInternalError("object already exists: " + name);
                    }

                    FreeUniqueName(name);
                    return;
                }

            case Constant constant:
                {
                    string name = constant.GetName()!;
                    if (!constants.TryAdd(name, constant))
                    {
                        throw DbException.GetInternalError("object already exists: " + name);
                    }

                    FreeUniqueName(name);
                    return;
                }

            case TableSynonym synonym:
                {
                    string name = synonym.GetName()!;
                    if (!synonyms.TryAdd(name, synonym))
                    {
                        throw DbException.GetInternalError("object already exists: " + name);
                    }

                    FreeUniqueName(name);
                    return;
                }

            default:
                throw new TODOException("Schema.add: the object kind is not ported");
        }
    }

    /// <summary>
    /// Adds a table or view.
    /// </summary>
    /// <remarks>
    /// A name already in use is refused, and that is the check that makes a second
    /// CREATE TABLE an error rather than a silent replacement of a table that already had rows
    /// in it. The reference answers this name collision before it gets here — CREATE TABLE
    /// looks the name up first — and treats a duplicate in this method as an internal error;
    /// the user-visible answer is what both paths produce.
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

        FreeUniqueName(tableName);
    }

    /// <summary>
    /// Removes a schema object from this schema.
    /// </summary>
    /// <param name="obj">the object to remove</param>
    public void Remove(SchemaObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        switch (obj)
        {
            case Index index:
                {
                    string objName = index.GetName()!;
                    if (!indexes.TryRemove(objName, out _))
                    {
                        throw DbException.GetInternalError("not found: " + objName);
                    }

                    FreeUniqueName(objName);
                    return;
                }

            case Constraint constraint:
                {
                    string objName = constraint.GetName()!;
                    if (!constraints.TryRemove(objName, out _))
                    {
                        throw DbException.GetInternalError("not found: " + objName);
                    }

                    FreeUniqueName(objName);
                    return;
                }

            case Sequence sequence:
                {
                    string objName = sequence.GetName()!;
                    if (!sequences.TryRemove(objName, out _))
                    {
                        throw DbException.GetInternalError("not found: " + objName);
                    }

                    FreeUniqueName(objName);
                    return;
                }

            case Domain domain:
                {
                    string objName = domain.GetName()!;
                    if (!domains.TryRemove(objName, out _))
                    {
                        throw DbException.GetInternalError("not found: " + objName);
                    }

                    FreeUniqueName(objName);
                    return;
                }

            case Constant constant:
                {
                    string objName = constant.GetName()!;
                    if (!constants.TryRemove(objName, out _))
                    {
                        throw DbException.GetInternalError("not found: " + objName);
                    }

                    FreeUniqueName(objName);
                    return;
                }

            case TableSynonym synonym:
                {
                    string objName = synonym.GetName()!;
                    if (!synonyms.TryRemove(objName, out _))
                    {
                        throw DbException.GetInternalError("not found: " + objName);
                    }

                    FreeUniqueName(objName);
                    return;
                }

            default:
                throw new TODOException("Schema.remove: the object kind is not ported");
        }
    }

    /// <summary>
    /// Removes a table or view.
    /// </summary>
    /// <param name="table">the table or view</param>
    public void Remove(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        string objName = table.GetTableName();
        if (!tablesAndViews.TryRemove(objName, out _))
        {
            throw DbException.GetInternalError("not found: " + objName);
        }

        FreeUniqueName(objName);
    }

    /// <summary>
    /// Gets all tables and views.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>all tables and views</returns>
    public List<Table> GetAllTablesAndViews(SessionLocal session)
        => [.. tablesAndViews.Values];

    /// <summary>
    /// Gets all indexes of this schema.
    /// </summary>
    /// <returns>all indexes</returns>
    public List<Index> GetAllIndexes() => [.. indexes.Values];

    /// <summary>
    /// Whether this schema holds nothing.
    /// </summary>
    public bool IsEmpty()
    {
        return tablesAndViews.IsEmpty && indexes.IsEmpty && constraints.IsEmpty && synonyms.IsEmpty;
    }

    /// <summary>
    /// Reserves a unique object name.
    /// </summary>
    /// <param name="name">the object name</param>
    public void ReserveUniqueName(string? name)
    {
        if (name is not null)
        {
            lock (temporaryUniqueNames)
            {
                _ = temporaryUniqueNames.Add(name);
            }
        }
    }

    /// <summary>
    /// Releases a unique object name.
    /// </summary>
    /// <param name="name">the object name</param>
    public void FreeUniqueName(string? name)
    {
        if (name is not null)
        {
            lock (temporaryUniqueNames)
            {
                _ = temporaryUniqueNames.Remove(name);
            }
        }
    }

    /// <summary>
    /// Creates a unique index name.
    /// </summary>
    /// <remarks>
    /// The name is derived from the indexed table's name, so that every index of one table
    /// shares a prefix and the first free suffix wins; that is what makes the second primary
    /// key of a test database come out as PRIMARY_KEY_5 on the JVM as well.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="table">the indexed table</param>
    /// <param name="prefix">the index name prefix</param>
    /// <returns>the unique name</returns>
    public string GetUniqueIndexName(SessionLocal session, Table table, string prefix)
    {
        if (table.IsTemporary() && !table.IsGlobalTemporary())
        {
            Dictionary<string, object?> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (Table temp in session.GetLocalTempTables())
            {
                foreach (Index tempIndex in temp.GetIndexes())
                {
                    string? name = tempIndex.GetName();
                    if (name is not null)
                    {
                        names[name] = null;
                    }
                }
            }

            return GetUniqueName(table.GetTableName(), names, prefix);
        }

        _ = session;
        return GetUniqueName(table.GetTableName(), indexes, prefix);
    }

    /// <summary>
    /// Creates a unique constraint name.
    /// </summary>
    /// <remarks>
    /// The rule and the prefix are the reference's: "CONSTRAINT_" followed by the hashed table
    /// name, so the first primary key of a table T is CONSTRAINT_5 on the JVM and here. The
    /// name is looked for among constraints, not among all objects: an index and a constraint
    /// may carry the same name in the reference, and a lookup in the wrong map would rename
    /// one of them without cause.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="table">the constraint table</param>
    /// <returns>the unique name</returns>
    public string GetUniqueConstraintName(SessionLocal session, Table table)
    {
        if (table.IsTemporary() && !table.IsGlobalTemporary())
        {
            Dictionary<string, object?> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (Table temp in session.GetLocalTempTables())
            {
                foreach (Constraint tempConstraint in temp.GetConstraints())
                {
                    string? name = tempConstraint.GetName();
                    if (name is not null)
                    {
                        names[name] = null;
                    }
                }
            }

            return GetUniqueName(table.GetTableName(), names, "CONSTRAINT_");
        }

        _ = session;
        return GetUniqueName(table.GetTableName(), constraints, "CONSTRAINT_");
    }

    /// <summary>
    /// Creates a unique name for the given object, by walking its name's hash.
    /// </summary>
    /// <remarks>
    /// The algorithm is the reference's and its shape is load-bearing: the hash is written in
    /// hex, upper-cased, and appended one character at a time until a candidate is free, so
    /// both the map and the set of names handed out but not yet stored are consulted. The
    /// reference hashes the object; here the name is passed because a table does not share the
    /// object base the reference hashes through.
    /// </remarks>
    /// <typeparam name="T">the kind of schema object in the map</typeparam>
    /// <param name="name">the object name to derive from</param>
    /// <param name="map">the map the name must be unique in</param>
    /// <param name="prefix">the name prefix</param>
    /// <returns>the unique name</returns>
    private string GetUniqueName<T>(string name, IDictionary<string, T> map, string prefix)
    {
        StringBuilder nameBuilder = new(prefix);
        string hash = ((uint)Util.Utils.JavaHashCode(name)).ToString("x", CultureInfo.InvariantCulture);
        lock (temporaryUniqueNames)
        {
            for (int i = 0, len = hash.Length; i < len; i++)
            {
                char c = hash[i];
                string candidate = nameBuilder.Append(c >= 'a' ? (char)(c - 0x20) : c).ToString();
                if (!map.ContainsKey(candidate) && temporaryUniqueNames.Add(candidate))
                {
                    return candidate;
                }
            }

            int nameLength = nameBuilder.Append('_').Length;
            for (int i = 0; ; i++)
            {
                string candidate = nameBuilder.Append(i.ToString(CultureInfo.InvariantCulture)).ToString();
                if (!map.ContainsKey(candidate) && temporaryUniqueNames.Add(candidate))
                {
                    return candidate;
                }

                nameBuilder.Length = nameLength;
            }
        }
    }
}
