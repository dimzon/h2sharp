// Copyright 2004-2026 H2 Group.
// Copyright 2026 H2Sharp contributors.
// Ported from org.h2.test.jdbc.TestDriver.
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified (API): the JDBC DriverManager takes a URL plus a Properties bag; the ADO.NET
// provider takes one connection string, and the properties ride in it as its options
// part.
//
// Modified (deferred, see MIGRATION_STATUS.md): testDriverObject and testURLs exercise
// the JDBC driver registration machinery (Driver.load/unload, DriverManager.getDriver,
// acceptsURL). The .NET mapping proposes DbProviderFactories as the DriverManager and
// H2Sharp.Driver as its client, but getDriver-by-URL has no registry analog and the
// "not registered" answer differs (JDBC SQLException against .NET's own exception), so
// the two parts are skipped and put on the discussion list. The class itself is ported
// (H2Sharp/Driver.cs); testSettingsAsProperties, which is plain ADO.NET, is ported
// below.
using Xunit;

namespace H2Sharp.Native.Tests.Jdbc;

/// <summary>
/// Tests the database driver.
/// </summary>
public sealed class TestDriver : TestDb
{
    /// <summary>
    /// Run just this test.
    /// </summary>
    [H2Fact]
    public override void Test()
    {
        TestSettingsAsProperties();
        // testDriverObject, testURLs: deferred, see the header and MIGRATION_STATUS.md.
    }

    private void TestSettingsAsProperties()
    {
        string url = GetURL("jdbc:h2:mem:driver", true);
        // The reference also passes an "unknown" property, which the JDBC Properties bag
        // silently ignores; a connection string has no such bag, so the unknown key has
        // nowhere to ride and is dropped here.
        using DbConnection conn = new H2Connection(ToClassicConnectionString(url
            + ";MAX_COMPACT_TIME=1234"));
        conn.Open();
        DbCommand command = conn.CreateCommand();
        command.CommandText =
            "SELECT SETTING_VALUE FROM INFORMATION_SCHEMA.SETTINGS WHERE SETTING_NAME = 'MAX_COMPACT_TIME'";
        using (DbDataReader rs = command.ExecuteReader())
        {
            AssertTrue(rs.Read());
            AssertEquals(1234, rs.GetInt32(0));
        }
    }
}
