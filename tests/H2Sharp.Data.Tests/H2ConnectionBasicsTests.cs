/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The ADO.NET connection, and the in-memory database it opens.
 *
 * There is nothing to compare against the reference here — the reference reaches a caller
 * through JDBC, and this type has no Java counterpart — so the test is about the shape of the
 * refusal rather than its wording: every member that would need the engine says so instead of
 * answering. A provider that reports a server version, a database name or a data source it never
 * connected to would be believed.
 *
 * Three things are pinned here that look like trivia and are not. The constructor refuses a null
 * connection string while the property accepts it, because null in a constructor argument is a
 * programming error and null through ADO.NET's [AllowNull] property is the documented way to
 * say "back to the default". Close does not refuse, because disposing closes, so a refusal would
 * make every `using` over a connection throw. And a named in-memory database is shared between
 * connections and disappears with the last of them, while an unnamed one belongs to the
 * connection that asked for it — the rule the whole `mem:` scheme exists for, and the one a test
 * that only ever opens one connection would never see.
 */
using System;
using System.Data;
using System.Data.Common;
using H2Sharp;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2ConnectionBasicsTests
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
    public void ItIsOpenOnlyBetweenOpenAndClose()
    {
        H2Connection connection = new("jdbc:h2:mem:states");
        Assert.Equal(ConnectionState.Closed, connection.State);

        connection.Open();
        Assert.Equal(ConnectionState.Open, connection.State);

        // Opening an open connection hands out a second session, and the close of either would
        // take the database from the other. The reference refuses, so this does, with the
        // InvalidOperationException the conformance suite demands for a state mistake.
        Assert.Throws<InvalidOperationException>(() => connection.Open());

        connection.Close();
        Assert.Equal(ConnectionState.Closed, connection.State);

        // Closing again, or closing one that was never open, does nothing: that is what makes
        // `using` safe.
        connection.Close();
        new H2Connection("jdbc:h2:mem:never_opened").Close();
    }

    [Fact]
    public void ANamedInMemoryDatabaseIsSharedAndThenForgotten()
    {
        using H2Connection first = new("jdbc:h2:mem:shared");
        using H2Connection second = new("jdbc:h2:mem:shared");
        first.Open();
        second.Open();

        // The same database, not two with the same name: the reference keys open databases by
        // name, and a port that forgot the registry would answer the same to every query and
        // leak a database per name.
        Assert.Same(first.CurrentDatabase, second.CurrentDatabase);

        // An unnamed in-memory database is the other case, and the one a test that always uses a
        // name never reaches: it is not entered into the registry, so two connections to it get
        // two databases.
        using H2Connection unnamedFirst = new("jdbc:h2:mem:");
        using H2Connection unnamedSecond = new("jdbc:h2:mem:");
        unnamedFirst.Open();
        unnamedSecond.Open();
        Assert.NotSame(unnamedFirst.CurrentDatabase, unnamedSecond.CurrentDatabase);
    }

    [Fact]
    public void TheDatabaseGoesAwayWithItsLastSession()
    {
        string name = "mem:gone_when_closed";
        H2Connection first = new("jdbc:h2:" + name);
        first.Open();
        H2Sharp.Engine.Database? database = first.CurrentDatabase;
        Assert.NotNull(database);

        H2Connection second = new("jdbc:h2:" + name);
        second.Open();
        Assert.Same(database, second.CurrentDatabase);
        second.Close();

        // One session still uses it.
        Assert.Equal(1, database.GetSessionCount());
        first.Close();

        // The last one went, so the database is closed and the name is free again. A new
        // connection to the same name therefore gets a new, empty database rather than the old
        // one — which for "mem:" is the whole difference between a name and a file.
        Assert.True(database.IsClosing());
        H2Connection third = new("jdbc:h2:" + name);
        third.Open();
        try
        {
            Assert.NotSame(database, third.CurrentDatabase);
        }
        finally
        {
            third.Close();
        }
    }

    [Fact]
    public void DatabaseDataSourceAndServerVersionAnswer()
    {
        using H2Connection connection = new("jdbc:h2:mem:answers");
        Assert.Equal(string.Empty, connection.Database);
        Assert.Equal(string.Empty, connection.DataSource);
        Assert.Throws<InvalidOperationException>(() => _ = connection.ServerVersion);

        connection.Open();
        try
        {
            Assert.NotEmpty(connection.Database);
            Assert.NotEmpty(connection.DataSource);
            Assert.NotEmpty(connection.ServerVersion);
        }
        finally
        {
            connection.Close();
        }
    }

    [Fact]
    public void EverythingThatIsNotPortedYetRefuses()
    {
        using H2Connection connection = new("jdbc:h2:mem:refusals");
        connection.Open();
        try
        {
            Assert.Throws<TODOException>(() => connection.ChangeDatabase("other"));
        }
        finally
        {
            connection.Close();
        }
    }

    [Fact]
    public void ACommandRunsOnlyOnAnOpenConnection()
    {
        using H2Connection connection = new("jdbc:h2:mem:commands");
        using DbCommand command = connection.CreateCommand();
        Assert.NotNull(command);
        command.CommandText = "SELECT 1";

        // Making it is fine; running it is where the closed connection refuses.
        Assert.Throws<InvalidOperationException>(() => _ = command.ExecuteNonQuery());

        connection.Open();
        try
        {
            // A query run as a non-query still runs; ADO.NET answers -1 for it.
            Assert.Equal(-1, command.ExecuteNonQuery());
        }
        finally
        {
            connection.Close();
        }
    }

    [Fact]
    public void ItIsTheShapeAProviderFactoryExpects()
    {
        // Sealed, so nothing can pretend to be a subclass of ours rather than of ADO.NET's.
        Assert.True(typeof(H2Connection).IsSealed);
        Assert.True(typeof(DbConnection).IsAssignableFrom(typeof(H2Connection)));
    }
}
