/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.Set.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * This class represents the statement SET.
 *
 * What is left out, and why:
 *
 * * checkAdmin on every branch that the reference guards with it. The port has no users and
 *   no rights, so there is no session.getUser().checkAdmin() to call; whoever can run a
 *   statement is the engine's only user. The guards come back with the user machinery.
 * * The trace system. TRACE_LEVEL_FILE, TRACE_LEVEL_SYSTEM_OUT and the setMaxFileSize half
 *   of TRACE_MAX_FILE_SIZE write into a TraceSystem that is not ported; the settings
 *   themselves are kept, so the statement is accepted and recorded as the reference records
 *   it, and the trace level it would set has nothing to reach.
 * * The authenticator. AUTHENTICATOR is in SetTypes and parses; its update branch names the
 *   missing machinery rather than pretending to set one.
 * * The CLUSTER branch commits through the system session in the reference; sessions here
 *   have no system session to borrow, and the setting row is written with the caller's own.
 */
using System;
using System.Globalization;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Command.Dml;

/// <summary>
/// This class represents the statement SET.
/// </summary>
public sealed class Set : Prepared
{
    private readonly int type;

    private Expression? expression;

    private string? stringValue;

    private string[]? stringValueList;

    public Set(SessionLocal session, int type)
        : base(session)
    {
        this.type = type;
    }

    public void SetString(string v)
    {
        stringValue = v;
    }

    public override bool IsTransactional()
    {
        switch (type)
        {
            case SetTypes.Cluster:
            case SetTypes.Variable:
            case SetTypes.QueryTimeout:
            case SetTypes.LockTimeout:
            case SetTypes.TraceLevelSystemOut:
            case SetTypes.TraceLevelFile:
            case SetTypes.Throttle:
            case SetTypes.Schema:
            case SetTypes.SchemaSearchPath:
            case SetTypes.Catalog:
            case SetTypes.RetentionTime:
            case SetTypes.LazyQueryExecution:
            case SetTypes.NonKeywords:
            case SetTypes.TimeZone:
            case SetTypes.VariableBinary:
            case SetTypes.TruncateLargeLength:
            case SetTypes.WriteDelay:
                return true;
            default:
                return false;
        }
    }

    public override long Update()
    {
        Database database = GetSession().GetDatabase();
        string name = SetTypes.GetTypeName(type);
        switch (type)
        {
            case SetTypes.AllowLiterals:
            {
                int value = GetIntValue();
                if (value < 0 || value > 2)
                {
                    throw DbException.GetInvalidValueException("ALLOW_LITERALS", value);
                }

                lock (database)
                {
                    database.SetAllowLiterals(value);
                    AddOrUpdateSetting(name, null, value);
                }

                break;
            }
            case SetTypes.CacheSize:
            {
                int value = GetIntValue();
                if (value < 0)
                {
                    throw DbException.GetInvalidValueException("CACHE_SIZE", value);
                }

                lock (database)
                {
                    database.SetCacheSize(value);
                    AddOrUpdateSetting(name, null, value);
                }

                break;
            }
            case SetTypes.Cluster:
            {
                if (Constants.ClusteringEnabled.Equals(stringValue))
                {
                    // this value is used when connecting
                    // ignore, as the cluster setting is checked later
                    break;
                }

                string value = StringUtils.QuoteStringSql(stringValue!)!;
                if (!value.Equals(database.GetCluster()))
                {
                    if (!value.Equals(Constants.ClusteringDisabled))
                    {
                        // anybody can disable the cluster
                        // (if he can't access a cluster node)
                        // checkAdmin waits for the user machinery
                    }

                    database.SetCluster(value);

                    // The reference writes the setting row with the system session and commits
                    // it there, so the caller's transaction is not committed by the setting;
                    // there is no system session to borrow, so the caller's session writes it
                    // and the row travels with the caller's transaction.
                    lock (database)
                    {
                        AddOrUpdateSetting(GetSession(), name, value, 0);
                    }
                }

                break;
            }
            case SetTypes.Collation:
            {
                Values.CompareMode compareMode;
                StringBuilder buff = new(stringValue);
                if (stringValue!.Equals(Values.CompareMode.Off))
                {
                    compareMode = Values.CompareMode.GetInstance(null, 0);
                }
                else
                {
                    int strength = GetIntValue();
                    _ = buff.Append(" STRENGTH ");
                    if (strength == Values.CompareMode.Identical)
                    {
                        _ = buff.Append("IDENTICAL");
                    }
                    else if (strength == Values.CompareMode.Primary)
                    {
                        _ = buff.Append("PRIMARY");
                    }
                    else if (strength == Values.CompareMode.Secondary)
                    {
                        _ = buff.Append("SECONDARY");
                    }
                    else if (strength == Values.CompareMode.Tertiary)
                    {
                        _ = buff.Append("TERTIARY");
                    }

                    compareMode = Values.CompareMode.GetInstance(stringValue, strength);
                }

                lock (database)
                {
                    Values.CompareMode old = database.GetCompareMode();
                    if (old.Equals(compareMode))
                    {
                        break;
                    }

                    Table? table = database.GetFirstUserTable();
                    if (table is not null)
                    {
                        throw DbException.Get(
                            ErrorCode.CollationChangeWithDataTable1, table.GetTraceSql());
                    }

                    AddOrUpdateSetting(name, buff.ToString(), 0);
                    database.SetCompareMode(compareMode);
                }

                break;
            }
            case SetTypes.CreateBuild:
            {
                if (database.IsStarting())
                {
                    // just ignore the command if not starting
                    // this avoids problems when running recovery scripts
                    int value = GetIntValue();
                    lock (database)
                    {
                        AddOrUpdateSetting(name, null, value);
                    }
                }

                break;
            }
            case SetTypes.DatabaseEventListener:
            {
                database.SetEventListenerClass(stringValue);
                break;
            }
            case SetTypes.DbCloseDelay:
            {
                int value = GetIntValue();
                if (value == -1)
                {
                    // -1 is a special value for in-memory databases,
                    // which means "keep the DB alive and use the same
                    // DB for all connections"
                }
                else if (value < 0)
                {
                    throw DbException.GetInvalidValueException("DB_CLOSE_DELAY", value);
                }

                lock (database)
                {
                    database.SetCloseDelay(value);
                    AddOrUpdateSetting(name, null, value);
                }

                break;
            }
            case SetTypes.DefaultLockTimeout:
            {
                int value = GetIntValue();
                if (value < 0)
                {
                    throw DbException.GetInvalidValueException("DEFAULT_LOCK_TIMEOUT", value);
                }

                lock (database)
                {
                    AddOrUpdateSetting(name, null, value);
                }

                break;
            }
            case SetTypes.DefaultTableType:
            {
                int value = GetIntValue();
                lock (database)
                {
                    database.SetDefaultTableType(value);
                    AddOrUpdateSetting(name, null, value);
                }

                break;
            }
            case SetTypes.Exclusive:
            {
                int value = GetIntValue();
                SessionLocal session = GetSession();
                switch (value)
                {
                    case 0:
                        if (!database.UnsetExclusiveSession(session))
                        {
                            throw DbException.Get(ErrorCode.DatabaseIsInExclusiveMode);
                        }

                        break;
                    case 1:
                        if (!database.SetExclusiveSession(session, false))
                        {
                            throw DbException.Get(ErrorCode.DatabaseIsInExclusiveMode);
                        }

                        break;
                    case 2:
                        if (!database.SetExclusiveSession(session, true))
                        {
                            throw DbException.Get(ErrorCode.DatabaseIsInExclusiveMode);
                        }

                        break;
                    default:
                        throw DbException.GetInvalidValueException("EXCLUSIVE", value);
                }

                break;
            }
            case SetTypes.JavaObjectSerializer:
            {
                lock (database)
                {
                    Table? table = database.GetFirstUserTable();
                    if (table is not null)
                    {
                        throw DbException.Get(
                            ErrorCode.JavaObjectSerializerChangeWithDataTable, table.GetTraceSql());
                    }

                    database.SetJavaObjectSerializerName(stringValue);
                    AddOrUpdateSetting(name, stringValue, 0);
                }

                break;
            }
            case SetTypes.IgnoreCase:
            {
                int value = GetIntValue();
                lock (database)
                {
                    database.SetIgnoreCase(value == 1);
                    AddOrUpdateSetting(name, null, value);
                }

                break;
            }
            case SetTypes.LockMode:
            {
                int value = GetIntValue();
                lock (database)
                {
                    database.SetLockMode(value);
                    AddOrUpdateSetting(name, null, value);
                }

                break;
            }
            case SetTypes.LockTimeout:
            {
                int value = GetIntValue();
                if (value < 0)
                {
                    throw DbException.GetInvalidValueException("LOCK_TIMEOUT", value);
                }

                GetSession().SetLockTimeout(value);
                break;
            }
            case SetTypes.MaxLengthInplaceLob:
            {
                int value = GetIntValue();
                if (value < 0)
                {
                    throw DbException.GetInvalidValueException("MAX_LENGTH_INPLACE_LOB", value);
                }

                lock (database)
                {
                    database.SetMaxLengthInplaceLob(value);
                    AddOrUpdateSetting(name, null, value);
                }

                break;
            }
            case SetTypes.MaxLogSize:
            {
                int value = GetIntValue();
                if (value < 0)
                {
                    throw DbException.GetInvalidValueException("MAX_LOG_SIZE", value);
                }

                break;
            }
            case SetTypes.MaxMemoryRows:
            {
                int value = GetIntValue();
                if (value < 0)
                {
                    throw DbException.GetInvalidValueException("MAX_MEMORY_ROWS", value);
                }

                lock (database)
                {
                    database.SetMaxMemoryRows(value);
                    AddOrUpdateSetting(name, null, value);
                }

                break;
            }
            case SetTypes.MaxMemoryUndo:
            {
                int value = GetIntValue();
                if (value < 0)
                {
                    throw DbException.GetInvalidValueException("MAX_MEMORY_UNDO", value);
                }

                lock (database)
                {
                    AddOrUpdateSetting(name, null, value);
                }

                break;
            }
            case SetTypes.MaxOperationMemory:
            {
                int value = GetIntValue();
                if (value < 0)
                {
                    throw DbException.GetInvalidValueException("MAX_OPERATION_MEMORY", value);
                }

                database.SetMaxOperationMemory(value);
                break;
            }
            case SetTypes.Mode:
            {
                Mode? mode = Mode.GetInstance(stringValue);
                if (mode is null)
                {
                    throw DbException.Get(ErrorCode.UnknownMode1, stringValue);
                }

                if (database.GetMode() != mode)
                {
                    database.SetMode(mode);
                }

                break;
            }
            case SetTypes.OptimizeReuseResults:
            {
                database.SetOptimizeReuseResults(GetIntValue() != 0);
                break;
            }
            case SetTypes.QueryTimeout:
            {
                int value = GetIntValue();
                if (value < 0)
                {
                    throw DbException.GetInvalidValueException("QUERY_TIMEOUT", value);
                }

                GetSession().SetQueryTimeout(value);
                break;
            }
            case SetTypes.RedoLogBinary:
            {
                throw DbException.GetUnsupportedException("MV_STORE + SET REDO_LOG_BINARY");
            }
            case SetTypes.ReferentialIntegrity:
            {
                int value = GetIntValue();
                if (value < 0 || value > 1)
                {
                    throw DbException.GetInvalidValueException("REFERENTIAL_INTEGRITY", value);
                }

                database.SetReferentialIntegrity(value == 1);
                break;
            }
            case SetTypes.QueryStatistics:
            {
                int value = GetIntValue();
                if (value < 0 || value > 1)
                {
                    throw DbException.GetInvalidValueException("QUERY_STATISTICS", value);
                }

                database.SetQueryStatistics(value == 1);
                break;
            }
            case SetTypes.QueryStatisticsMaxEntries:
            {
                int value = GetIntValue();
                if (value < 1)
                {
                    throw DbException.GetInvalidValueException("QUERY_STATISTICS_MAX_ENTRIES", value);
                }

                database.SetQueryStatisticsMaxEntries(value);
                break;
            }
            case SetTypes.Schema:
            {
                SessionLocal session = GetSession();
                Schemas.Schema schema = session.GetDatabase()
                    .GetSchema(expression!.Optimize(session).GetValue(session).GetString()!);
                session.SetCurrentSchema(schema);
                break;
            }
            case SetTypes.SchemaSearchPath:
            {
                GetSession().SetSchemaSearchPath(stringValueList);
                break;
            }
            case SetTypes.Catalog:
            {
                SessionLocal session = GetSession();
                string shortName = session.GetDatabase().GetShortName();
                string? value = expression!.Optimize(session).GetValue(session).GetString();
                if (value is null
                    || (!session.GetDatabase().EqualsIdentifiers(shortName, value)
                        && !session.GetDatabase().EqualsIdentifiers(shortName, value.Trim())))
                {
                    throw DbException.Get(ErrorCode.DatabaseNotFound1, stringValue);
                }

                break;
            }
            case SetTypes.TraceLevelFile:
            case SetTypes.TraceLevelSystemOut:
                if (GetPersistedObjectId() == 0)
                {
                    // don't set the property when opening the database
                    // this is for compatibility with older versions, because
                    // this setting was persistent
                    _ = GetIntValue();

                    // The level itself goes to the trace system, which is not ported.
                }

                break;
            case SetTypes.TraceMaxFileSize:
            {
                int value = GetIntValue();
                if (value < 0)
                {
                    throw DbException.GetInvalidValueException("TRACE_MAX_FILE_SIZE", value);
                }

                // The size limit goes to the trace system, which is not ported.
                lock (database)
                {
                    AddOrUpdateSetting(name, null, value);
                }

                break;
            }
            case SetTypes.Throttle:
            {
                int value = GetIntValue();
                if (value < 0)
                {
                    throw DbException.GetInvalidValueException("THROTTLE", value);
                }

                GetSession().SetThrottle(value);
                break;
            }
            case SetTypes.Variable:
            {
                SessionLocal session = GetSession();
                Expression expr = expression!.Optimize(session);
                session.SetVariable(stringValue!, expr.GetValue(session));
                break;
            }
            case SetTypes.WriteDelay:
            {
                int value = GetIntValue();
                if (value < 0)
                {
                    throw DbException.GetInvalidValueException("WRITE_DELAY", value);
                }

                lock (database)
                {
                    database.SetWriteDelay(value);
                    AddOrUpdateSetting(name, null, value);
                }

                break;
            }
            case SetTypes.RetentionTime:
            {
                int value = GetIntValue();
                if (value < 0)
                {
                    throw DbException.GetInvalidValueException("RETENTION_TIME", value);
                }

                lock (database)
                {
                    database.SetRetentionTime(value);
                    AddOrUpdateSetting(name, null, value);
                }

                break;
            }
            case SetTypes.LazyQueryExecution:
            {
                int value = GetIntValue();
                if (value != 0 && value != 1)
                {
                    throw DbException.GetInvalidValueException("LAZY_QUERY_EXECUTION", value);
                }

                GetSession().SetLazyQueryExecution(value == 1);
                break;
            }
            case SetTypes.BuiltinAliasOverride:
            {
                int value = GetIntValue();
                if (value != 0 && value != 1)
                {
                    throw DbException.GetInvalidValueException("BUILTIN_ALIAS_OVERRIDE", value);
                }

                database.SetAllowBuiltinAliasOverride(value == 1);
                break;
            }
            case SetTypes.Authenticator:
            {
                throw new TODOException("Set.update AUTHENTICATOR: the authenticator is not ported");
            }
            case SetTypes.IgnoreCatalogs:
            {
                int value = GetIntValue();
                lock (database)
                {
                    database.SetIgnoreCatalogs(value == 1);
                    AddOrUpdateSetting(name, null, value);
                }

                break;
            }
            case SetTypes.NonKeywords:
                GetSession().SetNonKeywords(Parser.ParseNonKeywords(stringValueList!));
                break;
            case SetTypes.TimeZone:
                GetSession().SetTimeZone(expression is null
                    ? TimeZoneProvider.GetDefault()
                    : ParseTimeZone(expression.GetValue(GetSession())));
                break;
            case SetTypes.VariableBinary:
                GetSession().SetVariableBinary(expression!.GetBooleanValue(GetSession()));
                break;
            case SetTypes.DefaultNullOrdering:
            {
                DefaultNullOrdering defaultNullOrdering;
                try
                {
                    defaultNullOrdering = (DefaultNullOrdering)System.Enum.Parse(
                        typeof(DefaultNullOrdering), StringUtils.ToUpperEnglish(stringValue!), true);
                }
                catch (ArgumentException)
                {
                    throw DbException.GetInvalidValueException("DEFAULT_NULL_ORDERING", stringValue);
                }

                if (database.GetDefaultNullOrdering() != defaultNullOrdering)
                {
                    database.SetDefaultNullOrdering(defaultNullOrdering);
                }

                break;
            }
            case SetTypes.TruncateLargeLength:
                GetSession().SetTruncateLargeLength(expression!.GetBooleanValue(GetSession()));
                break;
            default:
                throw DbException.GetInternalError("type=" + type);
        }

        // the metadata information has changed
        _ = database.GetNextModificationDataId();

        // query caches might be affected as well, for example
        // when changing the compatibility mode
        _ = database.GetNextModificationMetaId();
        return 0;
    }

    private static TimeZoneProvider ParseTimeZone(Value v)
    {
        if (DataType.IsCharacterStringType(v.GetValueType()))
        {
            TimeZoneProvider timeZone;
            try
            {
                timeZone = TimeZoneProvider.OfId(v.GetString()!);
            }
            catch (Exception)
            {
                throw DbException.GetInvalidValueException("TIME ZONE", v.GetTraceSql());
            }

            return timeZone;
        }
        else if (v == ValueNull.Instance)
        {
            throw DbException.GetInvalidValueException("TIME ZONE", v.GetTraceSql());
        }

        throw new TODOException("Set.parseTimeZone: interval time zones are not ported");
    }

    private int GetIntValue()
    {
        expression = expression!.Optimize(GetSession());
        return expression.GetValue(GetSession()).GetInt();
    }

    public void SetInt(int value)
    {
        expression = ValueExpression.Get(ValueInteger.Get(value));
    }

    public void SetExpression(Expression expression)
    {
        this.expression = expression;
    }

    private void AddOrUpdateSetting(string name, string? s, int v)
    {
        AddOrUpdateSetting(GetSession(), name, s, v);
    }

    private void AddOrUpdateSetting(SessionLocal session, string name, string? s, int v)
    {
        Database database = session.GetDatabase();
        if (database.IsReadOnly())
        {
            return;
        }

        Setting? setting = database.FindSetting(name);
        bool addNew = false;
        if (setting is null)
        {
            addNew = true;
            int id = GetObjectId();
            setting = new Setting(database, id, name);
        }

        if (s is not null)
        {
            if (!addNew && setting.GetStringValue()!.Equals(s))
            {
                return;
            }

            setting.SetStringValue(s);
        }
        else
        {
            if (!addNew && setting.GetIntValue() == v)
            {
                return;
            }

            setting.SetIntValue(v);
        }

        if (addNew)
        {
            database.AddDatabaseObject(session, setting);
        }
        else
        {
            database.UpdateMeta(session, setting);
        }
    }

    public override bool NeedRecompile()
    {
        return false;
    }

    public override ResultInterface? QueryMeta()
    {
        return null;
    }

    public void SetStringArray(string[] list)
    {
        stringValueList = list;
    }

    public override int GetCommandType() => CommandInterface.Set;
}
