/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.SetTypes.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;

namespace H2Sharp.Command.Dml;

/// <summary>
/// The list of setting for a SET statement.
/// </summary>
public static class SetTypes
{
    /// <summary>The type of a SET IGNORECASE statement.</summary>
    public const int IgnoreCase = 0;

    /// <summary>The type of a SET MAX_LOG_SIZE statement.</summary>
    public const int MaxLogSize = IgnoreCase + 1;

    /// <summary>The type of a SET MODE statement.</summary>
    public const int Mode = MaxLogSize + 1;

    /// <summary>The type of a SET READONLY statement.</summary>
    public const int Readonly = Mode + 1;

    /// <summary>The type of a SET LOCK_TIMEOUT statement.</summary>
    public const int LockTimeout = Readonly + 1;

    /// <summary>The type of a SET DEFAULT_LOCK_TIMEOUT statement.</summary>
    public const int DefaultLockTimeout = LockTimeout + 1;

    /// <summary>The type of a SET DEFAULT_TABLE_TYPE statement.</summary>
    public const int DefaultTableType = DefaultLockTimeout + 1;

    /// <summary>The type of a SET CACHE_SIZE statement.</summary>
    public const int CacheSize = DefaultTableType + 1;

    /// <summary>The type of a SET TRACE_LEVEL_SYSTEM_OUT statement.</summary>
    public const int TraceLevelSystemOut = CacheSize + 1;

    /// <summary>The type of a SET TRACE_LEVEL_FILE statement.</summary>
    public const int TraceLevelFile = TraceLevelSystemOut + 1;

    /// <summary>The type of a SET TRACE_MAX_FILE_SIZE statement.</summary>
    public const int TraceMaxFileSize = TraceLevelFile + 1;

    /// <summary>The type of a SET COLLATION  statement.</summary>
    public const int Collation = TraceMaxFileSize + 1;

    /// <summary>The type of a SET CLUSTER statement.</summary>
    public const int Cluster = Collation + 1;

    /// <summary>The type of a SET WRITE_DELAY statement.</summary>
    public const int WriteDelay = Cluster + 1;

    /// <summary>The type of a SET DATABASE_EVENT_LISTENER statement.</summary>
    public const int DatabaseEventListener = WriteDelay + 1;

    /// <summary>The type of a SET MAX_MEMORY_ROWS statement.</summary>
    public const int MaxMemoryRows = DatabaseEventListener + 1;

    /// <summary>The type of a SET LOCK_MODE statement.</summary>
    public const int LockMode = MaxMemoryRows + 1;

    /// <summary>The type of a SET DB_CLOSE_DELAY statement.</summary>
    public const int DbCloseDelay = LockMode + 1;

    /// <summary>The type of a SET THROTTLE statement.</summary>
    public const int Throttle = DbCloseDelay + 1;

    /// <summary>The type of a SET MAX_MEMORY_UNDO statement.</summary>
    public const int MaxMemoryUndo = Throttle + 1;

    /// <summary>The type of a SET MAX_LENGTH_INPLACE_LOB statement.</summary>
    public const int MaxLengthInplaceLob = MaxMemoryUndo + 1;

    /// <summary>The type of a SET ALLOW_LITERALS statement.</summary>
    public const int AllowLiterals = MaxLengthInplaceLob + 1;

    /// <summary>The type of a SET SCHEMA statement.</summary>
    public const int Schema = AllowLiterals + 1;

    /// <summary>The type of a SET OPTIMIZE_REUSE_RESULTS statement.</summary>
    public const int OptimizeReuseResults = Schema + 1;

    /// <summary>The type of a SET SCHEMA_SEARCH_PATH statement.</summary>
    public const int SchemaSearchPath = OptimizeReuseResults + 1;

    /// <summary>The type of a SET REFERENTIAL_INTEGRITY statement.</summary>
    public const int ReferentialIntegrity = SchemaSearchPath + 1;

    /// <summary>The type of a SET MAX_OPERATION_MEMORY statement.</summary>
    public const int MaxOperationMemory = ReferentialIntegrity + 1;

    /// <summary>The type of a SET EXCLUSIVE statement.</summary>
    public const int Exclusive = MaxOperationMemory + 1;

    /// <summary>The type of a SET CREATE_BUILD statement.</summary>
    public const int CreateBuild = Exclusive + 1;

    /// <summary>The type of a SET \@VARIABLE statement.</summary>
    public const int Variable = CreateBuild + 1;

    /// <summary>The type of a SET QUERY_TIMEOUT statement.</summary>
    public const int QueryTimeout = Variable + 1;

    /// <summary>The type of a SET REDO_LOG_BINARY statement.</summary>
    public const int RedoLogBinary = QueryTimeout + 1;

    /// <summary>The type of a SET JAVA_OBJECT_SERIALIZER statement.</summary>
    public const int JavaObjectSerializer = RedoLogBinary + 1;

    /// <summary>The type of a SET RETENTION_TIME statement.</summary>
    public const int RetentionTime = JavaObjectSerializer + 1;

    /// <summary>The type of a SET QUERY_STATISTICS statement.</summary>
    public const int QueryStatistics = RetentionTime + 1;

    /// <summary>The type of a SET QUERY_STATISTICS_MAX_ENTRIES statement.</summary>
    public const int QueryStatisticsMaxEntries = QueryStatistics + 1;

    /// <summary>The type of SET LAZY_QUERY_EXECUTION statement.</summary>
    public const int LazyQueryExecution = QueryStatisticsMaxEntries + 1;

    /// <summary>The type of SET BUILTIN_ALIAS_OVERRIDE statement.</summary>
    public const int BuiltinAliasOverride = LazyQueryExecution + 1;

    /// <summary>The type of a SET AUTHENTICATOR statement.</summary>
    public const int Authenticator = BuiltinAliasOverride + 1;

    /// <summary>The type of a SET IGNORE_CATALOGS statement.</summary>
    public const int IgnoreCatalogs = Authenticator + 1;

    /// <summary>The type of a SET CATALOG statement.</summary>
    public const int Catalog = IgnoreCatalogs + 1;

    /// <summary>The type of a SET NON_KEYWORDS statement.</summary>
    public const int NonKeywords = Catalog + 1;

    /// <summary>The type of a SET TIME ZONE statement.</summary>
    public const int TimeZone = NonKeywords + 1;

    /// <summary>The type of a SET VARIABLE_BINARY statement.</summary>
    public const int VariableBinary = TimeZone + 1;

    /// <summary>The type of a SET DEFAULT_NULL_ORDERING statement.</summary>
    public const int DefaultNullOrdering = VariableBinary + 1;

    /// <summary>The type of a SET TRUNCATE_LARGE_LENGTH statement.</summary>
    public const int TruncateLargeLength = DefaultNullOrdering + 1;

    /// <summary>
    /// The names, at the index of their type.
    /// </summary>
    private static readonly string[] Types =
    [
        "IGNORECASE",
        "MAX_LOG_SIZE",
        "MODE",
        "READONLY",
        "LOCK_TIMEOUT",
        "DEFAULT_LOCK_TIMEOUT",
        "DEFAULT_TABLE_TYPE",
        "CACHE_SIZE",
        "TRACE_LEVEL_SYSTEM_OUT",
        "TRACE_LEVEL_FILE",
        "TRACE_MAX_FILE_SIZE",
        "COLLATION",
        "CLUSTER",
        "WRITE_DELAY",
        "DATABASE_EVENT_LISTENER",
        "MAX_MEMORY_ROWS",
        "LOCK_MODE",
        "DB_CLOSE_DELAY",
        "THROTTLE",
        "MAX_MEMORY_UNDO",
        "MAX_LENGTH_INPLACE_LOB",
        "ALLOW_LITERALS",
        "SCHEMA",
        "OPTIMIZE_REUSE_RESULTS",
        "SCHEMA_SEARCH_PATH",
        "REFERENTIAL_INTEGRITY",
        "MAX_OPERATION_MEMORY",
        "EXCLUSIVE",
        "CREATE_BUILD",
        "@",
        "QUERY_TIMEOUT",
        "REDO_LOG_BINARY",
        "JAVA_OBJECT_SERIALIZER",
        "RETENTION_TIME",
        "QUERY_STATISTICS",
        "QUERY_STATISTICS_MAX_ENTRIES",
        "LAZY_QUERY_EXECUTION",
        "BUILTIN_ALIAS_OVERRIDE",
        "AUTHENTICATOR",
        "IGNORE_CATALOGS",
        "CATALOG",
        "NON_KEYWORDS",
        "TIME ZONE",
        "VARIABLE_BINARY",
        "DEFAULT_NULL_ORDERING",
        "TRUNCATE_LARGE_LENGTH",
    ];

    /// <summary>
    /// Get the set type number.
    /// </summary>
    /// <param name="name">the set type name</param>
    /// <returns>the number, or -1 when the name is not a setting</returns>
    public static int GetType(string name)
        => Array.IndexOf(Types, name);

    /// <summary>
    /// Get the set type name.
    /// </summary>
    /// <param name="type">the type number</param>
    /// <returns>the name</returns>
    public static string GetTypeName(int type)
        => Types[type];

    /// <summary>
    /// Get all set type names.
    /// </summary>
    /// <returns>the names</returns>
    public static IReadOnlyList<string> GetTypes()
        => Types;
}
