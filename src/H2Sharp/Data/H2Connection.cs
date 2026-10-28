/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * The ADO.NET provider's connection. No Java counterpart, and not a transliteration:
 * this is the surface a .NET application talks to, and it has no equivalent in the
 * reference because it is not part of H2 either — the reference reaches a caller through
 * JDBC, and this type is what the same job looks like on this side.
 *
 * Everything that needs the database refuses. That is the honest state of the port: there
 * is no engine yet (no Database, no Session, no Parser), so a connection string is text
 * this type can hold and nothing more. The refusals are TODOException rather than a
 * plausible answer, because a provider that answers "version 2.5" or "database H2" without
 * having connected is worse than one that says it cannot.
 *
 * Naming. H2Connection rather than Connection, because `Connection` in a namespace that is
 * imported next to System.Data.Common collides with DbConnection, SqlConnection and every
 * other provider's own type; the prefix also says which product the type belongs to, which
 * is the one thing a provider type name is for.
 */
using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using H2Sharp.Engine;
using H2Sharp.Extensions;
using H2Sharp.Util;
using H2DbException = H2Sharp.Message.DbException;
// The engine's isolation level and the ADO.NET one share the name; the transaction surface
// of this file speaks ADO.NET, the session surface speaks the engine's.
using AdoIsolationLevel = System.Data.IsolationLevel;
using EngineIsolationLevel = H2Sharp.Engine.IsolationLevel;

namespace H2Sharp.Data;

/// <summary>
/// A connection to an H2Sharp database. This is the entry point of the ADO.NET provider.
/// </summary>
/// <remarks>
/// The connection implements <see cref="IH2Test"/> explicitly: the seams a test needs are
/// not members of a connection that nobody is testing, and a caller who wants them casts
/// the connection to the interface first.
/// </remarks>
public sealed class H2Connection : DbConnection, IH2Test, IH2Extensions, IH2RawExtensions
{
    private string connectionString = string.Empty;

    private SessionLocal? session;


    private H2Transaction? currentTransaction;

    private Func<DateTimeOffset>? systemClockProvider;

    private Func<long?, Random>? randomGeneratorProvider;

    private Func<Guid>? uuidProvider;

    private Func<Util.TimeZoneProvider>? timeZoneProviderProvider;

    private readonly ExtensionConfiguration extensionConfiguration = new();

    private bool disposed;

    /// <inheritdoc/>
    Func<DateTimeOffset>? IH2Test.SystemClockProvider
    {
        get => systemClockProvider;
        set => systemClockProvider = value;
    }

    /// <inheritdoc/>
    Func<long?, Random>? IH2Test.RandomGeneratorProvider
    {
        get => randomGeneratorProvider;
        set => randomGeneratorProvider = value;
    }

    /// <inheritdoc/>
    Func<Guid>? IH2Test.UuidProvider
    {
        get => uuidProvider;
        set => uuidProvider = value;
    }

    /// <inheritdoc/>
    Func<Util.TimeZoneProvider>? IH2Test.TimeZoneProviderProvider
    {
        get => timeZoneProviderProvider;
        set => timeZoneProviderProvider = value;
    }

    /// <summary>
    /// Creates a connection with no connection string.
    /// </summary>
    public H2Connection()
    {
    }

    /// <summary>
    /// Creates a connection for the given connection string.
    /// </summary>
    /// <param name="connectionString">the connection string</param>
    public H2Connection(string connectionString)
    {
        this.connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <summary>
    /// Gets or sets the connection string. Setting it to null or empty is the ADO.NET
    /// spelling of "no connection string", so it is stored rather than refused: the base
    /// contract allows it, and refusing would make a data source throw before it ever got
    /// to say anything useful. The <see cref="AllowNullAttribute"/> is not decoration —
    /// <see cref="DbConnection"/> declares it, so an override that drops it is a nullability
    /// error rather than a warning.
    /// </summary>
    /// <remarks>
    /// Set on an open connection is refused, and a string neither side reads is refused
    /// too: only the classic key=value form is accepted. `Data Source` names the memory key
    /// or file path, `Storage` selects Memory or File, and `Mode` fixes the compatibility
    /// mode. JDBC URLs and credentials are not accepted.
    /// </remarks>
    [AllowNull]
    public override string ConnectionString
    {
        get => connectionString;
        set
        {
            if (session is not null)
            {
                throw new InvalidOperationException("The connection string cannot be changed while open");
            }

            string text = value ?? string.Empty;
            if (text.Length > 0)
            {
                ValidateConnectionString(text);
            }

            connectionString = text;
        }
    }

    /// <summary>
    /// Checks that a connection string is one this provider opens.
    /// </summary>
    /// <param name="text">the connection string</param>
    private static void ValidateConnectionString(string text)
    {
        try
        {
            _ = new H2ConnectionStringBuilder(text).ToConnectionInfo();
        }
        catch (Exception e)
        {
            throw new ArgumentException("Invalid connection string: " + text, e);
        }
    }

    /// <summary>
    /// Gets the name of the current database, or the initial catalog.
    /// </summary>
    /// <remarks>
    /// The reference's JDBC names the catalogue after the short name of the database: the
    /// last piece of the name, "DB" for "mem:db" and "UNNAMED" for a name with no piece.
    /// The whole connection string is not a name: it is the way the database was reached.
    /// <para>
    /// Empty while closed: there is no database yet, and a name invented from the
    /// connection string would be a guess that reads like an answer.
    /// </para>
    /// </remarks>
    public override string Database
        => session is null ? string.Empty : CurrentDatabase?.GetShortName() ?? string.Empty;

    /// <summary>
    /// Gets the name of the server this connection is attached to.
    /// </summary>
    /// <remarks>
    /// The string that names the source, which is the connection string itself: an
    /// embedded database has no server to name, and the string is the one identifier
    /// that is stable while the connection is open. Empty while closed.
    /// </remarks>
    public override string DataSource => session is null ? string.Empty : connectionString;

    /// <summary>
    /// The version of H2 this provider ports, as the server reports it.
    /// </summary>
    private const string ServerVersionValue = "2.5.259";

    /// <summary>
    /// Gets the version of the server this connection is attached to.
    /// </summary>
    public override string ServerVersion => session is null
        ? throw new InvalidOperationException("The connection is closed")
        : ServerVersionValue;

    /// <summary>
    /// Gets the state of the connection.
    /// </summary>
    public override ConnectionState State => session is null ? ConnectionState.Closed : ConnectionState.Open;

    /// <summary>
    /// Gets the database this connection is attached to, or null while closed.
    /// </summary>
    /// <remarks>
    /// Not the <c>Database</c> property above, which answers the *name* of the current database
    /// as ADO.NET defines it. The type here is <c>H2Sharp.Engine.Database</c>, and a property of
    /// that name cannot say so: inside this class the word binds to the type, not to the member.
    /// </remarks>
    public Engine.Database? CurrentDatabase => session?.GetDatabase();

    /// <summary>
    /// Gets the session this connection runs statements on, or null while closed.
    /// </summary>
    internal SessionLocal? Session => session;

    /// <summary>
    /// Opens the connection: parses the connection string and gets a session on the database it
    /// names.
    /// </summary>
    /// <remarks>
    /// Opening twice is refused rather than ignored, and that is the reference's rule: a second
    /// open on an open connection would hand out a second session, and the close of either one
    /// would then take the database away from the other.
    /// </remarks>
    public override void Open()
    {
        if (session is not null)
        {
            throw new InvalidOperationException("The connection is already open");
        }

        if (connectionString.Length == 0)
        {
            throw new InvalidOperationException("The connection string is empty");
        }

        ConnectionInfo connectionInfo = new H2ConnectionStringBuilder(connectionString).ToConnectionInfo();
        session = H2Sharp.Engine.Engine.OpenSession(
            connectionInfo, systemClockProvider, randomGeneratorProvider, uuidProvider,
            timeZoneProviderProvider, extensionConfiguration.Clone());
        OnStateChange(new StateChangeEventArgs(ConnectionState.Closed, ConnectionState.Open));
    }

    /// <summary>
    /// Opens the connection asynchronously.
    /// </summary>
    /// <remarks>
    /// Synchronous for now: only in-memory databases open, and there is no I/O to await.
    /// File-backed opens will do I/O, and then this becomes truly asynchronous rather than
    /// Open on a completed task.
    /// </remarks>
    /// <param name="cancellationToken">the token to watch</param>
    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        Open();
        return Task.CompletedTask;
    }

    private void EnsureCanRegister()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(H2Connection));
        }

        if (State != ConnectionState.Closed)
        {
            throw new InvalidOperationException("Extensions can only be registered while the connection is closed");
        }
    }

    void IH2Extensions.RegisterUdf<TResult>(string name, Func<TResult> implementation, bool deterministic)
    {
        EnsureCanRegister();
        extensionConfiguration.Add(UdfDefinition.Create(SqlName(name), implementation, deterministic));
    }

    void IH2Extensions.RegisterUdf<T1, TResult>(string name, Func<T1, TResult> implementation, bool deterministic)
    {
        EnsureCanRegister();
        extensionConfiguration.Add(UdfDefinition.Create(SqlName(name), implementation, deterministic));
    }

    void IH2Extensions.RegisterUdf<T1, T2, TResult>(string name, Func<T1, T2, TResult> implementation, bool deterministic)
    {
        EnsureCanRegister();
        extensionConfiguration.Add(UdfDefinition.Create(SqlName(name), implementation, deterministic));
    }

    void IH2Extensions.RegisterUdf<T1, T2, T3, TResult>(string name, Func<T1, T2, T3, TResult> implementation, bool deterministic)
    {
        EnsureCanRegister();
        extensionConfiguration.Add(UdfDefinition.Create(SqlName(name), implementation, deterministic));
    }

    void IH2Extensions.RegisterUdf<T1, T2, T3, T4, TResult>(string name, Func<T1, T2, T3, T4, TResult> implementation, bool deterministic)
    {
        EnsureCanRegister();
        extensionConfiguration.Add(UdfDefinition.Create(SqlName(name), implementation, deterministic));
    }

    void IH2RawExtensions.RegisterRawUdf(
        string name, int arity, H2Sharp.Values.TypeInfo resultType,
        Func<H2Sharp.Values.Value[], H2Sharp.Values.Value> implementation, bool deterministic)
    {
        EnsureCanRegister();
        ArgumentNullException.ThrowIfNull(implementation);
        extensionConfiguration.AddRaw(SqlName(name), arity, resultType, deterministic, implementation);
    }

    void IH2Extensions.RegisterHashAlgorithm(string name, Func<System.Security.Cryptography.HashAlgorithm> factory)
    {
        EnsureCanRegister();
        ArgumentNullException.ThrowIfNull(factory);
        extensionConfiguration.AddHash(SqlName(name), factory);
    }

    void IH2Extensions.RegisterRandomGenerator(Func<long?, Random> factory)
    {
        EnsureCanRegister();
        extensionConfiguration.SetRandomGenerator(factory ?? throw new ArgumentNullException(nameof(factory)));
    }

    void IH2Extensions.RegisterUuidGenerator(Func<int, Guid> factory)
    {
        EnsureCanRegister();
        extensionConfiguration.SetUuidGenerator(factory ?? throw new ArgumentNullException(nameof(factory)));
    }

    private static string SqlName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return StringUtils.ToUpperEnglish(name);
    }

    /// <summary>
    /// Closes the connection, and gives the session back.
    /// </summary>
    /// <remarks>
    /// Closing twice, or closing one that was never open, does nothing — which is what makes
    /// <c>using</c> safe and what keeps a database alive until its *last* session is gone.
    /// </remarks>
    public override void Close()
    {
        SessionLocal? current = session;
        if (current is null)
        {
            return;
        }

        currentTransaction?.Dispose();
        currentTransaction = null;
        session = null;
        current.Close();

        OnStateChange(new StateChangeEventArgs(ConnectionState.Open, ConnectionState.Closed));
    }

    /// <summary>
    /// Releases the resources of this connection.
    /// </summary>
    /// <remarks>
    /// The base does not close: disposing without closing would leave the session open
    /// behind a connection nobody holds, so this closes first and lets the base raise
    /// Disposed after.
    /// </remarks>
    /// <param name="disposing">true for managed cleanup</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
            disposed = true;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Gets the factory this connection was made by.
    /// </summary>
    protected override DbProviderFactory DbProviderFactory => H2ProviderFactory.Instance;

    /// <summary>
    /// Changes the current database for this connection.
    /// </summary>
    /// <param name="databaseName">the database name</param>
    public override void ChangeDatabase(string databaseName) => throw new TODOException("H2Connection.ChangeDatabase");

    /// <summary>
    /// Begins a database transaction.
    /// </summary>
    /// <param name="isolationLevel">the isolation level</param>
    /// <remarks>
    /// The isolation level the caller asked for rides into the engine here: the standard
    /// ADO.NET surface carries it on BeginTransaction, and the session keeps it for every
    /// transaction of this connection, the way the reference's
    /// JdbcConnection.setTransactionIsolation keeps it on the session. Unspecified means
    /// "the provider decides", which is the level the session already has.
    /// </remarks>
    protected override DbTransaction BeginDbTransaction(AdoIsolationLevel isolationLevel)
    {
        SessionLocal? current = session;
        if (current is null)
        {
            throw new InvalidOperationException("The connection is not open");
        }

        if (currentTransaction is not null)
        {
            throw new InvalidOperationException("The connection already has a transaction");
        }

        if (isolationLevel == AdoIsolationLevel.Unspecified)
        {
            isolationLevel = ToAdoIsolationLevel(current.GetIsolationLevel());
        }
        else
        {
            current.SetIsolationLevel(FromAdoIsolationLevel(isolationLevel));
        }

        // The session transaction, not a store transaction of its own: the statements share it,
        // and auto-commit is switched off so the per-statement gate in CommandContainer does not
        // commit inside an explicit transaction.
        current.Begin();
        H2Transaction transaction = new(this, isolationLevel);
        currentTransaction = transaction;
        return transaction;
    }

    /// <summary>Maps the engine's isolation level to ADO.NET's.</summary>
    /// <param name="isolationLevel">the engine's isolation level</param>
    /// <returns>the ADO.NET isolation level</returns>
    private static AdoIsolationLevel ToAdoIsolationLevel(EngineIsolationLevel isolationLevel)
    {
        return isolationLevel switch
        {
            EngineIsolationLevel.ReadUncommitted => AdoIsolationLevel.ReadUncommitted,
            EngineIsolationLevel.ReadCommitted => AdoIsolationLevel.ReadCommitted,
            EngineIsolationLevel.RepeatableRead => AdoIsolationLevel.RepeatableRead,
            EngineIsolationLevel.Snapshot => AdoIsolationLevel.Snapshot,
            EngineIsolationLevel.Serializable => AdoIsolationLevel.Serializable,
            _ => AdoIsolationLevel.Unspecified,
        };
    }

    /// <summary>
    /// Maps the ADO.NET isolation level to the engine's.
    /// </summary>
    /// <param name="isolationLevel">the ADO.NET isolation level</param>
    /// <returns>the engine's isolation level</returns>
    /// <remarks>
    /// The five levels the engine has map one to one. Chaos is dirty reads plus
    /// intervening changes; the engine has no such level, and this refuses it the way a
    /// provider refuses a level it cannot honor. Unspecified never reaches here.
    /// </remarks>
    private static EngineIsolationLevel FromAdoIsolationLevel(AdoIsolationLevel isolationLevel)
    {
        return isolationLevel switch
        {
            AdoIsolationLevel.ReadUncommitted => EngineIsolationLevel.ReadUncommitted,
            AdoIsolationLevel.ReadCommitted => EngineIsolationLevel.ReadCommitted,
            AdoIsolationLevel.RepeatableRead => EngineIsolationLevel.RepeatableRead,
            AdoIsolationLevel.Snapshot => EngineIsolationLevel.Snapshot,
            AdoIsolationLevel.Serializable => EngineIsolationLevel.Serializable,
            _ => throw new ArgumentException(
                "The isolation level " + isolationLevel + " is not supported",
                nameof(isolationLevel)),
        };
    }

    /// <summary>
    /// Forgets a finished transaction, so the next one may begin.
    /// </summary>
    /// <param name="transaction">the transaction that finished</param>
    internal void ClearTransaction(H2Transaction transaction)
    {
        if (ReferenceEquals(currentTransaction, transaction))
        {
            currentTransaction = null;
            if (session is not null)
            {
                session.Transaction = null;
            }
        }
    }

    /// <summary>
    /// Gets the open transaction, if any.
    /// </summary>
    internal H2Transaction? CurrentTransaction => currentTransaction;

    /// <summary>
    /// Creates a command for this connection.
    /// </summary>
    /// <remarks>
    /// Even while closed: the command fails when it runs, not when it is made, which is
    /// what lets a caller set the text and the parameters before opening.
    /// </remarks>
    protected override DbCommand CreateDbCommand()
    {
        return new H2Command { Connection = this };
    }
}
