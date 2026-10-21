/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The name of the catalogue, seen through ADO.NET.
 *
 * The reference's JDBC names the catalogue after the short name of the database: the last
 * piece of the name, "DB" for "mem:db" and "UNNAMED" for a name with no piece. The whole
 * connection string is not a name: it is the way the database was reached.
 */
using System;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2CatalogTests
{
    [Fact]
    public void TheCatalogNamesTheDatabaseByItsShortName()
    {
        using H2Connection connection = new("jdbc:h2:mem:ado_catalog");

        Assert.Equal(string.Empty, connection.Database);

        connection.Open();

        Assert.Equal("ADO_CATALOG", connection.Database);
    }
}
