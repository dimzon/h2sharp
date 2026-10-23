// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.Driver.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the JDBC registration surface maps onto .NET's. The reference registers
// itself with DriverManager and answers DriverManager.getDriver through it; .NET's
// registry is DbProviderFactories, so load() and unload() register and unregister the
// provider factory there, and the driver's properties ride in the connection string
// instead of a java.util.Properties bag. The default connection (used for SQL
// procedures on the same thread) is an AsyncLocal, the way the session is.
using System;
using System.Data.Common;
using System.Threading;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Message;
using ErrorFactory = H2Sharp.Message.DbException;

namespace H2Sharp;

/// <summary>
/// The database driver.
/// </summary>
public class Driver
{
    private static readonly Driver Instance = new();

    private static readonly string DefaultUrl = "jdbc:default:connection";

    private static readonly AsyncLocal<DbConnection?> DefaultConnection = new();

    private static bool registered;

    /// <summary>
    /// The invariant name the provider factory registers under.
    /// </summary>
    public const string InvariantName = "H2Sharp";

    /// <summary>
    /// Opens a connection to the database the URL names, with the properties the caller
    /// passes; the properties ride in the connection string as its options part.
    /// </summary>
    /// <param name="url">the database URL</param>
    /// <param name="info">the connection string options, or <c>null</c> for none</param>
    /// <returns>the connection, or <c>null</c> when the URL is not the driver's</returns>
    public virtual DbConnection? Connect(string? url, string? info)
    {
        if (url is null)
        {
            throw ErrorFactory.Get(ErrorCode.UrlFormatError2, Constants.UrlFormat);
        }
        else if (url.StartsWith(Constants.StartUrl, StringComparison.Ordinal))
        {
            string connectionString = info is null ? url : url + ";" + info;
            H2Connection connection = new(connectionString);
            connection.Open();
            return connection;
        }
        else if (url.Equals(DefaultUrl, StringComparison.Ordinal))
        {
            return DefaultConnection.Value;
        }
        else
        {
            return null;
        }
    }

    /// <summary>
    /// Whether this driver can open a connection to the database the URL names.
    /// </summary>
    /// <param name="url">the database URL</param>
    /// <returns><c>true</c> when the URL is the driver's</returns>
    public virtual bool AcceptsURL(string? url)
    {
        if (url is null)
        {
            throw ErrorFactory.Get(ErrorCode.UrlFormatError2, Constants.UrlFormat);
        }
        else if (url.StartsWith(Constants.StartUrl, StringComparison.Ordinal))
        {
            return true;
        }
        else if (url.Equals(DefaultUrl, StringComparison.Ordinal))
        {
            return DefaultConnection.Value is not null;
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// The major version of the database.
    /// </summary>
    /// <returns>the major version</returns>
    public virtual int GetMajorVersion()
    {
        return Constants.VersionMajor;
    }

    /// <summary>
    /// The minor version of the database.
    /// </summary>
    /// <returns>the minor version</returns>
    public virtual int GetMinorVersion()
    {
        return Constants.VersionMinor;
    }

    /// <summary>
    /// Whether the driver is JDBC compliant; the driver is ADO.NET compliant instead.
    /// </summary>
    /// <returns><c>true</c></returns>
    public virtual bool Compliant()
    {
        return true;
    }

    /// <summary>
    /// Registers the driver, if it is not registered yet.
    /// </summary>
    /// <returns>the driver</returns>
    public static Driver Load()
    {
        lock (typeof(Driver))
        {
            try
            {
                if (!registered)
                {
                    registered = true;
                    DbProviderFactories.RegisterFactory(InvariantName, H2ProviderFactory.Instance);
                }
            }
            catch (ArgumentException e)
            {
                ErrorFactory.TraceThrowable(e);
            }
        }

        return Instance;
    }

    /// <summary>
    /// Unregisters the driver, if it is registered.
    /// </summary>
    public static void Unload()
    {
        lock (typeof(Driver))
        {
            try
            {
                if (registered)
                {
                    registered = false;
                    DbProviderFactories.UnregisterFactory(InvariantName);
                }
            }
            catch (ArgumentException e)
            {
                ErrorFactory.TraceThrowable(e);
            }
        }
    }

    /// <summary>
    /// Sets the connection SQL procedures on this thread run with, or clears it.
    /// </summary>
    /// <param name="c">the connection, or <c>null</c> to clear</param>
    public static void SetDefaultConnection(DbConnection? c)
    {
        DefaultConnection.Value = c;
    }
}
