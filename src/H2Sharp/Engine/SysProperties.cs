/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.engine.SysProperties.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Modified: Java system properties (-D on the JVM command line, System.setProperty)
 * have no .NET equivalent, so `h2.*` settings are read from **environment variables**
 * via H2Sharp.Util.Utils.GetProperty. Names are unchanged, so `h2.traceIO` in Java
 * becomes the environment variable `h2.traceIO` in .NET. This is recorded in
 * docs/PORTING.md.
 *
 * `user.home` has no .NET counterpart either; UserHome falls back to
 * Environment.SpecialFolder.UserProfile.
 *
 * The following members are intentionally NOT ported because they are specific to
 * Java, the H2 web console, or H2's TCP server mode, all of which are out of scope:
 *   JAVA_SYSTEM_COMPILER, NIO_LOAD_MAPPED, NIO_CLEANER_HACK,
 *   USE_THREAD_CONTEXT_CLASS_LOADER, JAVA_OBJECT_SERIALIZER, AUTH_CONFIG_FILE,
 *   URL_MAP, BIND_ADDRESS, SOCKET_CONNECT_RETRY, SOCKET_CONNECT_TIMEOUT,
 *   MAX_RECONNECT, SERVER_CACHED_OBJECTS, SERVER_RESULT_SET_FETCH_SIZE,
 *   CLIENT_TRACE_DIRECTORY, LOB_CLIENT_MAX_SIZE_MEMORY, DATASOURCE_TRACE_LEVEL,
 *   CONSOLE_MAX_TABLES_LIST_INDEXES, CONSOLE_MAX_TABLES_LIST_COLUMNS,
 *   CONSOLE_MAX_PROCEDURES_LIST_COLUMNS, CONSOLE_STREAM, CONSOLE_TIMEOUT,
 *   THREAD_DEADLOCK_DETECTOR, ALLOWED_CLASSES, H2_BROWSER, H2_SCRIPT_DIRECTORY.
 * Most depend on org.h2.security, org.h2.server, org.h2.jdbcx, org.h2.tools or
 * org.h2.util.SourceCompiler, none of which are being ported. Re-add any of them if
 * the corresponding H2Sharp feature is ever implemented.
 *
 * `h2.check` no longer needs the Android special case: H2Sharp has no Android target,
 * so the default is unconditionally true.
 */
using System;
using H2Sharp.Util;

namespace H2Sharp.Engine;

/// <summary>
/// The constants defined in this class are initialized from environment variables
/// (the .NET analogue of Java system properties).
/// </summary>
public static class SysProperties
{
    private const string H2BaseDir = "h2.baseDir";

    /// <summary>
    /// <c>user.home</c> (empty string if not set). It is usually set by the system, and
    /// used as a replacement for ~ in file names.
    /// </summary>
    public static readonly string UserHome = ResolveUserHome();

    /// <summary>
    /// Optional additional checks in the database engine. Default <c>true</c>.
    /// </summary>
    public static readonly bool Check = Utils.GetProperty("h2.check", true);

    /// <summary>
    /// The cache size for collation keys (in elements). Used when a collator has been
    /// set for the database. Default 32000.
    /// </summary>
    public static readonly int CollatorCacheSize =
        Utils.GetProperty("h2.collatorCacheSize", 32_000);

    /// <summary>
    /// The minimum delay in milliseconds before an exception is thrown for using the
    /// wrong user name or password. This slows down brute force attacks. The delay is
    /// reset to this value after a successful login. Unsuccessful logins will double
    /// the time until <see cref="DelayWrongPasswordMax"/>. To disable the delay, set
    /// this property to 0. Default 250.
    /// </summary>
    public static readonly int DelayWrongPasswordMin =
        Utils.GetProperty("h2.delayWrongPasswordMin", 250);

    /// <summary>
    /// The maximum delay in milliseconds before an exception is thrown for using the
    /// wrong user name or password. The value 0 means there is no maximum delay.
    /// Default 4000.
    /// </summary>
    public static readonly int DelayWrongPasswordMax =
        Utils.GetProperty("h2.delayWrongPasswordMax", 4000);

    /// <summary>
    /// Close LOB files between read operations. Default <c>false</c>.
    /// Not a constant in H2 either; it is mutable so that <c>SET LOB_CLOSE_BETWEEN_READS</c>
    /// can change it at runtime.
    /// </summary>
    public static bool LobCloseBetweenReads =
        Utils.GetProperty("h2.lobCloseBetweenReads", false);

    /// <summary>
    /// Number of times to retry file delete and rename. On Windows, files can't be
    /// deleted if they are open. Waiting a bit can help. Default 16, minimum 1.
    /// </summary>
    public static readonly int MaxFileRetry =
        Math.Max(1, Utils.GetProperty("h2.maxFileRetry", 16));

    /// <summary>
    /// The default maximum number of rows to be kept in memory in a result set.
    /// Default 40000 per GB of available RAM.
    /// </summary>
    public static readonly int MaxMemoryRows =
        GetAutoScaledForMemoryProperty("h2.maxMemoryRows", 40_000);

    /// <summary>
    /// The maximum size of a LOB value that is written as data to the trace system.
    /// Default 65535.
    /// </summary>
    public static readonly long MaxTraceDataLength =
        Utils.GetProperty("h2.maxTraceDataLength", 65535L);

    /// <summary>
    /// Cache commonly used values (numbers, strings). There is a shared cache for all
    /// values. Default <c>true</c>.
    /// </summary>
    public static readonly bool ObjectCache = Utils.GetProperty("h2.objectCache", true);

    /// <summary>
    /// The maximum size (precision) of an object in the cache. Default 4096.
    /// </summary>
    public static readonly int ObjectCacheMaxPerElementSize =
        Utils.GetProperty("h2.objectCacheMaxPerElementSize", 4096);

    /// <summary>
    /// The maximum number of objects in the cache. This value must be a power of 2.
    /// Default 1024.
    /// </summary>
    public static readonly int ObjectCacheSize = ComputeObjectCacheSize();

    /// <summary>
    /// Default client encoding for the PostgreSQL server. Default UTF-8.
    /// </summary>
    public static readonly string PgDefaultClientEncoding =
        Utils.GetPropertyOrDefault("h2.pgClientEncoding", "UTF-8");

    /// <summary>
    /// The prefix for temporary files in the temp directory. Default <c>h2.temp</c>.
    /// </summary>
    public static readonly string PrefixTempFile =
        Utils.GetPropertyOrDefault("h2.prefixTempFile", "h2.temp");

    /// <summary>
    /// Throw error if the transaction's auto-commit property is true when a commit is
    /// executed. Default <c>false</c>. Mutable so that
    /// <c>SET FORCE_AUTOCOMMIT_OFF_ON_COMMIT</c> can change it at runtime.
    /// </summary>
    public static bool ForceAutoCommitOffOnCommit =
        Utils.GetProperty("h2.forceAutoCommitOffOnCommit", false);

    /// <summary>
    /// The maximum file size of a split file is 1L &lt;&lt; SplitFileSizeShift.
    /// Default 30.
    /// </summary>
    public static readonly long SplitFileSizeShift =
        Utils.GetProperty("h2.splitFileSizeShift", 30L);

    /// <summary>
    /// Trace all I/O operations. Default <c>false</c>.
    /// </summary>
    public static readonly bool TraceIo = Utils.GetProperty("h2.traceIO", false);

    /// <summary>
    /// Sets the base directory.
    /// </summary>
    /// <param name="dir">base directory</param>
    public static void SetBaseDir(string dir)
    {
        if (!dir.EndsWith("/", StringComparison.Ordinal))
        {
            dir += "/";
        }

        Environment.SetEnvironmentVariable(H2BaseDir, dir);
    }

    /// <summary>
    /// Returns the base directory, or null if it was never set.
    /// </summary>
    /// <returns>base directory</returns>
    public static string? GetBaseDir() => Utils.GetProperty(H2BaseDir, null);

    private static int ComputeObjectCacheSize()
    {
        try
        {
            return MathUtils.NextPowerOf2(Utils.GetProperty("h2.objectCacheSize", 1024));
        }
        catch (ArgumentException e)
        {
            throw new InvalidOperationException("Invalid h2.objectCacheSize", e);
        }
    }

    private static string ResolveUserHome()
    {
        string? fromEnvironment = Utils.GetProperty("user.home", null);
        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            return fromEnvironment;
        }

        try
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>
    /// Attempts to auto-scale some properties to take advantage of more powerful
    /// machines out of the box. The defaults are assumed to be correct for roughly 1 GB
    /// of memory and are scaled up when more is available.
    /// </summary>
    private static int GetAutoScaledForMemoryProperty(string key, int defaultValue)
    {
        string? s = Utils.GetProperty(key, null);
        if (s is not null && Utils.TryParseInt(s, out int parsed))
        {
            return parsed;
        }

        return Utils.ScaleForAvailableMemory(defaultValue);
    }
}
