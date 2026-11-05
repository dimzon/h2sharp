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
 * The connection owns a session on an open database. Schema discovery reads the engine's
 * metadata (H2Sharp.Jdbc.Meta), which is the same catalogue walk the reference's JDBC
 * DatabaseMetaData performs in its default mode; the rows are the reference's, wrapped in
 * the DataTables ADO.NET expects.
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
using System.Transactions;
using H2Sharp.Data.Extensions;
using H2Sharp.Engine;
using H2Sharp.Extensions;
using H2Sharp.Jdbc.Meta;
using H2Sharp.Result;
using H2Sharp.Util;
using H2Sharp.Values;
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

    private readonly H2DataSource? dataSource;

    private readonly bool connectionStringLocked;

    private SessionLocal? session;


    private H2Transaction? currentTransaction;

    private Transaction? enlistedTransaction;

    private SessionLocal? enlistedSession;

    private Func<DateTimeOffset>? systemClockProvider;

    private Func<long?, Random>? randomGeneratorProvider;

    private Func<Guid>? uuidProvider;

    private Func<Util.TimeZoneProvider>? timeZoneProviderProvider;

    private readonly ExtensionConfiguration extensionConfiguration = new();

    private bool disposed;

    /// <summary>
    /// Whether this connection is a borrowed view over someone else's session, created by
    /// <see cref="BorrowSession"/> for triggers to run queries on the firing session.
    /// </summary>
    private bool borrowedSession;

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
        ArgumentNullException.ThrowIfNull(connectionString);
        this.connectionString = connectionString;
    }

    internal H2Connection(H2DataSource dataSource, string connectionString)
    {
        this.dataSource = dataSource;
        this.connectionString = connectionString;
        connectionStringLocked = true;
    }

    /// <summary>
    /// Creates a connection that borrows an existing session: statements on it run on the
    /// same session and transaction as the firing statement. The borrowed connection does
    /// not own the session — closing or disposing it leaves the session alive.
    /// </summary>
    /// <param name="session">the session to borrow</param>
    /// <returns>the borrowed connection</returns>
    internal static H2Connection BorrowSession(SessionLocal session)
    {
        ArgumentNullException.ThrowIfNull(session);
        H2Connection connection = new();
        connection.session = session;
        connection.borrowedSession = true;
        return connection;
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
            if (connectionStringLocked)
            {
                throw new InvalidOperationException(
                    "The connection string is owned by the data source and cannot be changed");
            }

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
        if (borrowedSession)
        {
            throw new InvalidOperationException("The borrowed connection is already open");
        }

        if (session is not null || enlistedSession is not null)
        {
            throw new InvalidOperationException("The connection is already open");
        }

        if (connectionString.Length == 0)
        {
            throw new InvalidOperationException("The connection string is empty");
        }

        ConnectionInfo connectionInfo = new H2ConnectionStringBuilder(connectionString).ToConnectionInfo();
        SessionLocal opened = dataSource is null
            ? H2Sharp.Engine.Engine.OpenSession(
                connectionInfo, systemClockProvider, randomGeneratorProvider, uuidProvider,
                timeZoneProviderProvider, extensionConfiguration.Clone())
            : dataSource.OpenSession(
                connectionInfo, systemClockProvider, randomGeneratorProvider, uuidProvider,
                timeZoneProviderProvider, extensionConfiguration.Clone());
        session = opened;
        try
        {
            if (Transaction.Current is { } ambient)
            {
                EnlistTransaction(ambient);
            }
        }
        catch
        {
            session = null;
            opened.Close();
            throw;
        }

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
        ObjectDisposedException.ThrowIf(disposed, this);

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

    void IH2Extensions.RegisterAggregate(string name, IH2AggregateProvider provider)
    {
        EnsureCanRegister();
        ArgumentNullException.ThrowIfNull(provider);
        extensionConfiguration.AddAggregate(SqlName(name), provider);
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
        ArgumentNullException.ThrowIfNull(factory);
        extensionConfiguration.SetRandomGenerator(factory);
    }

    void IH2Extensions.RegisterUuidGenerator(Func<int, Guid> factory)
    {
        EnsureCanRegister();
        ArgumentNullException.ThrowIfNull(factory);
        extensionConfiguration.SetUuidGenerator(factory);
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
        if (borrowedSession)
        {
            // The borrowed session belongs to the connection that owns it.
            session = null;
            return;
        }

        SessionLocal? current = session;
        if (current is null)
        {
            return;
        }

        if (enlistedTransaction is not null)
        {
            // The ambient transaction owns the session now. Closing the connection detaches it,
            // but the completion notification still has to commit or roll it back.
            enlistedSession = current;
            session = null;
            OnStateChange(new StateChangeEventArgs(ConnectionState.Open, ConnectionState.Closed));
            return;
        }

        // The reference cancels the session before locking it: a statement running on
        // another thread aborts at its next cancellation checkpoint and releases the
        // lock, instead of the close waiting behind a query that never ends.
        current.Cancel();
        current.Lock();
        try
        {
            currentTransaction?.Dispose();
            currentTransaction = null;
            session = null;
            current.Close();
        }
        finally
        {
            current.Unlock();
        }

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
    /// Creates a batch over this connection.
    /// </summary>
    protected override DbBatch CreateDbBatch()
        => new H2Batch(this);

    /// <summary>
    /// The collections GetSchema can answer, their restriction counts and the number of
    /// identifier parts in a collection name.
    /// </summary>
    private static readonly (string Name, int Restrictions, int IdentifierParts)[] SchemaCollections =
    [
        (DbMetaDataCollectionNames.MetaDataCollections, 0, 0),
        (DbMetaDataCollectionNames.Restrictions, 0, 0),
        (DbMetaDataCollectionNames.DataTypes, 0, 0),
        (DbMetaDataCollectionNames.ReservedWords, 0, 0),
        ("Tables", 4, 3),
        ("Columns", 4, 3),
        ("Indexes", 4, 3),
        ("ForeignKeys", 3, 3),
        ("Procedures", 3, 3),
        ("TableTypes", 0, 0),
        ("Catalogs", 0, 0),
        ("Schemas", 2, 2),
    ];

    /// <summary>
    /// The restriction names of each collection that has any.
    /// </summary>
    private static readonly (string Collection, string[] Restrictions)[] SchemaRestrictions =
    [
        ("Tables", ["Catalog", "Schema", "Table", "TableType"]),
        ("Columns", ["Catalog", "Schema", "Table", "Column"]),
        ("Indexes", ["Catalog", "Schema", "Table", "Index"]),
        ("ForeignKeys", ["Catalog", "Schema", "Table"]),
        ("Procedures", ["Catalog", "Schema", "Procedure"]),
        ("Schemas", ["Catalog", "Schema"]),
    ];

    /// <inheritdoc/>
    public override DataTable GetSchema()
        => GetSchema(DbMetaDataCollectionNames.MetaDataCollections);

    /// <inheritdoc/>
    public override DataTable GetSchema(string collectionName)
        => GetSchema(collectionName, null);

    /// <inheritdoc/>
    public override DataTable GetSchema(string collectionName, string?[]? restrictionValues)
    {
        if (collectionName is null)
        {
            throw new ArgumentException("The collection name is null", nameof(collectionName));
        }

        int allowed = RestrictionCount(collectionName);
        if (restrictionValues is not null && restrictionValues.Length > allowed)
        {
            throw new ArgumentException(
                $"More restrictions were provided than the collection {collectionName} supports",
                nameof(restrictionValues));
        }

        SessionLocal current = session
            ?? throw new InvalidOperationException("The connection is closed");
        DatabaseMeta meta = current.GetDatabaseMeta();

        // The arms compare without copying: ToUpperInvariant built a string on every call
        // only to compare it with constants, and the case-insensitive comparison answers the
        // same names without the copy.
        bool Is(string name) => collectionName.Equals(name, StringComparison.OrdinalIgnoreCase);

        return collectionName switch
        {
            _ when Is("METADATACOLLECTIONS") => MetaDataCollectionsTable(),
            _ when Is("RESTRICTIONS") => RestrictionsTable(),
            _ when Is("DATATYPES") => ToDataTable(DbMetaDataCollectionNames.DataTypes, meta.GetTypeInfo()),
            _ when Is("RESERVEDWORDS") => ReservedWordsTable(meta),
            _ when Is("TABLES") => ToDataTable(
                "Tables",
                meta.GetTables(
                    Restriction(restrictionValues, 0),
                    Restriction(restrictionValues, 1),
                    Restriction(restrictionValues, 2),
                    Restriction(restrictionValues, 3) is { } type ? [type] : null)),
            _ when Is("COLUMNS") => ToDataTable(
                "Columns",
                meta.GetColumns(
                    Restriction(restrictionValues, 0),
                    Restriction(restrictionValues, 1),
                    Restriction(restrictionValues, 2),
                    Restriction(restrictionValues, 3))),
            _ when Is("INDEXES") => IndexesTable(meta, restrictionValues),
            _ when Is("FOREIGNKEYS") => ForeignKeysTable(meta, restrictionValues),
            _ when Is("PROCEDURES") => ToDataTable(
                "Procedures",
                meta.GetProcedures(
                    Restriction(restrictionValues, 0),
                    Restriction(restrictionValues, 1),
                    Restriction(restrictionValues, 2))),
            _ when Is("TABLETYPES") => ToDataTable("TableTypes", meta.GetTableTypes()),
            _ when Is("CATALOGS") => ToDataTable("Catalogs", meta.GetCatalogs()),
            _ when Is("SCHEMAS") => ToDataTable(
                "Schemas",
                meta.GetSchemas(
                    Restriction(restrictionValues, 0),
                    Restriction(restrictionValues, 1))),
            _ => throw new ArgumentException(
                $"The requested collection ({collectionName}) is not defined.", nameof(collectionName)),
        };
    }

    /// <inheritdoc/>
    public override Task<DataTable> GetSchemaAsync(CancellationToken cancellationToken = default)
        => GetSchemaAsync(DbMetaDataCollectionNames.MetaDataCollections, null, cancellationToken);

    /// <inheritdoc/>
    public override Task<DataTable> GetSchemaAsync(
        string collectionName, CancellationToken cancellationToken = default)
        => GetSchemaAsync(collectionName, null, cancellationToken);

    /// <inheritdoc/>
    public override Task<DataTable> GetSchemaAsync(
        string collectionName, string?[]? restrictionValues,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<DataTable>(cancellationToken);
        }

        return Task.FromResult(GetSchema(collectionName, restrictionValues));
    }

    private static int RestrictionCount(string collectionName)
    {
        foreach ((string name, int restrictions, _) in SchemaCollections)
        {
            if (string.Equals(name, collectionName, StringComparison.OrdinalIgnoreCase))
            {
                return restrictions;
            }
        }

        throw new ArgumentException(
            $"The requested collection ({collectionName}) is not defined.", nameof(collectionName));
    }

    private static string? Restriction(string?[]? values, int index)
        => values is not null && index < values.Length ? values[index] : null;

    private static DataTable MetaDataCollectionsTable()
    {
        DataTable table = new(DbMetaDataCollectionNames.MetaDataCollections);
        table.Columns.Add("CollectionName", typeof(string));
        table.Columns.Add("NumberOfRestrictions", typeof(int));
        table.Columns.Add("NumberOfIdentifierParts", typeof(int));
        foreach ((string name, int restrictions, int identifierParts) in SchemaCollections)
        {
            table.Rows.Add(name, restrictions, identifierParts);
        }

        return table;
    }

    private static DataTable RestrictionsTable()
    {
        DataTable table = new(DbMetaDataCollectionNames.Restrictions);
        table.Columns.Add("CollectionName", typeof(string));
        table.Columns.Add("RestrictionName", typeof(string));
        table.Columns.Add("RestrictionDefault", typeof(string));
        table.Columns.Add("RestrictionNumber", typeof(int));
        foreach ((string collection, string[] restrictions) in SchemaRestrictions)
        {
            for (int i = 0; i < restrictions.Length; i++)
            {
                table.Rows.Add(collection, restrictions[i], DBNull.Value, i + 1);
            }
        }

        return table;
    }

    private static DataTable ReservedWordsTable(DatabaseMeta meta)
    {
        DataTable table = new(DbMetaDataCollectionNames.ReservedWords);
        table.Columns.Add("ReservedWord", typeof(string));
        foreach (string word in meta.GetSQLKeywords().Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            table.Rows.Add(word);
        }

        return table;
    }

    private static DataTable IndexesTable(DatabaseMeta meta, string?[]? restrictionValues)
    {
        string? table = Restriction(restrictionValues, 2);
        if (string.IsNullOrEmpty(table))
        {
            throw new ArgumentException(
                "The Indexes collection requires a table restriction", nameof(restrictionValues));
        }

        string? index = Restriction(restrictionValues, 3);
        return ToDataTable(
            "Indexes",
            meta.GetIndexInfo(
                Restriction(restrictionValues, 0), Restriction(restrictionValues, 1), table,
                unique: false, approximate: false),
            filterOrdinal: 5,
            filterValue: string.IsNullOrEmpty(index) ? null : index);
    }

    private static DataTable ForeignKeysTable(DatabaseMeta meta, string?[]? restrictionValues)
    {
        string? table = Restriction(restrictionValues, 2);
        if (string.IsNullOrEmpty(table))
        {
            throw new ArgumentException(
                "The ForeignKeys collection requires a table restriction", nameof(restrictionValues));
        }

        return ToDataTable(
            "ForeignKeys",
            meta.GetImportedKeys(
                Restriction(restrictionValues, 0), Restriction(restrictionValues, 1), table));
    }

    private static DataTable ToDataTable(
        string name, ResultInterface result, int filterOrdinal = -1, string? filterValue = null)
    {
        DataTable table = new(name);
        int count = result.GetVisibleColumnCount();
        for (int i = 0; i < count; i++)
        {
            TypeInfo type = result.GetColumnType(i);
            table.Columns.Add(result.GetColumnName(i), H2ValueMapper.GetFieldType(type));
        }

        while (result.Next())
        {
            Value?[] row = result.CurrentRow();
            if (filterValue is not null && filterOrdinal >= 0
                && !string.Equals(row[filterOrdinal]?.GetString(), filterValue, StringComparison.Ordinal))
            {
                continue;
            }

            object?[] values = new object?[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = H2ValueMapper.ToObject(row[i]);
            }

            table.Rows.Add(values);
        }

        return table;
    }

    /// <summary>
    /// Changes the current database for this connection.
    /// </summary>
    /// <remarks>
    /// Not supported by decision: the provider is embedded, and the database is named by
    /// the connection string at open, not switched later.
    /// </remarks>
    /// <param name="databaseName">the database name</param>
    public override void ChangeDatabase(string databaseName)
        => throw new NotSupportedException(
            "ChangeDatabase is not supported: the database is named by the connection string at open");

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

        if (enlistedTransaction is not null)
        {
            throw new InvalidOperationException("The connection is enlisted in an ambient transaction");
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

    /// <summary>
    /// Enlists this connection in an ambient transaction.
    /// </summary>
    /// <remarks>
    /// The engine transaction belongs to the session, so enlisting switches the session out
    /// of auto-commit and lets the transaction's completion notification commit or roll it
    /// back. The connection may be closed before the ambient transaction completes; the
    /// session is then kept by the enlistment until the notification arrives.
    /// </remarks>
    /// <param name="transaction">the transaction to enlist in, or null</param>
    public override void EnlistTransaction(Transaction? transaction)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (transaction is null)
        {
            if (enlistedTransaction is not null)
            {
                throw new InvalidOperationException("The connection is already enlisted in a transaction");
            }

            return;
        }

        if (enlistedTransaction is not null)
        {
            if (ReferenceEquals(enlistedTransaction, transaction))
            {
                return;
            }

            throw new InvalidOperationException("The connection is already enlisted in another transaction");
        }

        if (currentTransaction is not null)
        {
            throw new InvalidOperationException("The connection already has a transaction");
        }

        SessionLocal current = session
            ?? throw new InvalidOperationException("The connection is not open");
        current.Begin();
        enlistedSession = current;
        enlistedTransaction = transaction;
        try
        {
            _ = transaction.EnlistVolatile(new H2Enlistment(this), EnlistmentOptions.None);
        }
        catch
        {
            enlistedSession = null;
            enlistedTransaction = null;
            current.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Completes the ambient transaction this connection was enlisted in.
    /// </summary>
    /// <param name="commit">true to commit, false to roll back</param>
    internal void CompleteEnlisted(bool commit)
    {
        SessionLocal? current = enlistedSession;
        enlistedSession = null;
        enlistedTransaction = null;
        if (current is null)
        {
            return;
        }

        bool attached = ReferenceEquals(current, session);
        try
        {
            if (commit)
            {
                current.Commit(false);
            }
            else
            {
                current.Rollback();
            }
        }
        finally
        {
            if (!attached)
            {
                current.Close();
            }
        }
    }

    /// <summary>Maps the engine's isolation level to ADO.NET's.</summary>
    /// <param name="isolationLevel">the engine's isolation level</param>
    /// <returns>the ADO.NET isolation level</returns>
    internal static AdoIsolationLevel ToAdoIsolationLevel(EngineIsolationLevel isolationLevel)
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
