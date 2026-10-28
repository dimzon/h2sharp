/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.Database.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Partially ported, in the order the connection path needs: what a database *is* while there is
 * nothing in it yet — its name, whether it is memory, the object ids, the system table, and
 * the rule that an in-memory database disappears when its last session closes. Everything that
 * needs a writer thread or the user layer is stubbed and refuses.
 *
 * The close rule is the part worth having first, because it is observable without a single
 * table: two connections to the same memory storage see one database, and after the second of
 * them closes, a new connection finds nothing. A port that kept the registry entry forever would
 * still answer every query correctly and would leak a database per name for the life of the
 * process — which is exactly the kind of thing a test that only creates tables never notices.
 *
 * Object ids are allocated here and nowhere else, from the reference's bit set: id 0 is SYS
 * (and PUBLIC), restored rows set their bits, and the first user object takes id 1. A port
 * that handed out id 0 would agree on every query and
 * disagree with every file written from it.
 */
using System;
using System.Collections.Generic;
using System.Threading;
using H2Sharp.Command;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Engine;

/// <summary>
/// A database.
/// </summary>
public sealed partial class Database : CastDataProvider
{
    /// <summary>
    /// The name of the internal user the reference runs startup and metadata as.
    /// </summary>
    private const string SystemUserName = "DBA";

    /// <summary>
    /// The object ids in use, as the reference's <c>objectIds</c>: bit 0 belongs to the
    /// metadata table, id 0 itself, and the next free bit is the next id handed out.
    /// </summary>
    private readonly BitSet objectIds = new();

    private readonly ConnectionInfo connectionInfo;

    private readonly string name;

    private readonly bool memory;

    /// <summary>
    /// The user sessions. Internal sessions (system, LOB) are not here, as in the reference:
    /// only these keep the database open.
    /// </summary>
    private readonly List<SessionLocal> sessions = [];

    private readonly object syncRoot = new();

    /// <summary>
    /// The session startup and metadata run as.
    /// </summary>
    private SessionLocal? systemSession;

    /// <summary>
    /// The system table: id, header, type, SQL.
    /// </summary>
    private Tables.Table? meta;

    /// <summary>
    /// The index on <c>SYS.ID</c>.
    /// </summary>
    private Indexes.Index? metaIdIndex;

    private long modificationMetaId;

    /// <summary>
    /// The store this database's tables live in.
    /// </summary>
    /// <remarks>
    /// The reference's Database owns its MVStore, and every table opens its maps on it: a
    /// table that brought its own store would be invisible to every transaction but its own.
    /// </remarks>
    private readonly MvStore.Db.Store store;

    /// <summary>
    /// The compare mode this database's sessions compare with, as in the reference.
    /// </summary>
    private Values.CompareMode compareMode = Values.CompareMode.GetInstance(null, 0);

    /// <summary>
    /// The row factory this database's tables and indexes use.
    /// </summary>
    /// <remarks>
    /// Reference: org.h2.engine.Database.java:217, 416
    /// Initialized with the default factory; tables and indexes build their own
    /// variants through createRowFactory.
    /// </remarks>
    private Result.RowFactory rowFactory = Result.RowFactory.GetDefaultRowFactory();

    /// <summary>
    /// The schemas of this database, by name.
    /// </summary>
    /// <remarks>
    /// Case-insensitive, as every name in H2 is: the fold is the database's, not the current
    /// culture's. It is a plain dictionary under the lock rather than a concurrent one, because
    /// the reference locks the database around schema changes and a lookup that raced a CREATE
    /// SCHEMA would be a lookup that answered differently on two threads at the same moment.
    /// </remarks>
    private readonly Dictionary<string, Schemas.Schema> schemas = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The name of the schema of the metadata views.
    /// </summary>
    public const string InformationSchemaName = "INFORMATION_SCHEMA";

    /// <summary>
    /// The schema new objects go into when the session has not chosen another.
    /// </summary>
    /// <remarks>
    /// Volatile rather than locked on read: a lone reference reads atomically, and the lock
    /// stays on the write, so every read sees a schema the write published — the same answers
    /// without blocking sessions that only resolve names.
    /// </remarks>
    private volatile Schemas.Schema? mainSchema;

    /// <summary>
    /// The schema of the metadata views, the INFORMATION_SCHEMA.
    /// </summary>
    /// <remarks>
    /// The reference creates it with the database and fills it with the views of the
    /// metadata. The views here are computed when they are read, so the schema holds the
    /// tables and nothing else.
    /// </remarks>
    private readonly Schemas.Schema informationSchema;

    /// <summary>
    /// Finds a schema.
    /// </summary>
    /// <param name="name">the schema name</param>
    /// <returns>the schema, or null</returns>
    public Schemas.Schema? FindSchema(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        lock (syncRoot)
        {
            return schemas.TryGetValue(name, out Schemas.Schema? schema) ? schema : null;
        }
    }

    /// <summary>
    /// Gets a schema, or fails with the error the reference fails with.
    /// </summary>
    /// <param name="name">the schema name</param>
    /// <returns>the schema</returns>
    public Schemas.Schema GetSchema(string name)
    {
        return FindSchema(name) ?? throw DbException.Get(Api.ErrorCode.SchemaNotFound1, name);
    }

    /// <summary>
    /// Adds a schema.
    /// </summary>
    /// <param name="schema">the schema</param>
    public void AddSchema(Schemas.Schema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        lock (syncRoot)
        {
            schemas[schema.GetName()] = schema;
        }
    }

    /// <summary>
    /// Removes a schema.
    /// </summary>
    /// <param name="schema">the schema</param>
    public void RemoveSchema(Schemas.Schema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        lock (syncRoot)
        {
            _ = schemas.Remove(schema.GetName());
        }
    }

    /// <summary>
    /// Gets the schema new objects go into, or null before there is one.
    /// </summary>
    /// <returns>the main schema, or null</returns>
    public Schemas.Schema? GetMainSchema() => mainSchema;

    /// <summary>
    /// Sets the schema new objects go into.
    /// </summary>
    /// <param name="schema">the schema</param>
    public void SetMainSchema(Schemas.Schema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        lock (syncRoot)
        {
            mainSchema = schema;
        }
    }

    /// <summary>
    /// Whether two identifiers name the same thing.
    /// </summary>
    /// <remarks>
    /// Exact match first, because that is the common case and the fast one; case-insensitive
    /// only when the database was opened that way. The fold is Java's English one rather than
    /// .NET's, for the same reason it is everywhere else names are compared: the two runtimes
    /// fold different letters, and an identifier that matched here and not on the JVM would be
    /// a table found in one runtime and missing in the other.
    /// </remarks>
    /// <param name="a">the first name</param>
    /// <param name="b">the second name</param>
    /// <returns>true when they name the same thing</returns>
    public bool EqualsIdentifiers(string a, string b)
    {
        return string.Equals(a, b, StringComparison.Ordinal)
            || (caseInsensitiveIdentifiers
                && Util.StringUtils.ToUpperEnglish(a).Equals(Util.StringUtils.ToUpperEnglish(b), StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether identifiers compare without case. False, as in the reference, where it comes
    /// from the database settings: the settings object is not ported, so the default is
    /// written here and EqualsIdentifiers reads it.
    /// </summary>
    private readonly bool caseInsensitiveIdentifiers = false;

    /// <summary>
    /// Gets the schemas of this database.
    /// </summary>
    /// <returns>the schemas</returns>
    public IEnumerable<Schemas.Schema> GetSchemas()
    {
        lock (syncRoot)
        {
            return [.. schemas.Values];
        }
    }

    private bool closing;

    /// <summary>
    /// Whether the database is opening.
    /// </summary>
    /// <remarks>
    /// Set while the constructor builds SYS and replays its rows, as the reference does: the
    /// parser treats that window as quirks mode and the metadata writers must not compare
    /// against rows that are being read.
    /// </remarks>
    private bool starting = false;
    /// <summary>
    /// Whether the database is opening.
    /// </summary>
    /// <returns>true while opening</returns>
    public bool IsStarting()
    {
        return starting;
    }

    /// <summary>
    /// Locks the meta data of the database.
    /// </summary>
    /// <remarks>
    /// The reference locks the SYS table exclusively. Its lock manager is not ported, so this
    /// answers locked without taking anything, and <see cref="UnlockMeta"/> does nothing.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <returns>true</returns>
    public bool LockMeta(SessionLocal session)
    {
        return true;
    }

    /// <summary>Checks the actual SYS-table lock (D-011: meta locking remains unported).</summary>
    public bool IsSysTableLockedBy(SessionLocal session) => meta is null || meta.IsLockedExclusivelyBy(session);

    /// <summary>Gets the internal session used for immediately committed metadata writes.</summary>
    public SessionLocal GetSystemSession() => systemSession!;

    /// <summary>
    /// Adds a schema object to the database.
    /// </summary>
    /// <remarks>
    /// The registry write is real; the meta-table write after it is deferred with the meta
    /// table, so an added object exists for as long as the process does. The reference has one
    /// method for every schema object, because there every schema object is a DbObject; here a
    /// table is not, so the table has its own overload and both share the body.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="obj">the object to add</param>
    public void AddSchemaObject(SessionLocal session, Schemas.SchemaObject obj)
        => AddSchemaObject(session, obj.GetId(), () => obj.GetSchema().Add(obj), () => AddMeta(session, obj));

    /// <summary>
    /// Adds a table to the database.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="table">the table to add</param>
    public void AddSchemaObject(SessionLocal session, Tables.Table table)
        => AddSchemaObject(session, table.GetId(), () => table.GetSchema()!.Add(table),
            () => AddMeta(session, table));

    private void AddSchemaObject(SessionLocal session, int id, Action add, Action addMeta)
    {
        if (id > 0 && !starting)
        {
            CheckWritingAllowed();
        }

        LockMeta(session);
        lock (syncRoot)
        {
            add();
            addMeta();
        }

        ClearQueryCache(id);
    }

    /// <summary>
    /// Removes a schema object from the database.
    /// </summary>
    /// <remarks>
    /// The reference handles local temporary tables, indexes and constraints through the
    /// session's maps before this point; local temporary tables are not ported, and nothing
    /// here can be one. The dependency checks past the schema removal need views and domains,
    /// neither of which is ported, so nothing can depend on what is removed yet and the
    /// children are removed directly.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="obj">the object to remove</param>
    public void RemoveSchemaObject(SessionLocal session, Schemas.SchemaObject obj)
        => RemoveSchemaObject(session, obj.GetId(), () => obj.GetSchema().Remove(obj),
            obj.RemoveChildrenAndResources, () => RemoveMeta(session, obj.GetId()));

    /// <summary>
    /// Removes a table from the database.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="table">the table to remove</param>
    public void RemoveSchemaObject(SessionLocal session, Tables.Table table)
        => RemoveSchemaObject(session, table.GetId(), () => table.GetSchema()!.Remove(table),
            table.RemoveChildrenAndResources, () => RemoveMeta(session, table.GetId()));

    private void RemoveSchemaObject(
        SessionLocal session,
        int id,
        Action remove,
        Action<SessionLocal> removeChildrenAndResources,
        Action removeMeta)
    {
        CheckWritingAllowed();
        LockMeta(session);
        lock (syncRoot)
        {
            remove();
            if (!starting)
            {
                removeChildrenAndResources(session);
            }

            removeMeta();
        }

        ClearQueryCache(id);
    }

    private void ClearQueryCache(int id)
    {
        if (id > 0 && !starting)
        {
            _ = GetNextModificationMetaId();
        }
    }

    /// <summary>
    /// How many rows a result may hold in memory before it spills. The reference's default,
    /// from its MAX_MEMORY_ROWS setting.
    /// </summary>
    /// <remarks>
    /// Volatile rather than locked on read, like the main schema above: one word, same answers.
    /// </remarks>
    private volatile int maxMemoryRows = 40_000;
    /// <summary>
    /// Where a NULL goes when a statement did not say. LOW, because the reference starts there
    /// and every answer about ordering before a caller sets it comes from this field.
    /// </summary>
    /// <remarks>
    /// Volatile rather than locked on read: this is the field the ORDER BY comparator reads per
    /// NULL comparison, and a lone word needs no monitor to answer the same.
    /// </remarks>
    private volatile DefaultNullOrdering defaultNullOrdering = DefaultNullOrdering.LOW;

    /// <summary>
    /// Gets where a NULL goes when a statement did not say.
    /// </summary>
    /// <returns>the ordering</returns>
    public DefaultNullOrdering GetDefaultNullOrdering() => defaultNullOrdering;

    /// <summary>
    /// Sets where a NULL goes when a statement did not say.
    /// </summary>
    /// <param name="ordering">the ordering</param>
    public void SetDefaultNullOrdering(DefaultNullOrdering ordering)
    {
        lock (syncRoot)
        {
            defaultNullOrdering = ordering;
        }
    }

    /// <summary>
    /// Opens a database.
    /// </summary>
    /// <param name="connectionInfo">the connection information</param>
    public Database(ConnectionInfo connectionInfo)
    {
        this.connectionInfo = connectionInfo;
        name = connectionInfo.Name;
        memory = connectionInfo.Storage == StorageKind.Memory;
        mode = connectionInfo.Mode;

        ConnectionOptions options = connectionInfo.Options;
        lockMode = options.LockMode;
        pageSize = options.PageSize;

        string accessModeData = Util.StringUtils.ToLowerEnglish(options.AccessModeData);
        if (accessModeData == "r")
        {
            readOnly = true;
        }

        // The reference resolves FILE_LOCK here and defaults to FS, or to FILE when
        // AUTO_SERVER is on. Only FS (and NO, which for the MVStore behaves the same: the
        // data-file lock is still taken) is ported. The rest must be refused, because
        // silently doing FS for an option that asked for the .lock.db protocol is a different
        // database than the one the caller asked for.
        bool autoServerMode = options.AutoServer;
        H2Sharp.Store.FileLockMethod fileLockMethod = options.FileLock
            ?? (autoServerMode ? H2Sharp.Store.FileLockMethod.File : H2Sharp.Store.FileLockMethod.Fs);

        if (autoServerMode)
        {
            throw DbException.GetUnsupportedException(
                "AUTO_SERVER=TRUE: the port is embedded-only (D-003)");
        }

        if (fileLockMethod is H2Sharp.Store.FileLockMethod.File or H2Sharp.Store.FileLockMethod.Socket)
        {
            throw new TODOException(
                "FileLock: FILE_LOCK=" + options.FileLock
                + " needs the .lock.db protocol, which is not ported");
        }

        // A read-only database must not be opened while a .lock.db says another process holds
        // it. FS locks the data file itself, but a lock file left by a FILE/SOCKET holder is
        // still a reason to refuse rather than to guess.
        if (readOnly && !memory)
        {
            string lockFileName = name + Constants.SuffixLockFile;
            if (Store.FileSystem.FileUtils.Exists(lockFileName))
            {
                throw DbException.Get(Api.ErrorCode.DatabaseAlreadyOpen1,
                    "Lock file exists: " + lockFileName);
            }
        }

        // The main schema exists from the moment the database does, before anybody connects. A
        // session opening against a database with no schema would have nothing to create its
        // first object in, and the reference names PUBLIC here rather than making the first
        // statement do it. Its id is the constant zero, shared with the metadata table, so
        // neither is ever written to SYS.
        Schemas.Schema main = new(this, Constants.MainSchemaId, Constants.SchemaMain, system: false);
        AddSchema(main);
        SetMainSchema(main);

        // The metadata views live in their own schema, as in the reference: the views are
        // tables of a system schema, and the schema is what a query names them through. Its id
        // is the constant minus one, and the views count further down from there, so none of
        // them is allocated and none is stored.
        informationSchema = new(this, Constants.InformationSchemaId, InformationSchemaName, system: true);
        AddSchema(informationSchema);
        for (int type = 0; type < Tables.InformationSchemaTable.MetaTableTypeCount; type++)
        {
            informationSchema.Add(new Tables.InformationSchemaTable(
                informationSchema, Constants.InformationSchemaId - type, type));
        }

        store = new MvStore.Db.Store(this, null);
        store.GetTransactionStore().Init();

        // Startup runs as an internal session, as in the reference. Constructed without
        // registering, because only user sessions keep the database open; closing the last
        // user session has to close a file database even though this one still exists.
        systemSession = new SessionLocal(this, SystemUserName, NextSessionId());
        starting = true;
        try
        {
            meta = CreateSysTable();
            objectIds.Set(0);
            metaIdIndex = meta.AddIndex(systemSession, "SYS_ID", 0,
                Tables.IndexColumn.Wrap([meta.GetColumns()[0]]), 1,
                Indexes.IndexType.CreatePrimaryKey(false, false), create: true, indexComment: null);
            systemSession.Commit(true);
            ExecuteMeta();
            systemSession.Commit(true);
            store.GetTransactionStore().EndLeftoverTransactions();
        }
        finally
        {
            starting = false;
        }
    }

    /// <summary>
    /// Builds the system table, the reference's <c>createSysTableData</c> plus the
    /// <c>createTable</c> call that turns the description into a table in PUBLIC.
    /// </summary>
    /// <remarks>
    /// The table has id 0, so it is never itself stored in SYS; PUBLIC shares that id, and bit 0
    /// of <c>objectIds</c> stands for both.
    /// </remarks>
    /// <returns>the table</returns>
    private Tables.Table CreateSysTable()
    {
        Tables.Column[] columns =
        [
            new Tables.Column("ID", Values.TypeInfo.TypeInteger),
            new Tables.Column("HEAD", Values.TypeInfo.TypeInteger),
            new Tables.Column("TYPE", Values.TypeInfo.TypeInteger),
            new Tables.Column("SQL", Values.TypeInfo.TypeVarchar),
        ];
        columns[0].SetNullable(false);
        MvStore.Db.MVTable table = new(this, GetMainSchema(), 0, "SYS", columns, persistIndexes: IsPersistent(),
            persistData: IsPersistent());
        for (int i = 0; i < columns.Length; i++)
        {
            columns[i].SetTable(table, i);
        }
        return table;
    }

    /// <summary>
    /// Reads the system table and creates what it names, the reference's <c>executeMeta</c>.
    /// </summary>
    /// <remarks>
    /// The rows are grouped by object type and replayed in the reference's order: settings,
    /// users, schemas and function aliases first; domains next, retried until no domain in the
    /// batch still depends on one that failed; then sequences, constants, tables and indexes;
    /// then constraints, unique and primary keys before the rest; then everything else.
    /// </remarks>
    private void ExecuteMeta()
    {
        Indexes.Cursor cursor = metaIdIndex!.GetCursor(systemSession!, null, null, false)
            ?? throw DbException.GetInternalError("SYS_ID.find returned no cursor");
        List<MetaRecord> firstRecords = [], domainRecords = [], middleRecords = [],
            constraintRecords = [], lastRecords = [];
        while (cursor.Next())
        {
            MetaRecord rec = new(cursor.Get()!);
            objectIds.Set(rec.GetId());
            switch (rec.GetObjectType())
            {
                case DbObject.Setting:
                case DbObject.User:
                case DbObject.Schema:
                case DbObject.FunctionAlias:
                    firstRecords.Add(rec);
                    break;
                case DbObject.Domain:
                    domainRecords.Add(rec);
                    break;
                case DbObject.Sequence:
                case DbObject.Constant:
                case DbObject.TableOrView:
                case DbObject.Index:
                    middleRecords.Add(rec);
                    break;
                case DbObject.Constraint:
                    constraintRecords.Add(rec);
                    break;
                default:
                    lastRecords.Add(rec);
                    break;
            }
        }

        SessionLocal session = systemSession!;
        session.Lock();
        try
        {
            ExecuteMeta(firstRecords);

            // Domains may depend on other domains.
            int count = domainRecords.Count;
            if (count > 0)
            {
                for (int j = 0; ; count = j)
                {
                    H2SharpException? exception = null;
                    for (int i = 0; i < count; i++)
                    {
                        MetaRecord rec = domainRecords[i];
                        try
                        {
                            rec.PrepareAndExecute(this, session, listener: null, throwIt: true);
                        }
                        catch (H2SharpException ex)
                        {
                            exception ??= ex;
                            domainRecords[j++] = rec;
                        }
                    }

                    if (exception is null)
                    {
                        break;
                    }

                    if (count == j)
                    {
                        throw exception;
                    }
                }
            }

            ExecuteMeta(middleRecords);

            // Prepare, but don't create all constraints at once: unique and primary keys
            // first, then the rest.
            count = constraintRecords.Count;
            if (count > 0)
            {
                List<Prepared> constraints = new(count);
                for (int i = 0; i < count; i++)
                {
                    Prepared? prepared = constraintRecords[i].Prepare(this, session, listener: null, throwIt: false);
                    if (prepared is not null)
                    {
                        constraints.Add(prepared);
                    }
                }

                constraints.Sort(MetaRecord.ConstraintsComparator);
                foreach (Prepared constraint in constraints)
                {
                    MetaRecord.Execute(this, constraint, listener: null, constraint.GetSql(), throwIt: false);
                }
            }

            ExecuteMeta(lastRecords);
        }
        finally
        {
            session.Unlock();
        }
    }

    private void ExecuteMeta(List<MetaRecord> records)
    {
        if (records.Count > 0)
        {
            records.Sort();
            foreach (MetaRecord rec in records)
            {
                // D-011: the port has no users, roles or rights (D-004), so the rows the
                // reference stores for them cannot be replayed. Skip them explicitly instead
                // of relying on their SQL failing as a syntax error.
                if (rec.GetObjectType() is DbObject.User or DbObject.Role or DbObject.Right)
                {
                    continue;
                }

                rec.PrepareAndExecute(this, systemSession!, listener: null, throwIt: false);
            }
        }
    }

    /// <summary>
    /// Writes a database object to the system table, the reference's <c>addMeta</c>.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="obj">the object to store</param>
    private void AddMeta(SessionLocal session, DbObject obj)
    {
        int id = obj.GetId();
        if (id > 0 && !obj.IsTemporary() && !IsReadOnly())
        {
            Result.Row row = meta!.GetTemplateRow();
            MetaRecord.PopulateRowFromDbObject(obj, row);
            Indexes.Cursor cursor = metaIdIndex!.GetCursor(session, row, row, false)
                ?? throw DbException.GetInternalError("SYS_ID.find returned no cursor");
            if (!cursor.Next())
            {
                _ = meta.AddRow(session, row);
            }
            else
            {
                Result.Row oldRow = cursor.Get()!;
                MetaRecord rec = new(oldRow);
                if (!string.Equals(rec.GetSql(), obj.GetCreateSqlForMeta(), StringComparison.Ordinal))
                {
                    meta.UpdateRow(session, oldRow, row);
                }
            }
        }
    }

    /// <summary>
    /// Writes a table to the system table.
    /// </summary>
    /// <remarks>
    /// The reference has one <c>addMeta(DbObject)</c> because a table is a database object
    /// there. In the port a table is not, so it gets its own overload with the same row shape
    /// and the same "id greater than zero and not temporary" rule.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="table">the table to store</param>
    private void AddMeta(SessionLocal session, Tables.Table table)
    {
        int id = table.GetId();
        if (id > 0 && !table.IsTemporary() && !IsReadOnly())
        {
            Result.Row row = meta!.GetTemplateRow();
            MetaRecord.PopulateRowFromTable(table, row);
            Indexes.Cursor cursor = metaIdIndex!.GetCursor(session, row, row, false)
                ?? throw DbException.GetInternalError("SYS_ID.find returned no cursor");
            if (!cursor.Next())
            {
                _ = meta.AddRow(session, row);
            }
            else
            {
                Result.Row oldRow = cursor.Get()!;
                MetaRecord rec = new(oldRow);
                if (!string.Equals(rec.GetSql(), table.GetCreateSql(), StringComparison.Ordinal))
                {
                    meta.UpdateRow(session, oldRow, row);
                }
            }
        }
    }

    /// <summary>
    /// Updates an object in the system table, the reference's <c>updateMeta</c>.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="obj">the database object</param>
    public void UpdateMeta(SessionLocal session, DbObject obj)
    {
        int id = obj.GetId();
        if (id > 0)
        {
            if (!starting && !obj.IsTemporary())
            {
                Result.Row newRow = meta!.GetTemplateRow();
                MetaRecord.PopulateRowFromDbObject(obj, newRow);
                Result.Row? oldRow = FindMetaRow(session, id);
                if (oldRow is not null)
                {
                    meta.UpdateRow(session, oldRow, newRow);
                }
            }

            // For temporary objects.
            lock (objectIds)
            {
                objectIds.Set(id);
            }
        }
    }

    /// <summary>
    /// Updates a table in the system table.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="table">the table</param>
    public void UpdateMeta(SessionLocal session, Tables.Table table)
    {
        int id = table.GetId();
        if (id > 0)
        {
            if (!starting && !table.IsTemporary())
            {
                Result.Row newRow = meta!.GetTemplateRow();
                MetaRecord.PopulateRowFromTable(table, newRow);
                Result.Row? oldRow = FindMetaRow(session, id);
                if (oldRow is not null)
                {
                    meta.UpdateRow(session, oldRow, newRow);
                }
            }

            lock (objectIds)
            {
                objectIds.Set(id);
            }
        }
    }

    /// <summary>Renames a database object whose home is not a schema.</summary>
    /// <param name="session">the session</param>
    /// <param name="obj">the object</param>
    /// <param name="newName">the new name</param>
    public void RenameDatabaseObject(SessionLocal session, DbObject obj, string newName)
    {
        if (obj is not Schemas.Schema schema)
        {
            throw new TODOException("Database.renameDatabaseObject: only schemas are ported");
        }

        CheckWritingAllowed();
        lock (syncRoot)
        {
            _ = schemas.Remove(schema.GetName());
            schema.Rename(newName);
            schemas[newName] = schema;
        }

        UpdateMeta(session, schema);
        ClearQueryCache(schema.GetId());
    }

    /// <summary>Renames a schema object.</summary>
    /// <param name="session">the session</param>
    /// <param name="obj">the object</param>
    /// <param name="newName">the new name</param>
    public void RenameSchemaObject(SessionLocal session, Schemas.SchemaObject obj, string newName)
    {
        CheckWritingAllowed();
        obj.GetSchema().Rename(obj, newName);
        UpdateMeta(session, obj);
        ClearQueryCache(obj.GetId());
    }

    /// <summary>Renames a table, with the D-011 table metadata entry point.</summary>
    public void RenameSchemaObject(SessionLocal session, Tables.Table table, string newName)
    {
        CheckWritingAllowed();
        table.GetSchema()!.Rename(table, newName);
        UpdateMeta(session, table);
        foreach (DbObject child in table.GetChildren())
            if (child.GetCreateSql() is not null) UpdateMeta(session, child);
        ClearQueryCache(table.GetId());
    }

    private int nextTempTableId;

    /// <summary>Gets a unique temporary table name for an ALTER TABLE copy.</summary>
    public string GetTempTableName(string baseName, SessionLocal session)
    {
        int maxBaseLength = Constants.MaxIdentifierLength - (7 + Values.ValueInteger.DisplaySize * 2);
        if (baseName.Length > maxBaseLength) baseName = baseName[..maxBaseLength];
        string tempName;
        do
        {
            int id = System.Threading.Interlocked.Increment(ref nextTempTableId) - 1;
            tempName = baseName + "_COPY_" + session.GetId() + '_' + id;
        }
        while (GetSchema(Constants.SchemaMain).FindTableOrView(session, tempName) is not null);
        return tempName;
    }

    /// <summary>
    /// Removes the given object from the system table, the reference's <c>removeMeta</c>.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="id">the id of the object to remove</param>
    public void RemoveMeta(SessionLocal session, int id)
    {
        if (id > 0 && !starting)
        {
            Result.Row r = meta!.GetTemplateRow();
            r.SetValue(0, Values.ValueInteger.Get(id));
            bool wasLocked = LockMeta(session);
            try
            {
                Result.Row? found = FindMetaRow(session, id);
                if (found is not null)
                {
                    meta.RemoveRow(session, found.GetKey());
                }
            }
            finally
            {
                if (!wasLocked)
                {
                    // Must not keep the lock if it was not locked, otherwise updating
                    // sequences may cause a deadlock.
                    UnlockMeta(session);
                }
            }

            // Release of the object id is postponed until the end of the transaction, so it
            // cannot be reused while a rollback could still bring the object back.
            session.ScheduleDatabaseObjectIdForRelease(id);
        }
    }

    private Result.Row? FindMetaRow(SessionLocal session, int id)
    {
        Result.Row r = meta!.GetTemplateRow();
        r.SetValue(0, Values.ValueInteger.Get(id));
        Indexes.Cursor cursor = metaIdIndex!.GetCursor(session, r, r, false)
            ?? throw DbException.GetInternalError("SYS_ID.find returned no cursor");
        return cursor.Next() ? cursor.Get() : null;
    }

    /// <summary>
    /// Gets the store this database's tables live in.
    /// </summary>
    /// <returns>the store</returns>
    public MvStore.Db.Store GetStore() => store;

    /// <summary>
    /// Gets the MVStore, as the reference's getMvStore does.
    /// </summary>
    /// <returns>the MVStore</returns>
    public MvStore.MVStore GetMvStore() => store.GetMvStore();

    /// <summary>
    /// The page size of the store, from the PAGE_SIZE setting.
    /// </summary>
    private readonly int pageSize;

    /// <summary>
    /// Gets the page size of the store.
    /// </summary>
    /// <returns>the page size in bytes</returns>
    public int GetPageSize() => pageSize;

    /// <summary>
    /// Gets the compare mode this database's sessions compare with.
    /// </summary>
    /// <remarks>
    /// The reference keeps it on the database and hands it to every session; a session that
    /// invented its own would compare differently from the database it belongs to.
    /// </remarks>
    /// <returns>the compare mode</returns>
    public Values.CompareMode GetCompareMode() => compareMode;

    /// <summary>
    /// Sets the compare mode this database's sessions compare with.
    /// </summary>
    /// <param name="compareMode">the compare mode</param>
    public void SetCompareMode(Values.CompareMode compareMode) => this.compareMode = compareMode;

    /// <summary>
    /// Gets the row factory this database uses.
    /// </summary>
    /// <remarks>
    /// Reference: org.h2.engine.Database.java:416
    /// Tables and indexes build their own variants through createRowFactory.
    /// </remarks>
    /// <returns>the row factory</returns>
    public Result.RowFactory GetRowFactory() => rowFactory;

    /// <summary>
    /// Returns the current timestamp with maximum resolution.
    /// </summary>
    /// <remarks>
    /// The reference's database is the provider of the row factories, and the row factories
    /// are what compare a stored value with a value of another type — and a comparison of two
    /// date and time types has to know the zone of the reader, which is the statement that
    /// runs now. The database answers with the session of the thread, and refuses when there
    /// is none: a comparison outside a statement has no reader, and the reference refuses it
    /// the same way.
    /// </remarks>
    /// <returns>the current timestamp</returns>
    public Values.ValueTimestampTimeZone CurrentTimestamp()
    {
        SessionLocal? session = SessionLocal.GetThreadLocalSession();
        if (session is null)
        {
            throw Message.DbException.GetUnsupportedException("Unsafe comparison or cast");
        }

        return session.CurrentTimestamp();
    }

    /// <summary>
    /// Returns the current time zone.
    /// </summary>
    /// <remarks>
    /// The zone of the session of the thread, for the reason
    /// <see cref="CurrentTimestamp"/> gives.
    /// </remarks>
    /// <returns>the current time zone</returns>
    public Util.TimeZoneProvider CurrentTimeZone()
    {
        SessionLocal? session = SessionLocal.GetThreadLocalSession();
        if (session is null)
        {
            throw Message.DbException.GetUnsupportedException("Unsafe comparison or cast");
        }

        return session.CurrentTimeZone();
    }

    /// <summary>
    /// Returns are ENUM values 0-based.
    /// </summary>
    /// <returns>true if they are</returns>
    public bool ZeroBasedEnums() => DefaultCastDataProvider.Instance.ZeroBasedEnums();

    /// <summary>
    /// Gets the connection information this database was opened with.
    /// </summary>
    /// <returns>the connection information</returns>
    public ConnectionInfo GetConnectionInfo()
    {
        return connectionInfo;
    }

    /// <summary>
    /// Gets the database name.
    /// </summary>
    /// <returns>the name</returns>
    public string GetName()
    {
        return name;
    }

    /// <summary>
    /// Gets the short name of the database: the part of the name after the last separator,
    /// in upper case.
    /// </summary>
    /// <remarks>
    /// The reference's parseDatabaseShortName. The name of a database is a path or a URL
    /// like "mem:db" or "file:/tmp/db", and the catalogue names the database by the last
    /// piece of it: "DB", and "UNNAMED" when the name has no piece at all.
    /// </remarks>
    /// <returns>the short name</returns>
    public string GetShortName()
    {
        string n = name;
        int l = n.Length;
        int i = l;
        while (--i >= 0)
        {
            char ch = n[i];
            if (ch is '/' or ':' or '\\')
            {
                break;
            }
        }

        n = ++i == l ? "UNNAMED" : n[i..];
        return Util.StringUtils.TruncateString(
            Util.StringUtils.ToUpperEnglish(n), Constants.MaxIdentifierLength);
    }

    /// <summary>
    /// Whether this database is in memory.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsMemory()
    {
        return memory;
    }

    /// <summary>
    /// Whether this database is on disk.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsPersistent()
    {
        return !memory;
    }

    /// <summary>
    /// The path of the database files, or null for an in-memory database.
    /// </summary>
    /// <remarks>
    /// The reference resolves the name to a real path here (FileUtils.toRealPath). The
    /// port does not open persistent databases yet (see the constructor), so the
    /// persistent branch has nothing to resolve and refuses rather than answering with
    /// an unresolved name.
    /// </remarks>
    /// <returns>the path, or null for an in-memory database</returns>
    public string? GetDatabasePath()
    {
        if (IsPersistent())
        {
            // Reference: FileUtils.toRealPath(databaseName)
            return Store.FileSystem.FileUtils.ToRealPath(name);
        }

        return null;
    }

    /// <summary>
    /// Whether this database was opened read only.
    /// </summary>
    /// <remarks>
    /// Set from the connection string's ACCESS_MODE_DATA setting, as in the reference. A read
    /// only database answers no to every write, including spilling a result to disk, which is
    /// why the result asks this before deciding how many rows to keep in memory.
    /// </remarks>
    private volatile bool readOnly;

    /// <summary>
    /// Whether this database may not be written to.
    /// </summary>
    /// <returns>true if it is read only</returns>
    public bool IsReadOnly()
    {
        return readOnly;
    }

    /// <summary>
    /// Sets whether this database may not be written to.
    /// </summary>
    /// <param name="readOnly">the new value</param>
    public void SetReadOnly(bool readOnly)
    {
        this.readOnly = readOnly;
    }

    /// <summary>
    /// Fails when this database may not be written to.
    /// </summary>
    /// <remarks>
    /// The reference's check, ported word for word: every write path asks this before it
    /// touches the store, so that a read-only database refuses the first write rather than
    /// one somewhere in the middle.
    /// </remarks>
    public void CheckWritingAllowed()
    {
        if (readOnly)
        {
            throw DbException.Get(Api.ErrorCode.DatabaseIsReadOnly);
        }
    }

    /// <summary>
    /// Gets the number of rows a result may hold in memory before it is written to disk.
    /// </summary>
    /// <returns>the number of rows</returns>
    public int GetMaxMemoryRows() => maxMemoryRows;

    /// <summary>
    /// Sets the number of rows a result may hold in memory before it is written to disk.
    /// </summary>
    /// <param name="value">the number of rows</param>
    public void SetMaxMemoryRows(int value)
    {
        lock (syncRoot)
        {
            maxMemoryRows = value;
        }
    }

    /// <summary>
    /// Gets the first user table, or null when the database holds none.
    /// </summary>
    /// <remarks>
    /// A table counts when it has a CREATE statement: virtual tables answer null, and a
    /// database of nothing but those is empty for the code that asks.
    /// </remarks>
    /// <returns>the first user table, or null</returns>
    public Tables.Table? GetFirstUserTable()
    {
        lock (syncRoot)
        {
            foreach (Schemas.Schema schema in schemas.Values)
            {
                foreach (Tables.Table table in schema.GetAllTablesAndViews(null!))
                {
                    if (table.GetCreateSql() is not null)
                    {
                        return table;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Allocates a new object id.
    /// </summary>
    /// <remarks>
    /// The reference's body: the first clear bit of <c>objectIds</c>. Bit 0 is set before
    /// startup metadata runs, so the first object a user can name is 1, and ids restored from
    /// an existing SYS table are never handed out again.
    /// </remarks>
    /// <returns>the id</returns>
    public int AllocateObjectId()
    {
        lock (objectIds)
        {
            int id = objectIds.NextClearBit(0);
            objectIds.Set(id);
            return id;
        }
    }

    /// <summary>
    /// Hands out the next session id, as the reference's nextSessionId does.
    /// </summary>
    /// <returns>the id</returns>
    public int NextSessionId() => Interlocked.Increment(ref nextSessionId);

    /// <summary>Counter for session ids.</summary>
    private int nextSessionId;

    /// <summary>
    /// Gets the current modification meta id.
    /// </summary>
    /// <remarks>
    /// An atomic read, as the reference's AtomicLong get: this runs on every cache lookup
    /// and every entry point check, where a monitor would serialize all sessions.
    /// </remarks>
    /// <returns>the id</returns>
    public long GetModificationMetaId()
    {
        return Interlocked.Read(ref modificationMetaId);
    }

    /// <summary>
    /// Gets the next modification meta id, marking the database as changed.
    /// </summary>
    /// <remarks>
    /// The id handed out is the one before the increment, as the reference's
    /// incrementAndGet minus one. The data id the reference bumps alongside waits for
    /// the meta table: nothing ported reads it yet.
    /// </remarks>
    /// <returns>the id</returns>
    public long GetNextModificationMetaId()
    {
        return Interlocked.Increment(ref modificationMetaId) - 1;
    }

    /// <summary>
    /// Gets the trace object for the given module.
    /// </summary>
    /// <remarks>
    /// Refuses for now, because the reference's trace is a module id plus a writer and this
    /// assembly has the interface but no writer yet (the reference's TraceSystem is not ported).
    /// Returning a trace that writes nowhere would be a working trace that tells nobody
    /// anything, which is worse than refusing: a caller that debugs with it would find silence
    /// where the reference writes lines.
    /// </remarks>
    /// <param name="module">the module name</param>
    /// <returns>the trace</returns>
    public Trace GetTrace(string module)
        => throw new TODOException("Database.getTrace: no TraceWriter is ported yet");

    /// <summary>
    /// Adds a session, so the database knows it is in use.
    /// </summary>
    /// <param name="session">the session</param>
    public void AddSession(SessionLocal session)
    {
        lock (syncRoot)
        {
            sessions.Add(session);
        }
    }

    /// <summary>
    /// Gets the open sessions.
    /// </summary>
    /// <returns>a snapshot of the sessions</returns>
    public SessionLocal[] GetSessions()
    {
        lock (syncRoot)
        {
            return [.. sessions];
        }
    }

    /// <summary>
    /// Called when the database has been opened, to start whatever it runs in the background.
    /// </summary>
    public void Opened()
    {
    }

    /// <summary>
    /// Unlocks the meta data of the database.
    /// </summary>
    /// <remarks>
    /// Paired with <see cref="LockMeta"/>. Neither locks anything yet: the meta table is new and
    /// the reference guards it with its own monitor, which the port has not ported.
    /// </remarks>
    /// <param name="session">the session</param>
    public void UnlockMeta(SessionLocal session)
    {
    }

    /// <summary>
    /// Mark some database ids as unused.
    /// </summary>
    /// <param name="idsToRelease">the ids to release</param>
    internal void ReleaseDatabaseObjectIds(BitSet idsToRelease)
    {
        lock (objectIds)
        {
            objectIds.AndNot(idsToRelease);
        }
    }

    /// <summary>
    /// Gets the number of open sessions.
    /// </summary>
    /// <returns>the number of sessions</returns>
    public int GetSessionCount()
    {
        lock (syncRoot)
        {
            return sessions.Count;
        }
    }

    /// <summary>
    /// Removes a session, and closes the database when it was the last one.
    /// </summary>
    /// <remarks>
    /// Reference removeSession: a close delay of 0 closes the database at once, a negative
    /// one keeps it open, and a positive one schedules a delayed close. The delayed close
    /// needs its own timer, which this port does not have yet, so a positive value refuses
    /// rather than closing at the wrong moment.
    /// </remarks>
    /// <param name="session">the session to remove</param>
    /// <returns>whether the database was closed</returns>
    public bool RemoveSession(SessionLocal session)
    {
        lock (syncRoot)
        {
            _ = sessions.Remove(session);
            if (sessions.Count == 0)
            {
                if (GetCloseDelay() == 0)
                {
                    Close();
                    return true;
                }

                if (GetCloseDelay() < 0)
                {
                    return false;
                }

                throw new TODOException("Database.removeSession: delayed closing is not ported");
            }
        }

        return false;
    }

    /// <summary>
    /// Whether this database is closing or closed.
    /// </summary>
    /// <remarks>
    /// A plain read, as the reference reads its own field: this runs on every entry point
    /// check, where a monitor would serialize all sessions.
    /// </remarks>
    /// <returns>true if it is</returns>
    public bool IsClosing()
    {
        return closing;
    }

    /// <summary>
    /// Closes the database.
    /// </summary>
    public void Close()
    {
        bool alreadyClosed;
        lock (syncRoot)
        {
            alreadyClosed = closing;
            if (!alreadyClosed)
            {
                closing = true;
            }
        }

        if (!alreadyClosed)
        {
            // Database.close: sequences flush their exact next value before the store closes.
            if (systemSession is not null && !readOnly)
            {
                foreach (Schemas.Schema schema in GetSchemas())
                    foreach (Schemas.Sequence sequence in schema.GetAllSequences()) sequence.Close();
                systemSession.Commit(true);
            }
            // Reference: store.close(allowedCompactionTime). Nothing in the port asks for
            // compaction on close, so the store is closed immediately.
            store.Close(0);
            Engine.RemoveClosed(this);
        }
    }

    /// <summary>
    /// Throws if the database is closed.
    /// </summary>
    public void CheckClosed()
    {
        if (IsClosing())
        {
            throw DbException.Get(Api.ErrorCode.DatabaseIsClosed, name);
        }
    }
}


