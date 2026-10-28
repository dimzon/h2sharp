// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.Driver.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: only the provider registration surface remains. The JDBC URL surface
// (connect/acceptsURL/default connection) and its connection-string parsing were
// removed; .NET callers use H2Connection with the classic ADO.NET connection string.
using System;
using System.Data.Common;
using H2Sharp.Data;
using H2Sharp.Engine;
using ErrorFactory = H2Sharp.Message.DbException;

namespace H2Sharp;

/// <summary>
/// The database driver.
/// </summary>
public class Driver
{
    private static readonly Driver Instance = new();

    private static bool registered;

    /// <summary>
    /// The invariant name the provider factory registers under.
    /// </summary>
    public const string InvariantName = "H2Sharp";

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

}

