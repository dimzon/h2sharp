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
    private Schemas.Schema? mainSchema;

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
    public Schemas.Schema? GetMainSchema()
    {
        lock (syncRoot)
        {
            return mainSchema;
        }
    }

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
    /// How many rows a result may hold in memory before it spills. The reference's default,
    /// from its MAX_MEMORY_ROWS setting.
    /// </summary>
    private int maxMemoryRows = 40_000;

    /// <summary>
    /// Where a NULL goes when a statement did not say. LOW, because the reference starts there
    /// and every answer about ordering before a caller sets it comes from this field.
    /// </summary>
    private DefaultNullOrdering defaultNullOrdering = DefaultNullOrdering.LOW;

    /// <summary>
    /// Gets where a NULL goes when a statement did not say.
    /// </summary>
    /// <returns>the ordering</returns>
    public DefaultNullOrdering GetDefaultNullOrdering()
    {
        lock (syncRoot)
        {
            return defaultNullOrdering;
        }
    }

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

        // The main schema exists from the moment the database does, before anybody connects. A
        // session opening against a database with no schema would have nothing to create its
        // first object in, and the reference names PUBLIC here rather than making the first
        // statement do it.
        Schemas.Schema main = new(this, AllocateObjectId(), Constants.SchemaMain, system: false);
        AddSchema(main);
        SetMainSchema(main);
    }

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
    /// Whether this database may not be written to.
    /// </summary>
    /// <remarks>
    /// A database can be opened read only from the connection string, and nothing writes to it
    /// while that holds — including spilling a result to disk, which is why the result asks this
    /// before deciding how many rows to keep in memory. The flag is not ported from the
    /// connection string yet, so it refuses rather than answering "no" for every database: a
    /// caller that believed it was writable would spill results nobody had asked it to.
    /// </summary>
    /// <returns>true if it is read only</returns>
    public bool IsReadOnly()
        => throw new TODOException("Database.isReadOnly: the connection string setting is not ported");

    /// <summary>
    /// Gets the number of rows a result may hold in memory before it is written to disk.
    /// </summary>
    /// <returns>the number of rows</returns>
    public int GetMaxMemoryRows()
    {
        lock (syncRoot)
        {
            return maxMemoryRows;
        }
    }

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
    /// Allocates a new object id.
    /// </summary>
    /// <returns>the id</returns>
    public int AllocateObjectId()
    {
        lock (syncRoot)
        {
            return objectId++;
        }
    }

    /// <summary>
    /// Gets the current modification meta id.
    /// </summary>
    /// <returns>the id</returns>
    public long GetModificationMetaId()
    {
        lock (syncRoot)
        {
            return modificationMetaId;
        }
    }

    /// <summary>
    /// Gets the next modification meta id, marking the database as changed.
    /// </summary>
    /// <returns>the id</returns>
    public long GetNextModificationMetaId()
    {
        lock (syncRoot)
        {
            return ++modificationMetaId;
        }
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
    /// <returns>true if it is</returns>
    public bool IsClosing()
    {
        lock (syncRoot)
        {
            return closing;
        }
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
