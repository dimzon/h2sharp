/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.engine.Constants.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Modified: fields are PascalCase; constants become `const`. The static initializer
 * that composed VERSION/FULL_VERSION became static readonly fields because C#
 * constant expressions cannot concatenate strings with ints.
 *
 * ResultSet.CONCUR_READ_ONLY is java.sql.ResultSet.CONCUR_READ_ONLY == 1007, which
 * has no ADO.NET counterpart. It is retained verbatim because H2 uses it when
 * reporting result-set concurrency in metadata tables.
 *
 * Server/TCP/HTTP/web-console constants are retained for fidelity even though
 * server mode is out of scope for H2Sharp (see docs/PORTING.md).
 */
namespace H2Sharp.Engine;

/// <summary>
/// Constants are fixed values that are used in the whole database code.
/// </summary>
public static class Constants
{
    /// <summary>
    /// The build date is updated for each public release.
    /// </summary>
    public const string BuildDate = "2026-09-23";

    /// <summary>
    /// Sequential version number. Even numbers are used for official releases,
    /// odd numbers are used for development builds.
    /// </summary>
    public const int BuildId = 259;

    /// <summary>
    /// Whether this is a snapshot version.
    /// </summary>
    public const bool BuildSnapshot = true;

    /// <summary>
    /// If H2 is compiled to be included in a product, this should be set to
    /// a unique vendor id (to distinguish from official releases).
    /// Additionally, a version number should be set to distinguish releases.
    /// Example: ACME_SVN1651_BUILD3
    /// </summary>
    /// <remarks>
    /// Declared <c>static readonly</c> rather than <c>const</c> so that the null check in
    /// <see cref="BuildVersion"/> is not constant-folded away by the compiler. H2 builds
    /// with <c>null</c>; forks can set a vendor id here.
    /// </remarks>
    public static readonly string? BuildVendorAndVersion = null;

    /// <summary>
    /// The TCP protocol version number 17.
    /// </summary>
    public const int TcpProtocolVersion17 = 17;

    /// <summary>
    /// The TCP protocol version number 18.
    /// </summary>
    public const int TcpProtocolVersion18 = 18;

    /// <summary>
    /// The TCP protocol version number 19.
    /// </summary>
    public const int TcpProtocolVersion19 = 19;

    /// <summary>
    /// The TCP protocol version number 20.
    /// </summary>
    public const int TcpProtocolVersion20 = 20;

    /// <summary>
    /// The TCP protocol version number 21.
    /// </summary>
    public const int TcpProtocolVersion21 = 21;

    /// <summary>
    /// Minimum supported version of TCP protocol.
    /// </summary>
    public const int TcpProtocolVersionMinSupported = TcpProtocolVersion17;

    /// <summary>
    /// Maximum supported version of TCP protocol.
    /// </summary>
    public const int TcpProtocolVersionMaxSupported = TcpProtocolVersion21;

    /// <summary>
    /// The major version of this database.
    /// </summary>
    public const int VersionMajor = 2;

    /// <summary>
    /// The minor version of this database.
    /// </summary>
    public const int VersionMinor = 5;

    /// <summary>
    /// The lock mode that means no locking is used at all.
    /// </summary>
    public const int LockModeOff = 0;

    /// <summary>
    /// The lock mode that means read locks are acquired, but they are released
    /// immediately after the statement is executed.
    /// </summary>
    public const int LockModeReadCommitted = 3;

    /// <summary>
    /// The lock mode that means table level locking is used for reads and writes.
    /// </summary>
    public const int LockModeTable = 1;

    /// <summary>
    /// The lock mode that means table level locking is used for reads and writes.
    /// If a table is locked, GC.Collect is called to close forgotten connections.
    /// </summary>
    public const int LockModeTableGc = 2;

    /// <summary>
    /// Constant meaning both numbers and text is allowed in SQL statements.
    /// </summary>
    public const int AllowLiteralsAll = 2;

    /// <summary>
    /// Constant meaning no literals are allowed in SQL statements.
    /// </summary>
    public const int AllowLiteralsNone = 0;

    /// <summary>
    /// Constant meaning only numbers are allowed in SQL statements (but no texts).
    /// </summary>
    public const int AllowLiteralsNumbers = 1;

    /// <summary>
    /// SNAPSHOT isolation level of transaction.
    /// </summary>
    public const int TransactionSnapshot = 6;

    /// <summary>
    /// Whether searching in Blob values should be supported.
    /// </summary>
    public const bool BlobSearch = false;

    /// <summary>
    /// The minimum number of entries to keep in the cache.
    /// </summary>
    public const int CacheMinRecords = 16;

    /// <summary>
    /// The default cache type.
    /// </summary>
    public const string CacheTypeDefault = "LRU";

    /// <summary>
    /// The value of the cluster setting if clustering is disabled.
    /// </summary>
    public const string ClusteringDisabled = "''";

    /// <summary>
    /// The value of the cluster setting if clustering is enabled (the actual
    /// value is checked later).
    /// </summary>
    public const string ClusteringEnabled = "TRUE";

    /// <summary>
    /// The database URL used when calling a function if only the column list
    /// should be returned.
    /// </summary>
    public const string ConnUrlColumnList = "jdbc:columnlist:connection";

    /// <summary>
    /// The database URL used when calling a function if the data should be returned.
    /// </summary>
    public const string ConnUrlInternal = "jdbc:default:connection";

    /// <summary>
    /// The cost is calculated on rowcount + this offset, to avoid using the wrong or
    /// no index if the table contains no rows _currently_ (when preparing the statement).
    /// </summary>
    public const int CostRowOffset = 1000;

    /// <summary>
    /// The number of milliseconds after which to check for a deadlock if locking
    /// is not successful.
    /// </summary>
    public const int DeadlockCheck = 100;

    /// <summary>
    /// The default port number of the HTTP server (for the H2 Console).
    /// </summary>
    public const int DefaultHttpPort = 8082;

    /// <summary>
    /// The default value for the LOCK_MODE setting.
    /// </summary>
    public const int DefaultLockMode = LockModeReadCommitted;

    /// <summary>
    /// The default maximum length of a LOB that is stored with the record itself,
    /// and not in a separate place.
    /// </summary>
    public const int DefaultMaxLengthInplaceLob = 256;

    /// <summary>
    /// The default for the setting MAX_OPERATION_MEMORY.
    /// </summary>
    public const int DefaultMaxOperationMemory = 100_000;

    /// <summary>
    /// The default page size to use for new databases.
    /// </summary>
    public const int DefaultPageSize = 16 * 1024;

    /// <summary>
    /// The default result set concurrency for statements created with
    /// Connection.createStatement() or prepareStatement(String sql).
    /// This is java.sql.ResultSet.CONCUR_READ_ONLY == 1007.
    /// </summary>
    public const int DefaultResultSetConcurrency = 1007;

    /// <summary>
    /// The default port of the TCP server.
    /// </summary>
    public const int DefaultTcpPort = 9092;

    /// <summary>
    /// The default delay in milliseconds before the transaction log is written.
    /// </summary>
    public const int DefaultWriteDelay = 500;

    /// <summary>
    /// The password is hashed this many times to slow down dictionary attacks.
    /// </summary>
    public const int EncryptionKeyHashIterations = 1024;

    /// <summary>
    /// The block of a file. It is also the encryption block size.
    /// </summary>
    public const int FileBlockSize = 16;

    /// <summary>
    /// For testing, the lock timeout is smaller than for interactive use cases.
    /// This value could be increased to about 5 or 10 seconds.
    /// </summary>
    public const int InitialLockTimeout = 2000;

    /// <summary>
    /// The block size for I/O operations.
    /// </summary>
    public const int IoBufferSize = 4 * 1024;

    /// <summary>
    /// The block size used to compress data in the LZF output stream.
    /// </summary>
    public const int IoBufferSizeCompress = 128 * 1024;

    /// <summary>
    /// The number of milliseconds to wait between checking that the .lock.db file
    /// still exists once a database is locked.
    /// </summary>
    public const int LockSleep = 1000;

    /// <summary>
    /// The maximum allowed length of identifiers.
    /// </summary>
    public const int MaxIdentifierLength = 256;

    /// <summary>
    /// The maximum number of columns in a table, select statement or row value.
    /// </summary>
    public const int MaxColumns = 16_384;

    /// <summary>
    /// The maximum allowed length for character string, binary string, and other
    /// data types based on them; excluding LOB data types.
    /// This needs to be less than (2^31-8)/2 to avoid running into the limit on
    /// encoding data fields when storing rows.
    /// </summary>
    public const int MaxStringLength = 1_000_000_000;

    /// <summary>
    /// The maximum allowed precision of numeric data types.
    /// </summary>
    public const int MaxNumericPrecision = 100_000;

    /// <summary>
    /// The maximum allowed cardinality of an array.
    /// </summary>
    public const int MaxArrayCardinality = 65_536;

    /// <summary>
    /// The highest possible parameter index.
    /// </summary>
    public const int MaxParameterIndex = 100_000;

    /// <summary>
    /// The memory needed by a regular object with at least one field.
    /// </summary>
    public const int MemoryObject = 24;

    /// <summary>
    /// The memory needed by an array.
    /// </summary>
    public const int MemoryArray = 24;

    /// <summary>
    /// The memory needed by a pointer.
    /// </summary>
    public const int MemoryPointer = 8;

    /// <summary>
    /// The memory needed by a Row.
    /// </summary>
    public const int MemoryRow = 40;

    /// <summary>
    /// The name prefix used for indexes that are not explicitly named.
    /// </summary>
    public const string PrefixIndex = "INDEX_";

    /// <summary>
    /// The name prefix used for synthetic nested join tables.
    /// </summary>
    public const string PrefixJoin = "SYSTEM_JOIN_";

    /// <summary>
    /// The name prefix used for primary key constraints that are not explicitly named.
    /// </summary>
    public const string PrefixPrimaryKey = "PRIMARY_KEY_";

    /// <summary>
    /// The name prefix used for query aliases that are not explicitly named.
    /// </summary>
    public const string PrefixQueryAlias = "QUERY_ALIAS_";

    /// <summary>
    /// Every user belongs to this role.
    /// </summary>
    public const string PublicRoleName = "PUBLIC";

    /// <summary>
    /// The number of bytes in random salt that is used to hash passwords.
    /// </summary>
    public const int SaltLen = 8;

    /// <summary>
    /// The identity of INFORMATION_SCHEMA.
    /// </summary>
    public const int InformationSchemaId = -1;

    /// <summary>
    /// The identity of PUBLIC schema.
    /// </summary>
    public const int MainSchemaId = 0;

    /// <summary>
    /// The name of the default schema.
    /// </summary>
    public const string SchemaMain = "PUBLIC";

    /// <summary>
    /// The identity of pg_catalog schema.
    /// </summary>
    public const int PgCatalogSchemaId = -1_000;

    /// <summary>
    /// The name of the pg_catalog schema.
    /// </summary>
    public const string SchemaPgCatalog = "PG_CATALOG";

    /// <summary>
    /// The default selectivity (used if the selectivity is not calculated).
    /// </summary>
    public const int SelectivityDefault = 50;

    /// <summary>
    /// The number of distinct values to keep in memory when running ANALYZE.
    /// </summary>
    public const int SelectivityDistinctCount = 10_000;

    /// <summary>
    /// The default directory name of the server properties file for the H2 Console.
    /// </summary>
    public const string ServerPropertiesDir = "~";

    /// <summary>
    /// The name of the server properties file for the H2 Console.
    /// </summary>
    public const string ServerPropertiesName = ".h2.server.properties";

    /// <summary>
    /// Queries that take longer than this number of milliseconds are written to
    /// the trace file with the level info.
    /// </summary>
    public const long SlowQueryLimitMs = 100;

    /// <summary>
    /// The database URL prefix of this database.
    /// </summary>
    public const string StartUrl = "jdbc:h2:";

    /// <summary>
    /// The file name suffix of file lock files that are used to make sure a
    /// database is open by only one process at any time.
    /// </summary>
    public const string SuffixLockFile = ".lock.db";

    /// <summary>
    /// The file name suffix of a H2 version 1.1 database file.
    /// </summary>
    public const string SuffixOldDatabaseFile = ".data.db";

    /// <summary>
    /// The file name suffix of a MVStore file.
    /// </summary>
    public const string SuffixMvFile = ".mv.db";

    /// <summary>
    /// The file name suffix of a new MVStore file, used when compacting a store.
    /// </summary>
    public const string SuffixMvStoreNewFile = ".newFile";

    /// <summary>
    /// The file name suffix of a temporary MVStore file, used when compacting a store.
    /// </summary>
    public const string SuffixMvStoreTempFile = ".tempFile";

    /// <summary>
    /// The file name suffix of temporary files.
    /// </summary>
    public const string SuffixTempFile = ".temp.db";

    /// <summary>
    /// The file name suffix of trace files.
    /// </summary>
    public const string SuffixTraceFile = ".trace.db";

    /// <summary>
    /// How often we check to see if we need to apply a throttling delay if SET
    /// THROTTLE has been used.
    /// </summary>
    public const int ThrottleDelay = 50;

    /// <summary>
    /// The database URL format in simplified Backus-Naur form.
    /// </summary>
    public const string UrlFormat = StartUrl
        + "{ {.|mem:}[name] | [file:]fileName | "
        + "{tcp|ssl}:[//]server[:port][,server2[:port]]/name }[;key=value...]";

    /// <summary>
    /// The package name of user defined classes.
    /// </summary>
    public const string UserPackage = "org.h2.dynamic";

    /// <summary>
    /// The maximum time in milliseconds to keep the cost of a view.
    /// 10000 means 10 seconds.
    /// </summary>
    public const int ViewCostCacheMaxAge = 10_000;

    /// <summary>
    /// The name of the index cache that is used for temporary views (subqueries
    /// used as tables).
    /// </summary>
    public const int ViewIndexCacheSize = 64;

    /// <summary>
    /// The maximum number of entries in query statistics.
    /// </summary>
    public const int QueryStatisticsMaxEntries = 100;

    /// <summary>
    /// The minimum number of characters in web admin password.
    /// </summary>
    public const int MinWebAdminPasswordLength = 12;

    /// <summary>
    /// Announced version for PgServer.
    /// </summary>
    public const string PgVersion = "8.2.23";

    /// <summary>
    /// The version of this product, consisting of major version, minor version,
    /// and build id.
    /// </summary>
    public static readonly string Version = BuildVersion();

    /// <summary>
    /// The complete version number of this database, consisting of the major version,
    /// the minor version, the build id, and the build date.
    /// </summary>
    public static readonly string FullVersion = Version + " (" + BuildDate + ')';

    private static string BuildVersion()
    {
        string version = VersionMajor + "." + VersionMinor + "." + BuildId;
        if (BuildVendorAndVersion is not null)
        {
            version += '_' + BuildVendorAndVersion;
        }

        if (BuildSnapshot)
        {
            version += "-SNAPSHOT";
        }

        return version;
    }
}
