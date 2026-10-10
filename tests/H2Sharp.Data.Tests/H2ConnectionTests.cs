/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The ADO.NET connection, before there is an engine to connect to.
 *
 * There is nothing to compare against the reference here — the reference reaches a caller
 * through JDBC, and this type has no Java counterpart — so the test is about the shape of the
 * refusal rather than its wording. What matters is that every member that would need a
 * database says so instead of answering: a provider that reports a server version, a database
 * name or a data source it never connected to would be believed.
 *
 * Two things are pinned here that look like trivia and are not. The constructor refuses a null
 * connection string while the property accepts it, because null in a constructor argument is a
 * programming error and null through ADO.NET's [AllowNull] property is the documented way to
 * say "back to the default". And Close does not refuse, because disposing closes, so a refusal
 * would make every `using` over a connection that was never opened throw — which teaches
 * callers to avoid disposing rather than to trust the type.
 */
using System;
using System.Data;
using System.Data.Common;
using H2Sharp;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2ConnectionTests
{
    [Fact]
    public void TheConstructorOverloadTakesTheConnectionString()
    {
        Assert.Equal(string.Empty, new H2Connection().ConnectionString);
        Assert.Equal("Data Source=/tmp/db", new H2Connection("Data Source=/tmp/db").ConnectionString);

        // The property is the other way round on null: ADO.NET declares it [AllowNull], so null
        // means "no connection string", and an empty string is how it is spelled.
        H2Connection connection = new("Data Source=/tmp/db");
        connection.ConnectionString = null;
        Assert.Equal(string.Empty, connection.ConnectionString);
        connection.ConnectionString = "jdbc:h2:mem:test";
        Assert.Equal("jdbc:h2:mem:test", connection.ConnectionString);

        // Null through a constructor argument is a mistake, not a setting.
        Assert.Throws<ArgumentNullException>(() => new H2Connection(null!));
    }

    [Fact]
    public void ItIsAlwaysClosed()
    {
        using H2Connection connection = new("Data Source=/tmp/db");
        Assert.Equal(ConnectionState.Closed, connection.State);
        connection.Close();
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void EverythingThatNeedsAnEngineRefuses()
    {
        using H2Connection connection = new("Data Source=/tmp/db");

        Assert.Throws<TODOException>(() => connection.Open());
        Assert.Throws<TODOException>(() => _ = connection.Database);
        Assert.Throws<TODOException>(() => _ = connection.DataSource);
        Assert.Throws<TODOException>(() => _ = connection.ServerVersion);
        Assert.Throws<TODOException>(() => connection.ChangeDatabase("other"));
        Assert.Throws<TODOException>(() => connection.BeginTransaction());
        Assert.Throws<TODOException>(() => connection.CreateCommand());
    }

    [Fact]
    public void ItIsTheShapeAProviderFactoryExpects()
    {
        // Sealed, so nothing can pretend to be a subclass of ours rather than of ADO.NET's.
        Assert.True(typeof(H2Connection).IsSealed);
        Assert.True(typeof(DbConnection).IsAssignableFrom(typeof(H2Connection)));
    }
}
