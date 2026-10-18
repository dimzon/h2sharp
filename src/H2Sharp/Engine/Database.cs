/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.Database.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Partially ported, in the order the connection path needs: what a database *is* while there is
 * nothing in it yet — its name, whether it is memory, the object id counter, and the rule that
 * an in-memory database disappears when its last session closes. Everything that needs the
 * storage layer (the store, the meta table, the writer thread, the object registry lookup) is
 * stubbed and refuses.
 *
 * The close rule is the part worth having first, because it is observable without a single
 * table: two connections to the same "jdbc:h2:mem:x" see one database, and after the second of
 * them closes, a new connection finds nothing. A port that kept the registry entry forever would
 * still answer every query correctly and would leak a database per name for the life of the
 * process — which is exactly the kind of thing a test that only creates tables never notices.
 *
 * Object ids are allocated here and nowhere else, and the counter starts where the reference's
 * does. A database whose first user takes id 0 and whose first table takes a later one is what
 * the reference produces; a port that starts the counter at 1 would agree on every query and
 * disagree with every file written from it.
 */
using System;
using System.Collections.Generic;
using System.Threading;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Engine;

/// <summary>
/// A database.
/// </summary>
public sealed partial class Database
{
    /// <summary>
    /// The first object id handed out. The reference allocates the meta table and its index
    /// before anything a user can name, so user objects start above them.
    /// </summary>
    private const int FirstObjectId = 3;

    private readonly ConnectionInfo connectionInfo;

    private readonly string name;

    private readonly bool memory;

    private readonly List<SessionLocal> sessions = [];

    private readonly object syncRoot = new();

    private int objectId;

    private long modificationMetaId;

    /// <summary>
    /// The store this database's tables live in.
    /// </summary>
    /// <remarks>
    /// The reference's Database owns its MVStore, and every table opens its maps on it: a
    /// table that brought its own store would be invisible to every transaction but its own.
    /// File-backed databases are refused until the file path mapping lands; opening them on
    /// an anonymous in-memory store would answer correctly and lose everything on close.
    /// </remarks>
    private readonly MvStore.Tx.TransactionStore store;

    /// <summary>
    /// The compare mode this database's sessions compare with, as in the reference.
    /// </summary>
    private Values.CompareMode compareMode = Values.CompareMode.GetInstance(null, 0);

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
    /// The schema new objects go into when the session has not chosen another.
    /// </summary>
    /// <remarks>
    /// Volatile rather than locked on read: a lone reference reads atomically, and the lock
    /// stays on the write, so every read sees a schema the write published — the same answers
    /// without blocking sessions that only resolve names.
    /// </remarks>
    private volatile Schemas.Schema? mainSchema;

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
    /// Construction leaves it cleared: there is no open lifecycle here yet to set it while
    /// running, so every database reads as already open.
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
    /// There is no meta table here yet to lock, so this answers locked, as the reference does
    /// when there is nothing to guard. When the meta table arrives, this delegates to it.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <returns>true</returns>
    public bool LockMeta(SessionLocal session)
    {
        return true;
    }

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
        => AddSchemaObject(session, obj.GetId(), () => obj.GetSchema().Add(obj));

    /// <summary>
    /// Adds a table to the database.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="table">the table to add</param>
    public void AddSchemaObject(SessionLocal session, Tables.Table table)
        => AddSchemaObject(session, table.GetId(), () => table.GetSchema()!.Add(table));

    private void AddSchemaObject(SessionLocal session, int id, Action add)
    {
        if (id > 0 && !starting)
        {
            CheckWritingAllowed();
        }

        LockMeta(session);
        lock (syncRoot)
        {
            add();

            // addMeta is deferred with the meta table.
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
            obj.RemoveChildrenAndResources);

    /// <summary>
    /// Removes a table from the database.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="table">the table to remove</param>
    public void RemoveSchemaObject(SessionLocal session, Tables.Table table)
        => RemoveSchemaObject(session, table.GetId(), () => table.GetSchema()!.Remove(table),
            table.RemoveChildrenAndResources);

    private void RemoveSchemaObject(
        SessionLocal session,
        int id,
        Action remove,
        Action<SessionLocal> removeChildrenAndResources)
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

            // removeMeta is deferred with the meta table.
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
        name = connectionInfo.GetName();
        memory = !connectionInfo.IsPersistent();
        objectId = FirstObjectId;

        string accessModeData = Util.StringUtils.ToLowerEnglish(
            connectionInfo.GetProperty("ACCESS_MODE_DATA") ?? "rw");
        if (accessModeData == "r")
        {
            readOnly = true;
        }

        // The main schema exists from the moment the database does, before anybody connects. A
        // session opening against a database with no schema would have nothing to create its
        // first object in, and the reference names PUBLIC here rather than making the first
        // statement do it.
        Schemas.Schema main = new(this, AllocateObjectId(), Constants.SchemaMain, system: false);
        AddSchema(main);
        SetMainSchema(main);

        if (!memory)
        {
            throw new TODOException("Database: persistent databases are not opened yet");
        }

        MvStore.MVStore mvStore = MvStore.MVStore.Open(null!);
        store = new MvStore.Tx.TransactionStore(mvStore);
        store.Init();
    }

    /// <summary>
    /// Gets the store this database's tables live in.
    /// </summary>
    /// <returns>the transaction store</returns>
    public MvStore.Tx.TransactionStore GetStore() => store;

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
    /// <returns>the id</returns>
    public int AllocateObjectId() => Interlocked.Increment(ref objectId) - 1;

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
    /// Called when the database has been opened, to start whatever it runs in the background.
    /// </summary>
    public void Opened()
    {
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
    /// The reference closes an in-memory database here rather than waiting for a shutdown, and
    /// that is what makes "mem:" a name for a lifetime rather than for a file's lifetime. The
    /// persistent case keeps the database open, which is why a file database can be closed and
    /// reopened with its contents intact.
    /// </remarks>
    /// <param name="session">the session to remove</param>
    /// <returns>whether the database was closed</returns>
    public bool RemoveSession(SessionLocal session)
    {
        lock (syncRoot)
        {
            _ = sessions.Remove(session);
            if (sessions.Count == 0 && memory)
            {
                closing = true;
                Close();
                return true;
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
        lock (syncRoot)
        {
            if (closing)
            {
                return;
            }

            closing = true;
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
