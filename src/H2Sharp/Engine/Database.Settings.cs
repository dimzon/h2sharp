/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.Database (the settings half: the SET statement's
 * fields, the Setting registry, the exclusive session and populateInfo).
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What is left out, and why:
 *
 * * checkAdmin on every setter's caller. The port has no users and no rights, so
 *   there is nobody to ask; the reference's call sits in Set.update, and its
 *   absence is recorded there.
 * * queryStatisticsData. It is a queue of measured queries, not a setting: the
 *   flag and the entry count are settings and are here, the measurements
 *   themselves are not ported.
 * * The DbSettings half of populateInfo. The port has no DbSettings (the
 *   h2.* system-property-backed table of engine knobs), so the SETTINGS table
 *   shows the named settings and not that table.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Api;
using H2Sharp.Command.Dml;
using H2Sharp.Message;
using H2Sharp.Tables;

namespace H2Sharp.Engine;

/// <summary>
/// The settings half of the database: what SET writes and INFORMATION_SCHEMA.SETTINGS reads.
/// </summary>
public sealed partial class Database
{
    private readonly Dictionary<string, Setting> settings = new(StringComparer.OrdinalIgnoreCase);

    private volatile SessionLocal? exclusiveSession;

    private string cluster = Constants.ClusteringDisabled;

    private int lockMode;

    private int maxLengthInplaceLob;

    private int allowLiterals = Constants.AllowLiteralsAll;

    private volatile int closeDelay;

    private bool ignoreCase;

    private bool optimizeReuseResults = true;

    private bool referentialIntegrity = true;

    private Mode mode = Mode.GetRegular();

    private int maxOperationMemory = Constants.DefaultMaxOperationMemory;

    private int defaultTableType = Table.TYPE_CACHED;

    private bool allowBuiltinAliasOverride;

    private volatile bool queryStatistics;

    private int queryStatisticsMaxEntries = Constants.QueryStatisticsMaxEntries;

    private bool ignoreCatalogs;

    private string? javaObjectSerializerName;

    /// <summary>The data-change counter the SETTINGS table and the meta layer read.</summary>
    private long modificationDataId;

    /// <summary>
    /// Get the data modification id of the database.
    /// </summary>
    /// <returns>the modification id</returns>
    public long GetModificationDataId()
        => System.Threading.Interlocked.Read(ref modificationDataId);

    /// <summary>
    /// Get the next data modification id, bumping the counter.
    /// </summary>
    /// <returns>the new modification id</returns>
    public long GetNextModificationDataId()
        => System.Threading.Interlocked.Increment(ref modificationDataId);

    /// <summary>
    /// Get the setting if it exists, or null if not.
    /// </summary>
    /// <param name="name">the name of the setting</param>
    /// <returns>the setting or null</returns>
    public Setting? FindSetting(string name)
    {
        return settings.GetValueOrDefault(name);
    }

    /// <summary>
    /// Get all settings.
    /// </summary>
    /// <returns>the settings</returns>
    public ICollection<Setting> GetAllSettings()
    {
        return settings.Values;
    }

    /// <summary>
    /// Add a database object to the registry.
    /// </summary>
    /// <remarks>
    /// The reference's one addDatabaseObject for every object type; here only a Setting can
    /// arrive, because users, roles and rights are not ported and schema objects go through
    /// AddSchemaObject. The meta-table write after the registry write is deferred with the meta
    /// table, as in AddSchemaObject.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="obj">the object to add</param>
    public void AddDatabaseObject(SessionLocal session, Setting obj)
    {
        int id = obj.GetId();
        if (id > 0 && !starting)
        {
            CheckWritingAllowed();
        }

        string name = obj.GetName()!;
        if (settings.ContainsKey(name))
        {
            throw DbException.GetInternalError("object already exists");
        }

        LockMeta(session);
        lock (syncRoot)
        {
            settings[name] = obj;
            AddMeta(session, obj);
        }

        ClearQueryCache(id);
    }

    /// <summary>
    /// Rename a setting, keeping the registry in step.
    /// </summary>
    /// <remarks>
    /// The reference keeps every object type in its own map and re-keys it in rename; only a
    /// Setting can be renamed here, and nothing in the port does yet.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="obj">the setting</param>
    /// <param name="newName">the new name</param>
    public void RenameDatabaseObject(SessionLocal session, Setting obj, string newName)
    {
        CheckWritingAllowed();
        LockMeta(session);
        lock (syncRoot)
        {
            string name = obj.GetName()!;
            if (!name.Equals(newName, StringComparison.OrdinalIgnoreCase))
            {
                _ = settings.Remove(name);
                obj.Rename(newName);
                settings[newName] = obj;
            }

            UpdateMeta(session, obj);
        }
    }

    /// <summary>
    /// Update a setting the meta layer knows about.
    /// </summary>
    /// <remarks>
    /// The reference's updateMeta writes the meta row; the meta table is not ported, so the
    /// query-cache invalidation is the whole effect. Set.addOrUpdateSetting calls this on the
    /// value-change path.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="obj">the setting</param>
    public void UpdateMeta(SessionLocal session, Setting obj)
    {
        LockMeta(session);
        _ = GetNextModificationMetaId();
    }

    /// <summary>
    /// Remove a setting from the registry.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="obj">the setting</param>
    public void RemoveDatabaseObject(SessionLocal session, Setting obj)
    {
        CheckWritingAllowed();
        LockMeta(session);
        string name = obj.GetName()!;
        lock (syncRoot)
        {
            _ = settings.Remove(name);
            obj.RemoveChildrenAndResources(session);

            // removeMeta is deferred with the meta table.
        }

        ClearQueryCache(obj.GetId());
    }

    /// <summary>
    /// The default lock timeout of a new session, from the DEFAULT_LOCK_TIMEOUT setting.
    /// </summary>
    /// <returns>the lock timeout</returns>
    public int GetLockTimeout()
    {
        Setting? setting = FindSetting(SetTypes.GetTypeName(SetTypes.DefaultLockTimeout));
        return setting is null ? Constants.InitialLockTimeout : setting.GetIntValue();
    }

    /// <summary>
    /// Reports the current session's allowed literals.
    /// </summary>
    /// <returns>the allowed literals</returns>
    public int GetAllowLiterals()
    {
        if (starting)
        {
            return Constants.AllowLiteralsAll;
        }

        return allowLiterals;
    }

    public void SetAllowLiterals(int value)
    {
        allowLiterals = value;
    }

    public void SetCacheSize(int kb)
    {
        store.GetMvStore().SetCacheSize(Math.Max(1, kb));
    }

    public void SetIgnoreCase(bool b)
    {
        ignoreCase = b;
    }

    public bool IsIgnoreCase() => ignoreCase;

    public void SetLockMode(int newLockMode)
    {
        switch (newLockMode)
        {
            case Constants.LockModeOff:
            case Constants.LockModeReadCommitted:
                break;
            case Constants.LockModeTable:
            case Constants.LockModeTableGc:
                newLockMode = Constants.LockModeReadCommitted;
                break;
            default:
                throw DbException.GetInvalidValueException("lock mode", newLockMode);
        }

        lockMode = newLockMode;
    }

    public int GetLockMode() => lockMode;

    public void SetCloseDelay(int value)
    {
        closeDelay = value;
    }

    public int GetCloseDelay() => closeDelay;

    public void SetMaxLengthInplaceLob(int value)
    {
        maxLengthInplaceLob = value;
    }

    public int GetMaxLengthInplaceLob() => maxLengthInplaceLob;

    public void SetMaxOperationMemory(int value)
    {
        maxOperationMemory = value;
    }

    public int GetMaxOperationMemory() => maxOperationMemory;

    public int GetWriteDelay()
    {
        return store.GetMvStore().GetAutoCommitDelay();
    }

    public void SetWriteDelay(int value)
    {
        store.GetMvStore().SetAutoCommitDelay(value < 0 ? 0 : value);
    }

    public int GetRetentionTime()
    {
        return store.GetMvStore().GetRetentionTime();
    }

    public void SetRetentionTime(int value)
    {
        store.GetMvStore().SetRetentionTime(value);
    }

    public void SetReferentialIntegrity(bool b)
    {
        referentialIntegrity = b;
    }

    public bool IsReferentialIntegrity() => referentialIntegrity;

    public void SetQueryStatistics(bool b)
    {
        queryStatistics = b;

        // The reference also drops the collected measurements here; the collector itself is
        // not ported, so the flag is the whole state.
    }

    public bool IsQueryStatistics() => queryStatistics;

    public void SetQueryStatisticsMaxEntries(int n)
    {
        queryStatisticsMaxEntries = n;

        // The reference forwards the limit to the collector when one is running; the
        // collector is not ported, so the field is where the limit waits.
    }

    public int GetQueryStatisticsMaxEntries() => queryStatisticsMaxEntries;

    public void SetOptimizeReuseResults(bool b)
    {
        optimizeReuseResults = b;
    }

    public bool IsOptimizeReuseResults() => optimizeReuseResults;

    public void SetAllowBuiltinAliasOverride(bool b)
    {
        allowBuiltinAliasOverride = b;
    }

    public bool IsAllowBuiltinAliasOverride() => allowBuiltinAliasOverride;

    public void SetIgnoreCatalogs(bool b)
    {
        ignoreCatalogs = b;
    }

    public bool IsIgnoreCatalogs() => ignoreCatalogs;

    public void SetDefaultTableType(int newDefaultTableType)
    {
        defaultTableType = newDefaultTableType;
    }

    public int GetDefaultTableType() => defaultTableType;

    public void SetMode(Mode newMode)
    {
        if (!ReferenceEquals(mode, newMode))
        {
            throw DbException.GetUnsupportedException(
                "SET MODE: the mode is fixed when the database is opened");
        }
    }

    public Mode GetMode()
    {
        return mode;
    }

    public string GetCluster()
    {
        return cluster;
    }

    public void SetCluster(string newCluster)
    {
        cluster = newCluster;
    }

    public void SetJavaObjectSerializerName(string? serializerName)
    {
        lock (syncRoot)
        {
            javaObjectSerializerName = serializerName;
        }
    }

    public string? GetJavaObjectSerializerName() => javaObjectSerializerName;

    /// <summary>
    /// Sets the database event listener, loading the class it names.
    /// </summary>
    /// <param name="className">the class name</param>
    public void SetEventListenerClass(string? className)
    {
        if (string.IsNullOrEmpty(className))
        {
            return;
        }

        try
        {
            // The reference loads a Java class and casts it to DatabaseEventListener; the port
            // has no listener interface, so the load itself is what stands: a class nobody can
            // provide fails here exactly as it does in the reference without the class on the
            // classpath.
            throw new InvalidOperationException("Class \"" + className + "\" not found");
        }
        catch (Exception e)
        {
            throw DbException.Get(
                ErrorCode.ErrorSettingDatabaseEventListener2, e, className, e.ToString());
        }
    }

    /// <summary>
    /// Whether the database is in exclusive mode.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsInExclusiveMode()
    {
        return GetExclusiveSession() is not null;
    }

    /// <summary>
    /// The session that can exclusively access the database, or null.
    /// </summary>
    /// <returns>the session, or null</returns>
    public SessionLocal? GetExclusiveSession()
    {
        return exclusiveSession;
    }

    /// <summary>
    /// Set the session that can exclusively access the database.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="closeOthers">whether other sessions are closed</param>
    /// <returns>true if success or if database is in exclusive mode set by this session already, false otherwise</returns>
    public bool SetExclusiveSession(SessionLocal session, bool closeOthers)
    {
        lock (syncRoot)
        {
            if (exclusiveSession != session && exclusiveSession is not null)
            {
                return false;
            }

            exclusiveSession = session;
        }

        if (closeOthers)
        {
            // The reference closes every other session here; server-mode sessions are not
            // ported, and in a single-connection database there is nobody to close.
        }

        return true;
    }

    /// <summary>
    /// Stop exclusive access the database by provided session.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>true if success or if database is in non-exclusive mode already, false otherwise</returns>
    public bool UnsetExclusiveSession(SessionLocal session)
    {
        lock (syncRoot)
        {
            return exclusiveSession is null || exclusiveSession == session;
        }
    }

    /// <summary>
    /// Fills the consumer with the settings INFORMATION_SCHEMA.SETTINGS shows beyond its own
    /// rows: the computed ones and the database settings.
    /// </summary>
    /// <param name="consumer">the (name, value) sink</param>
    public void PopulateInfo(Action<string, string> consumer)
    {
        consumer("DEFAULT_NULL_ORDERING", GetDefaultNullOrdering().ToString());
        consumer("EXCLUSIVE", IsInExclusiveMode() ? "TRUE" : "FALSE");
        consumer("MODE", GetMode().Name);
        consumer("RETENTION_TIME", GetRetentionTime().ToString(System.Globalization.CultureInfo.InvariantCulture));
        consumer("WRITE_DELAY", GetWriteDelay().ToString(System.Globalization.CultureInfo.InvariantCulture));

        // The reference adds the DbSettings table and the MVStore's own rows here; DbSettings
        // is not ported, and the MVStore's rows are filled by its PopulateInfo below.
        store.GetMvStore().PopulateInfo(consumer);
    }
}

