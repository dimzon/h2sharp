/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 291: the three remaining uncovered D-004 lines are refusal stubs, not missing code
 * behind a feature gate. They can and must be covered directly by asking for the operation and
 * checking that it names the missing feature.
 */
using System;
using System.Data.Common;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Jdbc.Meta;
using Xunit;

namespace H2Sharp.Tests;

public sealed class D004StubSurfaceTests
{
    [Fact]
    public void MetadataPrivilegeStubsNameTheMissingObjects()
    {
        Database database = new(ConnectionInfo.Memory("d004_metadata"));
        SessionLocal session = new(database, "sa");
        try
        {
            DatabaseMetaLocal meta = new(session);
            Assert.Contains("D-004", Assert.Throws<TODOException>(
                () => meta.GetColumnPrivileges(null, null, null, null)).Message, StringComparison.Ordinal);
            Assert.Contains("D-004", Assert.Throws<TODOException>(
                () => meta.GetTablePrivileges(null, null, null)).Message, StringComparison.Ordinal);
        }
        finally
        {
            database.Close();
        }
    }

    [Fact]
    public void PgGetUserByIdNamesTheMissingUsers()
    {
        using H2Connection connection = new(
            "Data Source=d004_pg_" + Guid.NewGuid().ToString("N") + ";Storage=Memory;MODE=PostgreSQL");
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT PG_GET_USERBYID(1)";
        TODOException e = Assert.Throws<TODOException>(() => command.ExecuteScalar());
        Assert.Contains("users and roles are not ported", e.Message, StringComparison.Ordinal);
    }
}
