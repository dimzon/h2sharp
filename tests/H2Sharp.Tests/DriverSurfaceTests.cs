/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The ADO.NET driver registration: the factory under the invariant name, the version the
 * driver answers with, and the load/unload pair the host calls.
 */
using System;
using System.Data.Common;
using H2Sharp;
using H2Sharp.Data;
using H2Sharp.Engine;
using Xunit;

namespace H2Sharp.Tests;

public sealed class DriverSurfaceTests
{
    [Fact]
    public void TheDriverRegistersItsFactoryAndAnswersItsVersion()
    {
        Driver driver = Driver.Load();
        Assert.True(driver.Compliant());
        Assert.Equal(Constants.VersionMajor, driver.GetMajorVersion());
        Assert.Equal(Constants.VersionMinor, driver.GetMinorVersion());
        Assert.Equal("H2Sharp", Driver.InvariantName);

        // The registered factory builds the port's connection.
        DbProviderFactory factory = DbProviderFactories.GetFactory(Driver.InvariantName);
        Assert.IsType<H2ProviderFactory>(factory);
        using (DbConnection connection = factory.CreateConnection()!)
        {
            Assert.IsType<H2Connection>(connection);
            connection.ConnectionString =
                "Data Source=driver_" + Guid.NewGuid().ToString("N") + ";Storage=Memory";
            connection.Open();
            Assert.Equal(System.Data.ConnectionState.Open, connection.State);
            connection.Close();
        }

        // A second load keeps the registration; unloading twice is the same unload.
        _ = Driver.Load();
        Driver.Unload();
        Driver.Unload();
        Assert.ThrowsAny<Exception>(
            () => DbProviderFactories.GetFactory(Driver.InvariantName));

        _ = Driver.Load();
    }
}
