// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.TestDb.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): the reference hands out java.sql.Connection from
// DriverManager.getConnection; the port's provider is ADO.NET, so this hands out
// System.Data.Common.DbConnection built straight from the same URL the reference builds.
// The URL keeps the jdbc:h2: prefix, which the port's ConnectionInfo reads.
//
// Modified (port gap): deleteDb deletes through org.h2.tools.DeleteDbFiles, which the
// port does not have yet; the deletion is done here by the same rules
// (FileLister.getDatabaseFiles for the database's file suffixes, then delete). When the
// tool is ported, this goes back to calling it.
using System;
using System.Collections.Generic;
using System.Data.Common;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Store.FileSystem;

namespace H2Sharp.Native.Tests;

/// <summary>
/// The base class for tests that use connections to database.
/// </summary>
public abstract class TestDb : TestBase
{
    /// <summary>
    /// Open a database connection in admin mode. The default user name and
    /// password is used.
    /// </summary>
    /// <param name="name">the database name</param>
    /// <returns>the connection</returns>
    public DbConnection GetConnection(string name)
    {
        return GetConnectionInternal(GetURL(name, true), GetUser(), GetPassword());
    }

    /// <summary>
    /// Open a database connection.
    /// </summary>
    /// <param name="name">the database name</param>
    /// <param name="user">the user name to use</param>
    /// <param name="password">the password to use</param>
    /// <returns>the connection</returns>
    public DbConnection GetConnection(string name, string user, string password)
    {
        return GetConnectionInternal(GetURL(name, false), user, password);
    }

    /// <summary>
    /// Get the database URL for the given database name using the current
    /// configuration options.
    /// </summary>
    /// <param name="name">the database name</param>
    /// <param name="admin">true if the current user is an admin</param>
    /// <returns>the database URL</returns>
    protected string GetURL(string name, bool admin)
    {
        string url;
        if (name.StartsWith("jdbc:", StringComparison.Ordinal))
        {
            name = AddOption(name, "MV_STORE", "true");
            return name;
        }

        if (admin)
        {
            // name = AddOption(name, "RETENTION_TIME", "10");
            // name = AddOption(name, "WRITE_DELAY", "10");
        }

        int idx = name.IndexOf(':');
        if (idx == -1 && Config.Memory)
        {
            name = "mem:" + name;
        }
        else
        {
            if (idx < 0 || idx > 10)
            {
                // index > 10 if in options
                name = GetBaseDir() + "/" + name;
            }
        }

        if (Config.Networked)
        {
            if (Config.Ssl)
            {
                url = "ssl://localhost:" + Config.GetPort() + "/" + name;
            }
            else
            {
                url = "tcp://localhost:" + Config.GetPort() + "/" + name;
            }
        }
        else if (Config.GoogleAppEngine)
        {
            url = "gae://" + name
                + ";FILE_LOCK=NO;AUTO_SERVER=FALSE;DB_CLOSE_ON_EXIT=FALSE";
        }
        else
        {
            url = name;
        }

        url = AddOption(url, "MV_STORE", "true");
        url = AddOption(url, "MAX_COMPACT_TIME", "0"); // to speed up tests
        if (!Config.Memory)
        {
            if (Config.SmallLog && admin)
            {
                url = AddOption(url, "MAX_LOG_SIZE", "1");
            }
        }

        if (Config.TraceSystemOut)
        {
            url = AddOption(url, "TRACE_LEVEL_SYSTEM_OUT", "2");
        }

        if (Config.TraceLevelFile > 0 && admin)
        {
            url = AddOption(url, "TRACE_LEVEL_FILE", "" + Config.TraceLevelFile);
            url = AddOption(url, "TRACE_MAX_FILE_SIZE", "8");
        }

        if (Config.ThrottleDefault > 0)
        {
            url = AddOption(url, "THROTTLE", "" + Config.ThrottleDefault);
        }
        else if (Config.Throttle > 0)
        {
            url = AddOption(url, "THROTTLE", "" + Config.Throttle);
        }

        url = AddOption(url, "LOCK_TIMEOUT", "" + Config.LockTimeout);
        if (Config.DiskUndo && admin)
        {
            url = AddOption(url, "MAX_MEMORY_UNDO", "3");
        }

        if (Config.Big && admin)
        {
            // force operations to disk
            url = AddOption(url, "MAX_OPERATION_MEMORY", "1");
        }

        if (Config.Lazy)
        {
            url = AddOption(url, "LAZY_QUERY_EXECUTION", "1");
        }

        if (Config.CacheType is not null && admin)
        {
            url = AddOption(url, "CACHE_TYPE", Config.CacheType);
        }

        if (Config.DiskResult && admin)
        {
            url = AddOption(url, "MAX_MEMORY_ROWS", "100");
            url = AddOption(url, "CACHE_SIZE", "0");
        }

        if (Config.Cipher is not null)
        {
            url = AddOption(url, "CIPHER", Config.Cipher);
        }

        if (Config.Collation is not null)
        {
            url = AddOption(url, "COLLATION", Config.Collation);
        }

        return Constants.StartUrl + url;
    }

    private static string AddOption(string url, string option, string value)
    {
        if (url.IndexOf(";" + option + "=", StringComparison.Ordinal) < 0)
        {
            url += ";" + option + "=" + value;
        }

        return url;
    }

    private static DbConnection GetConnectionInternal(string url, string user,
        string password)
    {
        string connectionString = url;
        if (user.Length > 0 || password.Length > 0)
        {
            connectionString += ";USER=" + user + ";PASSWORD=" + password;
        }

        H2Connection connection = new(connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Delete all database files for this database.
    /// </summary>
    /// <param name="name">the database name</param>
    protected void DeleteDb(string name)
    {
        DeleteDb(GetBaseDir(), name);
    }

    /// <summary>
    /// Delete all database files for a database.
    /// </summary>
    /// <param name="dir">the directory where the database files are located</param>
    /// <param name="name">the database name</param>
    /// <remarks>
    /// The reference calls DeleteDbFiles.execute(dir, name, true), which lists the
    /// database's files through FileLister and deletes them, silently. The port has
    /// neither the tool nor the lister yet, so the same walk is done here: a file in the
    /// directory whose name starts with the database name and ends with one of the
    /// database's suffixes goes, and so does a directory left empty by it. When
    /// DeleteDbFiles is ported, this goes back to calling it.
    /// </remarks>
    protected void DeleteDb(string dir, string name)
    {
        string start = name + '.';
        foreach (FilePath path in FilePath.Get(dir).NewDirectoryStream())
        {
            string f = path.ToString();
            bool ok = f.EndsWith(Constants.SuffixMvFile, StringComparison.Ordinal)
                || f.EndsWith(Constants.SuffixLockFile, StringComparison.Ordinal)
                || f.EndsWith(Constants.SuffixTempFile, StringComparison.Ordinal)
                || f.EndsWith(Constants.SuffixTraceFile, StringComparison.Ordinal);
            if (ok && path.GetName().StartsWith(start, StringComparison.Ordinal))
            {
                path.Delete();
            }
        }
    }

    /// <summary>
    /// Build a child process.
    /// </summary>
    /// <param name="name">the name</param>
    /// <param name="childClass">the class</param>
    /// <param name="jvmArgs">the argument list</param>
    /// <returns>the process builder</returns>
    /// <remarks>
    /// The reference returns java.lang.ProcessBuilder and launches a JVM running the child
    /// test class; .NET describes a process to start with ProcessStartInfo instead, so
    /// that is what this returns. The JVM mechanics (the child classpath and
    /// SelfDestructor) are not portable; the crash and kill tests that need them are not
    /// ported yet either.
    /// </remarks>
    public System.Diagnostics.ProcessStartInfo BuildChild(string name, Type childClass,
        params string[] jvmArgs)
    {
        _ = name;
        _ = childClass;
        _ = jvmArgs;
        throw new TODOException("the child process launcher (SelfDestructor on the JVM)");
    }

    /// <summary>
    /// A test that runs in a child process the parent built.
    /// </summary>
    public abstract class Child : TestDb
    {
        private string? url;
        private string? user;
        private string? password;

        /// <summary>
        /// Builds the child from the arguments the parent passed.
        /// </summary>
        /// <param name="args">the arguments</param>
        protected Child(params string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if ("-url".Equals(args[i], StringComparison.Ordinal))
                {
                    url = args[++i];
                }
                else if ("-user".Equals(args[i], StringComparison.Ordinal))
                {
                    user = args[++i];
                }
                else if ("-password".Equals(args[i], StringComparison.Ordinal))
                {
                    password = args[++i];
                }
            }
        }

        /// <summary>
        /// Open the connection to the database the parent named.
        /// </summary>
        /// <returns>the connection</returns>
        public DbConnection GetConnection()
        {
            return GetConnection(url!, user!, password!);
        }
    }
}
