/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.SessionLocal.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A session, to the extent the connection path needs it yet: it knows its database, it counts
 * as a user of that database, and closing it gives the database back. Everything that answers a
 * question about SQL — the current timestamp, the identity, preparing a command — is stubbed and
 * refuses, because a session that answered those without the engine behind it would produce
 * rows that look right and are stamped wrong.
 */
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;
using H2Sharp.Api;
using H2Sharp.Command;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Engine;

/// <summary>
/// A session in a local (in-process) database.
/// </summary>
public sealed class SessionLocal : Session
{
    private readonly Database database;

    private readonly string userName;

    private bool closed;

    /// <summary>
    /// Allocates an object id that is unique within this session.
    /// </summary>
    /// <remarks>
    /// Not the database's <c>AllocateObjectId</c>: those are for objects that live in the
    /// catalogue, and this one is for things that live inside a statement — table filters. A
    /// filter's hash is its identity among the query's own filters, and it must not spend an id
    /// a catalogue object would need.
    ///
    /// Atomic, although the reference's own counter is not. The reference touches it under the
    /// session lock, which this port does not hold everywhere the counter is needed — and two
    /// filters with one hash would be one filter too few for the query that holds them both.
    /// </remarks>
    /// <returns>the next id</returns>
    public int NextObjectId() => Interlocked.Increment(ref objectId) - 1;

    private int objectId;

    private Result.SearchRow? currentRow;

    private readonly Dictionary<string, Values.Value> parameters = [];

    /// <summary>
    /// Gets the transaction this session is working on, or null when it has none.
    /// </summary>
    /// <remarks>
    /// The reference's <c>Session.getTransaction()</c>. The transaction belongs to the session
    /// rather than to the caller that happens to be writing: a table that took one from its
    /// argument would read and write on a different transaction than the statement is on, and the
    /// row it wrote would be invisible to the statement that wrote it.
    /// </remarks>
    public MvStore.Tx.Transaction? Transaction { get; set; }

    /// <summary>
    /// The scope that owns the transaction above, when one is open. A table restores it when its
    /// statement is done, so that nested work on the same session does not lose it.
    /// </summary>
    internal object? TransactionScope { get; set; }

    /// <summary>
    /// Gets the value of a column of the row the session is reading.
    /// </summary>
    /// <remarks>
    /// The reference reads a column through the <c>ColumnResolver</c> the session carries, and
    /// that is the same thing by another name here: one row at a time, whichever part of the
    /// query asks for it. Reading a column with no row being read is a failure and not a NULL —
    /// a column read outside a scan is a mistake in the query, and answering NULL would hide it.
    /// </remarks>
    /// <param name="column">the column</param>
    /// <returns>the value</returns>
    public Values.Value GetRowValue(Tables.Column column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return currentRow is null
            ? throw new TODOException("SessionLocal.getRowValue: no row is being read")
            : currentRow.GetValue(column.GetColumnId())!;
    }

    /// <summary>
    /// Sets the row that column expressions read.
    /// </summary>
    /// <param name="row">the row, or null between rows</param>
    public void SetCurrentRow(Result.SearchRow? row)
    {
        currentRow = row;
    }

    /// <summary>Gets the parameter values of this session, by name.</summary>
    public IReadOnlyDictionary<string, Values.Value> Parameters => parameters;

    /// <summary>
    /// Sets the value of a parameter, by name.
    /// </summary>
    /// <param name="name">the parameter name</param>
    /// <param name="value">the value</param>
    public void SetParameter(string name, Values.Value value)
    {
        ArgumentNullException.ThrowIfNull(name);
        parameters[name] = value;
    }

    /// <summary>
    /// Gets the value of a parameter, or NULL when none was set.
    /// </summary>
    /// <param name="name">the parameter name</param>
    /// <returns>the value</returns>
    public Values.Value GetParameterValue(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return parameters.TryGetValue(name, out Values.Value? value) ? value : Values.ValueNull.Instance;
    }

    /// <summary>
    /// Whether a value was set for the parameter.
    /// </summary>
    /// <param name="name">the parameter name</param>
    /// <returns>true if it was set</returns>
    public bool IsParameterSet(string name)
    {
        return parameters.ContainsKey(name);
    }

    /// <summary>
    /// Gets where casts come from.
    /// </summary>
    public CastDataProvider CastDataProvider { get; init; } = null!;

    /// <summary>
    /// Gets how values are compared.
    /// </summary>
    public Values.CompareMode CompareMode { get; init; } = null!;

    /// <summary>
    /// Opens a session on the given database.
    /// </summary>
    /// <param name="database">the database</param>
    /// <param name="userName">the user name</param>
    public SessionLocal(Database database, string userName)
    {
        this.database = database;
        this.userName = userName;
        Schemas.Schema? main = database.GetMainSchema();
        currentSchemaName = main is not null ? main.GetName() : H2Sharp.Engine.Constants.SchemaMain;
        database.AddSession(this);
    }

    /// <summary>
    /// Gets the database this session belongs to.
    /// </summary>
    /// <returns>the database</returns>
    public Database GetDatabase()
    {
        return database;
    }

    /// <summary>
    /// Gets the user name.
    /// </summary>
    /// <returns>the user name</returns>
    public string GetUserName()
    {
        return userName;
    }

    /// <summary>
    /// Whether this session is closed.
    /// </summary>
    /// <returns>true if it is</returns>
    public override bool IsClosed()
    {
        return closed;
    }

    /// <summary>
    /// Closes the session, and the database with it when it was the last session.
    /// </summary>
    public override void Close()
    {
        if (closed)
        {
            return;
        }

        closed = true;
        _ = database.RemoveSession(this);
    }

    /// <summary>
    /// Commits the current transaction.
    /// </summary>
    /// <remarks>
    /// A no-op, and deliberately so: with nothing prepared there is nothing to commit, and a
    /// commit that reported rows would be reporting an invention. It becomes the real thing
    /// with the transaction layer.
    /// </remarks>
    public void Commit()
    {
    }

    /// <summary>
    /// Rolls the current transaction back.
    /// </summary>
    public void Rollback()
    {
    }

    /// <summary>
    /// Gets the column resolver for the given table alias.
    /// </summary>
    /// <param name="alias">the table alias</param>
    /// <returns>the resolver, or null when this query has no table by that alias</returns>
    public H2Sharp.Tables.ColumnResolver? GetResolver(string alias)
        => throw new TODOException("SessionLocal.getResolver: the query layer is not ported");

    /// <summary>
    /// Registers an index of a session-local temporary table.
    /// </summary>
    /// <remarks>
    /// Not ported. It keeps the index by name and refuses a duplicate, which needs the
    /// session's temporary-table registry — and there is no temporary-table lifecycle here
    /// yet to register against.
    /// </remarks>
    /// <param name="index">the index</param>
    public void AddLocalTempTableIndex(H2Sharp.Indexes.Index index)
        => throw new TODOException("SessionLocal.addLocalTempTableIndex");

    /// <summary>
    /// Compares two values, answering the SQL rule for NULL: a comparison with NULL is
    /// unknown unless the mode says otherwise, and unknown sorts before everything.
    /// </summary>
    /// <param name="left">the left value</param>
    /// <param name="right">the right value</param>
    /// <param name="ignoreCase">whether case is ignored</param>
    /// <returns>-1, 0 or 1</returns>
    public int CompareWithNull(Values.Value left, Values.Value right, bool ignoreCase)
        => throw new TODOException("SessionLocal.compareWithNull");

    /// <summary>
    /// Compares two values with the current comparison mode. The values may have different data
    /// types, including NULL.
    /// </summary>
    /// <remarks>
    /// The session is the one that knows how to compare, so it is passed to the value: a value
    /// that had to convert the other to its own type would answer wrongly for two different
    /// types. It is also what the reference hands a TreeMap as its comparator, which is how rows
    /// are found in a DISTINCT result.
    /// </remarks>
    /// <param name="left">the left value</param>
    /// <param name="right">the right value</param>
    /// <returns>0 if both values are equal, -1 if the first value is smaller, 1 otherwise</returns>
    public int Compare(Values.Value left, Values.Value right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.CompareTo(right, this.CastDataProvider, GetCompareMode());
    }

    /// <summary>
    /// Gets how this session compares values.
    /// </summary>
    /// <returns>the compare mode</returns>
    public Values.CompareMode GetCompareMode()
    {
        return CompareMode;
    }

    /// <summary>
    /// Whether two values are equal, where NULL equals NULL.
    /// </summary>
    /// <remarks>
    /// Not Equals: 0.0 is not equal to 0.00, and a distinct result that answered that question
    /// with Equals would keep both and drop nothing.
    /// </remarks>
    /// <param name="left">the left value</param>
    /// <param name="right">the right value</param>
    /// <returns>true if they are equal</returns>
    public bool AreEqual(Values.Value left, Values.Value right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.CompareTo(right, this, GetCompareMode()) == 0;
    }

    /// <summary>
    /// Gets the database mode, which decides how identifiers and aliases are spelled.
    /// </summary>
    public override Mode GetMode()
        => throw new TODOException("SessionLocal.getMode: Mode is not ported");

    /// <summary>
    /// Prepares a command for execution.
    /// </summary>
    /// <remarks>
    /// The reference reads the first word and hands the text to the command for that word, which
    /// parses it itself: <c>prepareCommand</c> is a dispatch over statement kinds, not a
    /// statement tree. Those commands — <c>Insert</c>, <c>Select</c>, <c>CreateTable</c> — are
    /// not ported yet, so this refuses. It says so rather than answering with a parsed tree of
    /// its own shape, because a caller would then depend on a shape the reference does not have.
    /// </remarks>
    /// <param name="sql">the SQL</param>
    /// <returns>the prepared command</returns>
    public override H2Sharp.Command.CommandInterface PrepareCommand(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        // First, and before anything else: a session that has been closed prepares nothing. This
        // is the reference's first line of prepareLocal, and it is first for a reason — every
        // step below it, the cache included, would otherwise hand a closed session a command it
        // could not run and leave the failure somewhere further along.
        if (IsClosed())
        {
            throw DbException.Get(Api.ErrorCode.ConnectionBroken1, "session closed");
        }

        // The reference reads the statement and wraps it in a CommandContainer here. Same three
        // steps, and the cache around them is where a repeated statement is meant to become
        // cheap: a command is only handed back once it has been closed, so nothing that is still
        // in use can be run twice underneath a caller.
        H2Sharp.Command.Command? cached = null;
        if (queryCacheSize > 0)
        {
            if (queryCache is null)
            {
                queryCache = SmallLRUCache<string, H2Sharp.Command.Command>.NewInstance(queryCacheSize);
                modificationMetaId = GetDatabase().GetModificationMetaId();
            }
            else
            {
                // A change of any kind invalidates the whole cache. This is blunt on purpose:
                // tracking which object each cached command depended on is the finer rule, and
                // dropping everything is the one that cannot be wrong about a dependency it
                // failed to notice.
                long newModificationMetaId = GetDatabase().GetModificationMetaId();
                if (newModificationMetaId != modificationMetaId)
                {
                    queryCache.Clear();
                    modificationMetaId = newModificationMetaId;
                }

                if (queryCache.TryGetValue(sql, out H2Sharp.Command.Command? found) && found.CanReuse())
                {
                    cached = found;
                }
            }
        }

        if (cached is not null)
        {
            cached.Reuse();
            return cached;
        }
        H2Sharp.Command.Command command = new H2Sharp.Command.Parser(this).PrepareCommand(sql);

        if (queryCache is not null && command.IsCacheable())
        {
            _ = queryCache.Put(sql, command);
        }

        return command;
    }

    /// <summary>
    /// The prepared commands this session keeps, keyed by their text.
    /// </summary>
    /// <remarks>
    /// A statement is parsed once and run many times, so the tree is worth keeping — and keeping
    /// it is only safe while nothing it read has changed. That is what the modification id is
    /// for: the whole cache is dropped the moment the database reports a change, which is blunt
    /// but correct, and is cheaper than tracking which object each cached command depended on.
    /// </remarks>
    private SmallLRUCache<string, H2Sharp.Command.Command>? queryCache;

    private long modificationMetaId;

    /// <summary>
    /// How many commands this session keeps, or zero for none.
    /// </summary>
    /// <remarks>
    /// The reference reads this from the database settings (QUERY_CACHE_SIZE, default 8) when the
    /// session opens, because a setting changed after the database started must not change a
    /// session that is already running. The settings object is not ported, so the default is
    /// written here.
    /// </remarks>
    private readonly int queryCacheSize = QueryCacheSize;

    private const int QueryCacheSize = 8;

    /// <summary>
    /// Gets the current timestamp of this session.
    /// </summary>
    public Values.ValueTimestamp GetCurrentTimestamp()
        => throw new TODOException("SessionLocal.getCurrentTimestamp");

    /// <summary>
    /// Whether this session is in autocommit mode.
    /// </summary>
    public override bool GetAutoCommit()
        => throw new TODOException("SessionLocal.getAutoCommit");

    /// <summary>
    /// Checks whether this session may read the given table.
    /// </summary>
    /// <param name="table">the table</param>
    public void CheckReadRight(H2Sharp.Tables.Table? table)
        => throw new TODOException("SessionLocal.checkReadRight");

    /// <summary>
    /// Checks whether this session may modify the given table.
    /// </summary>
    /// <param name="table">the table</param>
    public void CheckRight(H2Sharp.Tables.Table? table)
        => throw new TODOException("SessionLocal.checkRight");

    /// <summary>
    /// Gets the data handler of this session's database.
    /// </summary>
    /// <returns>the data handler</returns>
    public override H2Sharp.Store.DataHandler GetDataHandler()
        => throw new TODOException("SessionLocal.getDataHandler: Database.getDataHandler is not ported");

    /// <summary>
    /// Gets the names of the cluster servers.
    /// </summary>
    /// <returns>the cluster servers</returns>
    public override List<string> GetClusterServers()
        => throw new TODOException("SessionLocal.getClusterServers");

    /// <inheritdoc/>
    public override H2Sharp.Message.Trace GetTrace()
        => throw new TODOException("SessionLocal.getTrace: the trace system has no writer yet");

    /// <summary>
    /// Whether this session has a pending transaction.
    /// </summary>
    /// <returns>true if it has</returns>
    public override bool HasPendingTransaction()
        => throw new TODOException("SessionLocal.hasPendingTransaction");

    /// <summary>
    /// Cancels the statement being run.
    /// </summary>
    public override void Cancel()
        => throw new TODOException("SessionLocal.cancel");

    /// <summary>
    /// Sets whether each statement commits by itself.
    /// </summary>
    /// <param name="autoCommit">the new value</param>
    public override void SetAutoCommit(bool autoCommit)
        => throw new TODOException("SessionLocal.setAutoCommit");

    /// <summary>
    /// Adds a lob that lives as long as this session does.
    /// </summary>
    /// <param name="v">the value</param>
    /// <returns>the value that was added</returns>
    public override Values.ValueLob AddTemporaryLob(Values.ValueLob v)
        => throw new TODOException("SessionLocal.addTemporaryLob");

    /// <summary>
    /// Whether this is a remote session. It is not: there is no wire protocol in this port.
    /// </summary>
    /// <returns>false</returns>
    public override bool IsRemote() => false;

    /// <summary>
    /// Sets the schema that unqualified names are read from.
    /// </summary>
    /// <remarks>
    /// The reference stores the *name*, not the schema, and that is deliberate: the schema may
    /// be dropped and recreated while this session is open, and a session holding the old object
    /// would go on using a schema that no longer exists. The name is looked up when it is needed.
    /// </remarks>
    /// <param name="schema">the schema name</param>
    public override void SetCurrentSchemaName(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        currentSchemaName = GetDatabase().GetSchema(schema).GetName();
    }

    /// <summary>
    /// Gets the schema that unqualified names are read from.
    /// </summary>
    /// <returns>the schema name</returns>
    public override string GetCurrentSchemaName()
    {
        return currentSchemaName;
    }

    /// <summary>
    /// Gets the schema that unqualified names are read from.
    /// </summary>
    /// <returns>the schema</returns>
    public H2Sharp.Schemas.Schema GetCurrentSchema()
    {
        return GetDatabase().GetSchema(currentSchemaName);
    }

    /// <summary>
    /// Sets the schema by object.
    /// </summary>
    /// <param name="schema">the schema</param>
    public void SetCurrentSchema(H2Sharp.Schemas.Schema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        currentSchemaName = schema.GetName();
    }

    /// <summary>
    /// The name of the schema unqualified names are read from.
    /// </summary>
    /// <remarks>
    /// The main schema of the database, or the system identifier for it. It is a *name* and not
    /// a schema on purpose, and the reason is written on <see cref="SetCurrentSchemaName"/>.
    /// </remarks>
    private string currentSchemaName;

    /// <summary>
    /// Sets the network connection information.
    /// </summary>
    /// <param name="networkConnectionInfo">the connection information</param>
    public override void SetNetworkConnectionInfo(object networkConnectionInfo)
        => throw new TODOException("SessionLocal.setNetworkConnectionInfo");

    /// <summary>
    /// Gets the isolation level of the transaction.
    /// </summary>
    /// <returns>the isolation level</returns>
    public override int GetIsolationLevel()
        => throw new TODOException("SessionLocal.getIsolationLevel");

    /// <summary>
    /// Sets the isolation level of the transaction.
    /// </summary>
    /// <param name="isolationLevel">the isolation level</param>
    public override void SetIsolationLevel(int isolationLevel)
        => throw new TODOException("SessionLocal.setIsolationLevel");

    /// <summary>
    /// Gets the settings that do not change while the database is open.
    /// </summary>
    /// <returns>the settings</returns>
    public override object GetStaticSettings()
        => throw new TODOException("SessionLocal.getStaticSettings");

    /// <summary>
    /// Gets the settings that may change while the database is open.
    /// </summary>
    /// <returns>the settings</returns>
    public override object GetDynamicSettings()
        => throw new TODOException("SessionLocal.getDynamicSettings");

    /// <summary>
    /// Gets the object that builds the metadata of this database.
    /// </summary>
    /// <returns>the meta object</returns>
    public override object GetDatabaseMeta()
        => throw new TODOException("SessionLocal.getDatabaseMeta");

    /// <summary>
    /// Whether this is an old information schema.
    /// </summary>
    /// <returns>true if it is</returns>
    public override bool IsOldInformationSchema()
        => throw new TODOException("SessionLocal.isOldInformationSchema");

    /// <summary>
    /// Gets the current timestamp, which is fixed for the length of one statement or
    /// transaction.
    /// </summary>
    /// <returns>the current timestamp</returns>
    public override Values.ValueTimestampTimeZone CurrentTimestamp()
        => CastDataProvider.CurrentTimestamp();

    /// <summary>
    /// Gets the current time zone.
    /// </summary>
    /// <returns>the time zone</returns>
    public override Util.TimeZoneProvider CurrentTimeZone()
        => CastDataProvider.CurrentTimeZone();

    /// <summary>
    /// Whether ENUM values are 0-based.
    /// </summary>
    /// <returns>true if they are</returns>
    public override bool ZeroBasedEnums()
        => CastDataProvider.ZeroBasedEnums();
}
