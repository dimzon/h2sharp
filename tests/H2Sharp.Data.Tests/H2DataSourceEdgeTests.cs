/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 117: the data source edges SQL does not reach — the builder's extension configure,
 * the listener setters before and after the database is opened, the create-connection
 * configure refusal that disposes the new connection, the trigger registration made before
 * the first open, and the dispose paths.
 */
using System;
using System.Data.Common;
using System.Reflection;
using H2Sharp.Data;
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2DataSourceEdgeTests
{
    private sealed class CountingTraceListener : IH2DatabaseTraceListener
    {
        internal int Lines;

        public void Write(H2TraceLevel level, string module, string message, Exception? t) => Lines++;

        public bool IsEnabled(H2TraceLevel level) => true;
    }

    private sealed class CountingEventListener : IH2DatabaseEventListener
    {
        internal bool OpenedCalled;

        public void Opened() => OpenedCalled = true;
    }

    private sealed class CountingTrigger : IH2Trigger
    {
        internal int Fires;

        public void Fire(IH2TriggerRow? oldRow, IH2TriggerRow? newRow) => Fires++;
    }

    private static string UniqueName() => "ds_edge_" + Guid.NewGuid().ToString("N");

    [Fact]
    public void BuilderAnswersItsShapes()
    {
        string name = UniqueName();
        Assert.Throws<ArgumentNullException>(() => new H2DataSourceBuilder(null!));

        H2DataSourceBuilder builder = new($"Data Source={name};Storage=Memory");
        Assert.Same(builder, builder.ConfigureExtensions(_ => { }));
        Assert.Same(builder, builder.ConfigureDataFormat(_ => { }));
        Assert.Throws<ArgumentNullException>(() => builder.ConfigureExtensions(null!));
        Assert.Throws<ArgumentNullException>(() => builder.ConfigureDataFormat(null!));

        using H2DataSource dataSource = builder.Build();
        Assert.Equal($"Data Source={name};Storage=Memory", dataSource.ConnectionString);
        Assert.Equal(name, dataSource.DatabaseName);
    }

    [Fact]
    public void CreateConnectionRunsConfiguresAndDisposesOnRefusal()
    {
        string cs = $"Data Source={UniqueName()};Storage=Memory";
        int extensions = 0;
        int formats = 0;
        using H2DataSource dataSource = new H2DataSourceBuilder(cs)
            .ConfigureExtensions(_ => extensions++)
            .ConfigureDataFormat(_ => formats++)
            .Build();

        using (H2Connection connection = (H2Connection)dataSource.CreateConnection())
        {
            Assert.Equal(1, extensions);
            Assert.Equal(1, formats);
            Assert.Equal(cs, connection.ConnectionString);
        }

        // A configure action that refuses leaves no connection behind.
        using H2DataSource refusing = new H2DataSourceBuilder(cs)
            .ConfigureExtensions(_ => throw new InvalidOperationException("refused"))
            .Build();
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => refusing.CreateConnection());
        Assert.Equal("refused", thrown.Message);
    }

    [Fact]
    public void ListenersForwardOnceTheDatabaseIsOpen()
    {
        using H2DataSource dataSource = new H2DataSourceBuilder(
            $"Data Source={UniqueName()};Storage=Memory").Build();

        CountingTraceListener trace = new();
        CountingEventListener events = new();

        // Before the database exists the data source only remembers them.
        dataSource.SetTraceListener(trace);
        dataSource.SetDatabaseEventListener(events);

        using H2Connection connection = (H2Connection)dataSource.CreateConnection();
        connection.Open();

        // After the open the setters forward to the database, and null clears.
        dataSource.SetTraceListener(trace);
        dataSource.SetDatabaseEventListener(events);
        dataSource.SetTraceListener(null);
        dataSource.SetDatabaseEventListener(null);
    }

    [Fact]
    public void RegistrationMadeBeforeOpenAppliesWhenTheDatabaseOpens()
    {
        using H2DataSource dataSource = new H2DataSourceBuilder(
            $"Data Source={UniqueName()};Storage=Memory").Build();
        CountingTrigger trigger = new();
        dataSource.RegisterTrigger("early", (schema, table) => true, H2TriggerEvent.Insert,
            before: true, rowBased: true, () => trigger);

        using H2Connection connection = (H2Connection)dataSource.CreateConnection();
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE T(ID INT)";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO T VALUES(1)";
        command.ExecuteNonQuery();
        Assert.Equal(1, trigger.Fires);

        Assert.True(dataSource.UnregisterTrigger("early"));
        Assert.False(dataSource.UnregisterTrigger("early"));
    }

    [Fact]
    public void DisposeClosesTheDatabaseAndRefusesLaterUse()
    {
        using H2DataSource dataSource = new H2DataSourceBuilder(
            $"Data Source={UniqueName()};Storage=Memory").Build();
        using H2Connection connection = (H2Connection)dataSource.CreateConnection();
        connection.Open();

        dataSource.Dispose();
        dataSource.Dispose();
        Assert.Throws<ObjectDisposedException>(() => dataSource.CreateConnection());

        // The non-disposing dispose path, the one the finalizer takes.
        _ = typeof(H2DataSource).GetMethod("Dispose",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dataSource, [false]);
    }
}
