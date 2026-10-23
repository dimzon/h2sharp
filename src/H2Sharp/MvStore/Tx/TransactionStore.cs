using System;
using H2Sharp.Engine;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using H2Sharp.MvStore.Type;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// A store with concurrent MVCC transactions over one <see cref="MVStore"/>.
/// </summary>
/// <remarks>
/// Every versioned data map is keyed and valued by plain objects: Java's erasure lets one
/// undo log and one commit pass serve every map regardless of its declared key and value
/// types, and C# generics do not. An explicitly passed key or value type is still registered
/// (and still decides nothing about the map): the map always holds objects.
/// </remarks>
public class TransactionStore : IDisposable
{
    private const int Open = 0;
    private const int Initializing = 1;
    private const int Ready = 2;
    private const int Closing = 3;
    private const int Closed = 4;

    /// <summary>
    /// The store.
    /// </summary>
    internal readonly MVStore Store;

    /// <summary>
    /// The default blocked-transaction timeout.
    /// </summary>
    internal readonly int TimeoutMillis;

    /// <summary>
    /// The persisted map of prepared transactions: id to [status, name].
    /// </summary>
    private readonly MVMap<int, object?[]> preparedTransactions;

    /// <summary>
    /// Undo logs by transaction id. The first entry's log id is 0 unless the transaction is
    /// partially committed (then rollback is impossible). Log entries precede the data change.
    /// Key: operation id, value: [mapId, key, oldValue].
    /// </summary>
    private readonly MVMap<long, Record<object, object>>?[] undoLogs;

    private readonly MVMap<long, Record<object, object>>.Builder<long, Record<object, object>> undoLogBuilder;

    private readonly object sync = new();

    /// <summary>
    /// Vacancy bits for transaction slots: the first clear bit is the next id.
    /// Copy-on-write, so readers never lock.
    /// </summary>
    private VersionedBitSet? openTransactions = new VersionedBitSet();

    /// <summary>
    /// The truth about commitment: a set bit means logically committed, even while uncommitted
    /// entries and undo records are still around.
    /// </summary>
    private VersionedBitSet? committingTransactions = new VersionedBitSet();

    internal VersionedBitSet CommittingTransactions => Volatile.Read(ref committingTransactions)!;

    private int state = Open;

    /// <summary>
    /// Soft limit on concurrently open transactions.
    /// </summary>
    private int maxTransactionId = MaxOpenTransactions;

    /// <summary>
    /// Open transactions by id.
    /// </summary>
    private readonly Transaction?[] transactions;

    private const string TypeRegistryName = "_";

    /// <summary>
    /// The prefix for undo log map names.
    /// </summary>
    public const string UndoLogNamePrefix = "undoLog";

    // must come before open in lexicographical order
    private const char UndoLogCommitted = '-';

    private const char UndoLogOpen = '.';

    /// <summary>
    /// Hard limit on concurrently open transactions.
    /// </summary>
    private static readonly int MaxOpenTransactions = Utils.GetProperty("h2.maxOpenTransactions", 255);

    private static string GetUndoLogName(int transactionId)
        => transactionId > 0 ? UndoLogNamePrefix + UndoLogOpen + transactionId
                : UndoLogNamePrefix + UndoLogOpen;

    /// <summary>
    /// A transaction store over the given store.
    /// </summary>
    public TransactionStore(MVStore store)
        : this(store, new ObjectDataType())
    {
    }

    /// <summary>
    /// A transaction store with a default data type.
    /// </summary>
    public TransactionStore(MVStore store, DataType<object?> dataType)
        : this(store, new MetaType<object>(null!, null), dataType, 0)
    {
    }

    /// <summary>
    /// A transaction store with full configuration.
    /// </summary>
    /// <remarks>
    /// The data type only satisfies the shape: versioned maps always hold objects, so it is
    /// accepted and not consulted.
    /// </remarks>
    public TransactionStore(MVStore store, MetaType<object> metaDataType, DataType<object?> dataType, int timeoutMillis)
    {
        Store = store;
        _ = dataType;
        TimeoutMillis = timeoutMillis;
        TypeRegistry = OpenTypeRegistry(store, metaDataType);
        preparedTransactions = store.OpenMap("openTransactions",
            new MVMap<int, object?[]>.Builder<int, object?[]>());
        undoLogBuilder = CreateUndoLogBuilder();
        undoLogs = new MVMap<long, Record<object, object>>?[MaxOpenTransactions + 1];
        transactions = new Transaction?[MaxOpenTransactions + 1];
    }

    private MVMap<string, DataType<object>> TypeRegistry { get; }

    internal static string GetDataTypeRegistrationKey(object dataType)
        => RuntimeHelpers.GetHashCode(dataType).ToString("x", CultureInfo.InvariantCulture);

    private MVMap<long, Record<object, object>>.Builder<long, Record<object, object>> CreateUndoLogBuilder() => new MVMap<long, Record<object, object>>.Builder<long, Record<object, object>>()
                .SingleWriter()
                .KeyType(LongDataType.Instance)
                .ValueType(new Record<object, object>.Type(this));

    private static MVMap<string, DataType<object>> OpenTypeRegistry(MVStore store, MetaType<object> metaDataType)
    {
        var typeRegistryBuilder = new MVMap<string, DataType<object>>.Builder<string, DataType<object>>()
                .KeyType(StringDataType.Instance);
        _ = typeRegistryBuilder.ValueType(metaDataType);
        return store.OpenMap(TypeRegistryName, typeRegistryBuilder);
    }

    /// <summary>
    /// Initializes the store, recovering leftover transactions.
    /// </summary>
    public void Init() => Init(None);

    /// <summary>
    /// Initializes the store, notifying the listener about rollbacks.
    /// </summary>
    public void Init(RollbackListener listener)
    {
        if (Interlocked.CompareExchange(ref state, Initializing, Open) == Open)
        {
            var leftoverTransactions = new List<Transaction>();
            foreach (string mapName in Store.GetMapNames())
            {
                if (mapName.StartsWith(UndoLogNamePrefix, StringComparison.Ordinal))
                {
                    // Unexpectedly short name may be encountered upon upgrade from older version
                    // where undo log was persisted as a single map, remove it.
                    if (mapName.Length > UndoLogNamePrefix.Length)
                    {
                        // make a decision about tx status based on a log name
                        // to handle upgrade from a previous versions
                        bool committed = mapName[UndoLogNamePrefix.Length] == UndoLogCommitted;
                        if (Store.HasData(mapName))
                        {
                            int transactionId = StringUtils.ParseUInt31(mapName,
                                UndoLogNamePrefix.Length + 1, mapName.Length);
                            if (transactionId <= maxTransactionId)
                            {
                                object?[]? data = preparedTransactions.Get(transactionId);
                                int status;
                                string? name;
                                if (data is null)
                                {
                                    status = Transaction.StatusOpen;
                                    name = null;
                                }
                                else
                                {
                                    status = (int)data[0]!;
                                    name = (string?)data[1];
                                }

                                try
                                {
                                    MVMap<long, Record<object, object>> undoLog =
                                        Store.OpenMap(mapName, undoLogBuilder);
                                    undoLogs[transactionId] = undoLog;
                                    long? lastUndoKey = undoLog.LastKey();
                                    Debug.Assert(lastUndoKey.HasValue);
                                    Debug.Assert(GetTransactionId(lastUndoKey!.Value) == transactionId);
                                    long logId = GetLogId(lastUndoKey.Value) + 1;
                                    int commitOrder = 1;
                                    if (committed)
                                    {
                                        // give it a proper name and use a marker record instead
                                        Store.RenameMap(undoLog, GetUndoLogName(transactionId));
                                        MarkUndoLogAsCommitted(transactionId, -commitOrder);
                                    }
                                    else
                                    {
                                        committed = logId > LogIdMask;
                                    }

                                    if (committed)
                                    {
                                        status = Transaction.StatusCommitted;
                                        Record<object, object>? record = undoLog.Get(lastUndoKey);
                                        Debug.Assert(record is not null);
                                        Debug.Assert(record!.MapId < 0);
                                        commitOrder = -record.MapId;
                                        lastUndoKey = undoLog.LowerKey(lastUndoKey);
                                        Debug.Assert(!lastUndoKey.HasValue
                                            || GetTransactionId(lastUndoKey.Value) == transactionId);
                                        logId = !lastUndoKey.HasValue ? 0 : GetLogId(lastUndoKey.Value) + 1;
                                    }

                                    var transaction = new Transaction(null, transactionId, commitOrder,
                                                                                status, name, logId, 1, 0, null, null);
                                    leftoverTransactions.Add(transaction);
                                    continue;
                                }
                                catch (Exception)
                                {
                                    // Exception like NPE or Assertion are possible here after some chunk loss
                                    // after unclean shutdown, i.e. when undo log may have references to already
                                    // removed map
                                }
                            }
                        }
                    }

                    if (!Store.IsClosed())
                    {
                        Store.RemoveMap(mapName);
                    }
                }
            }

            leftoverTransactions.Sort((a, b) => a.GetSequenceNum().CompareTo(b.GetSequenceNum()));
            foreach (Transaction tx in leftoverTransactions)
            {
                _ = RegisterTransaction(tx.GetId(), tx.GetStatus(), tx.GetName(), tx.GetLogId(), TimeoutMillis, 0,
                        IsolationLevel.ReadCommitted, listener);
            }

            _ = Interlocked.Exchange(ref state, Ready);
        }
    }

    /// <summary>
    /// Whether the transaction is fully closed: status closed, bits cleared, slot empty.
    /// </summary>
    public bool IsTransactionClosed(int transactionId)
        => !Volatile.Read(ref openTransactions)!.Get(transactionId);

    private void MarkUndoLogAsCommitted(int transactionId, long commitOrder)
        => AddUndoLogRecord(transactionId, LogIdMask, new Record<object, object>(-(int)commitOrder));

    /// <summary>
    /// Commits the committed leftovers, rolls back the open ones.
    /// </summary>
    public void EndLeftoverTransactions()
    {
        List<Transaction> list = GetOpenTransactions();
        foreach (Transaction t in list)
        {
            Debug.Assert(!Volatile.Read(ref committingTransactions)!.Get(t.TransactionId));
            int status = t.GetStatus();
            if (status == Transaction.StatusCommitted)
            {
                t.Commit();
            }
            else if (status != Transaction.StatusPrepared)
            {
                t.Rollback();
            }
        }
    }

    internal int GetMaxTransactionId() => maxTransactionId;

    /// <summary>
    /// Sets the maximum transaction id, after which ids are re-used.
    /// </summary>
    public void SetMaxTransactionId(int max)
    {
        DataUtils.CheckArgument(max <= MaxOpenTransactions,
                "Concurrent transactions limit is too high: {0}", max);
        maxTransactionId = max;
    }

    /// <summary>
    /// Whether a map exists.
    /// </summary>
    public bool HasMap(string name) => Store.HasMap(name);

    private const int LogIdBits = Transaction.LogIdBits;
    private const long LogIdMask = (1L << LogIdBits) - 1;

    /// <summary>
    /// Unboxes a value, answering default for null. A direct <c>(V?)</c> cast of null throws
    /// for value types (unbox of null), where Java answers null: every boundary cast from
    /// object storage to a map value goes through here.
    /// </summary>
    internal static V? Unbox<V>(object? value) => value is null ? default : (V?)value;

    /// <summary>
    /// Combines a transaction id and a log id into an operation id.
    /// </summary>
    internal static long GetOperationId(int transactionId, long logId)
    {
        DataUtils.CheckArgument(transactionId >= 0 && transactionId < (1 << (64 - LogIdBits)),
                "Transaction id out of range: {0}", transactionId);
        DataUtils.CheckArgument(logId >= 0 && logId <= LogIdMask,
                "Transaction log id out of range: {0}", logId);
        return ((long)transactionId << LogIdBits) | logId;
    }

    /// <summary>
    /// The transaction id of an operation id.
    /// </summary>
    internal static int GetTransactionId(long operationId) => (int)(operationId >> LogIdBits);

    /// <summary>
    /// The log id of an operation id.
    /// </summary>
    internal static long GetLogId(long operationId) => operationId & LogIdMask;

    /// <summary>
    /// The entry id for a value: its own when it comes from the same transaction, else the
    /// log id of the undo key (the first update of the entry in this transaction).
    /// </summary>
    internal static long GetEntryId(VersionedValue<object>? versionedValue, long undoKey)
    {
        long entryId = VersionedValue<object>.NoEntryId;
        if (versionedValue is not null)
        {
            long operationId = versionedValue.GetOperationId();
            if (operationId != VersionedValue<object>.NoOperationId
                && GetTransactionId(operationId) == GetTransactionId(undoKey))
            {
                entryId = versionedValue.GetEntryId();
            }
        }

        if (entryId == VersionedValue<object>.NoEntryId)
        {
            long logId = GetLogId(undoKey);
            Debug.Assert(logId != VersionedValue<object>.NoEntryId);
            entryId = logId;
        }

        return entryId;
    }

    /// <summary>
    /// The unclosed transactions with pending writes, sorted by id.
    /// </summary>
    public List<Transaction> GetOpenTransactions()
    {
        if (Volatile.Read(ref state) == Open)
        {
            Init();
        }

        var list = new List<Transaction>();
        int transactionId = 0;

        VersionedBitSet lastCommittingTx;
        VersionedBitSet latestCommittingTx = Volatile.Read(ref committingTransactions)!;
        VersionedBitSet latestOpenTx;
        do
        {
            lastCommittingTx = latestCommittingTx;
            latestOpenTx = Volatile.Read(ref openTransactions)!;
            latestCommittingTx = Volatile.Read(ref committingTransactions)!;
        } while (!ReferenceEquals(lastCommittingTx, latestCommittingTx));

        while ((transactionId = latestOpenTx.NextSetBit(transactionId + 1)) > 0)
        {
            if (!lastCommittingTx.Get(transactionId))
            {
                Transaction? transaction = GetTransaction(transactionId);
                if (transaction is not null)
                {
                    if (transaction.GetStatus() != Transaction.StatusClosed)
                    {
                        list.Add(transaction);
                    }
                }
            }
        }

        list.Sort((a, b) => a.GetSequenceNum().CompareTo(b.GetSequenceNum()));
        return list;
    }

    /// <summary>
    /// Closes the transaction store, committing the store.
    /// </summary>
    public void Close()
    {
        lock (sync)
        {
            int storeState;
            while ((storeState = Volatile.Read(ref state)) <= Ready)
            {
                if (Interlocked.CompareExchange(ref state, Closing, storeState) == storeState)
                {
                    _ = Store.Commit();
                    _ = Interlocked.Exchange(ref state, Closed);
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Closes without committing.
    /// </summary>
    public void CloseImmediately() => _ = Interlocked.Exchange(ref state, Closed);

    /// <summary>
    /// Begins a transaction.
    /// </summary>
    public Transaction Begin() => Begin(None, TimeoutMillis, 0, IsolationLevel.ReadCommitted);

    /// <summary>
    /// Begins a transaction at the given isolation level.
    /// </summary>
    /// <param name="isolationLevel">the isolation level</param>
    /// <returns>the transaction</returns>
    /// <remarks>
    /// The shape of the reference's no-argument begin, with the level as the parameter:
    /// the caller that knows the level (the session, from its own field) does not have to
    /// spell the listener, the timeout and the owner it takes by default.
    /// </remarks>
    public Transaction Begin(IsolationLevel isolationLevel)
        => Begin(None, TimeoutMillis, 0, isolationLevel);

    /// <summary>
    /// Begins a transaction with a listener, timeout, owner, and isolation level.
    /// </summary>
    public Transaction Begin(RollbackListener listener, int timeoutMillis, int ownerId,
            IsolationLevel isolationLevel)
    {
        Transaction transaction = RegisterTransaction(0, Transaction.StatusOpen, null, 0,
                timeoutMillis, ownerId, isolationLevel, listener);
        return transaction;
    }

    private Transaction RegisterTransaction(int txId, int status, string? name, long logId,
                                            int timeoutMillis, int ownerId,
                                            IsolationLevel isolationLevel, RollbackListener listener)
    {
        int transactionId;
        long sequenceNo;
        bool success;
        do
        {
            VersionedBitSet original = Volatile.Read(ref openTransactions)!;
            if (txId == 0)
            {
                transactionId = original.NextClearBit(1);
            }
            else
            {
                transactionId = txId;
                Debug.Assert(!original.Get(transactionId));
            }

            if (transactionId > maxTransactionId)
            {
                throw DataUtils.NewMvStoreException(
                        DataUtils.ErrorTooManyOpenTransactions,
                        "There are {0} open transactions",
                        transactionId - 1);
            }

            Debug.Assert(!original.Get(transactionId));
            var clone = new VersionedBitSet(original, transactionId);
            sequenceNo = clone.GetVersion();
            success = Interlocked.CompareExchange(ref openTransactions, clone, original) == original;
        } while (!success);

        Debug.Assert(!Volatile.Read(ref committingTransactions)!.Get(transactionId));

        var transaction = new Transaction(this, transactionId, sequenceNo, status, name, logId,
                timeoutMillis, ownerId, isolationLevel, listener);

        Debug.Assert(Volatile.Read(ref transactions[transactionId]) is null);
        Volatile.Write(ref transactions[transactionId], transaction);

        if (undoLogs[transactionId] is null)
        {
            string undoName = GetUndoLogName(transactionId);
            MVMap<long, Record<object, object>> undoLog = Store.OpenMap(undoName, undoLogBuilder);
            undoLogs[transactionId] = undoLog;
        }

        return transaction;
    }

    /// <summary>
    /// Persists a prepared or named transaction.
    /// </summary>
    internal void StoreTransaction(Transaction t)
    {
        if (t.GetStatus() == Transaction.StatusPrepared ||
                t.GetName() is not null)
        {
            object?[] v = { t.GetStatus(), t.GetName() };
            _ = preparedTransactions.Put(t.GetId(), v);
            t.WasStored = true;
        }
    }

    /// <summary>
    /// Adds an undo log entry, answering its key.
    /// </summary>
    internal long AddUndoLogRecord(int transactionId, long logId, Record<object, object> record)
    {
        MVMap<long, Record<object, object>>? undoLog = undoLogs[transactionId];
        Debug.Assert(undoLog is not null);
        long undoKey = GetOperationId(transactionId, logId);
        if (logId == 0 && !undoLog!.IsEmpty())
        {
            throw DataUtils.NewMvStoreException(
                    DataUtils.ErrorTooManyOpenTransactions,
                    "An old transaction with the same id " +
                    "is still open: {0}",
                    transactionId);
        }

        undoLog.Append(undoKey, record);
        return undoKey;
    }

    /// <summary>
    /// Removes the last undo log entry.
    /// </summary>
    internal void RemoveUndoLogRecord(int transactionId) => undoLogs[transactionId]!.TrimLast();

    /// <summary>
    /// Removes the map.
    /// </summary>
    internal void RemoveMap(MVMap<object, VersionedValue<object>> map) => Store.RemoveMap(map);

    /// <summary>
    /// Commits a transaction: others see its changes, the undo log is marked, entries are
    /// rewritten as committed, and the log is cleared.
    /// </summary>
    internal void Commit(Transaction t, bool recovery)
    {
        if (!Store.IsClosed())
        {
            int transactionId = t.TransactionId;

            // this is an atomic action that causes all changes
            // made by this transaction, to be considered as "committed"
            VersionedBitSet committingTx = FlipCommittingTransactionsBit(transactionId, true);

            t.NotifyAllWaitingTransactions();

            t.MarkStatementStart(null);

            // Now mark log as "committed".
            // It does not change the way this transaction is treated by others,
            // but preserves fact of commit in case of abrupt termination.
            MVMap<long, Record<object, object>>? undoLog = undoLogs[transactionId];
            Debug.Assert(undoLog is not null);
            if (!recovery)
            {
                MarkUndoLogAsCommitted(transactionId, committingTx.GetVersion());
            }

            Cursor<long, Record<object, object>> cursor = undoLog!.Cursor(null);

            var commitDecisionMaker = new CommitDecisionMaker<object>(t, Store.GetKeysPerPage());

            try
            {
                while (cursor.HasNext())
                {
                    long undoKey = cursor.Next();
                    Record<object, object>? record = cursor.GetValue();
                    int mapId = record!.MapId;
                    if (mapId < 0)
                    {
                        continue;
                    }

                    long entryId = GetEntryId(record.OldValue, undoKey);

                    if (!commitDecisionMaker.HaveSeenEntry((int)entryId))
                    {
                        MVMap<object, VersionedValue<object>>? map = OpenMap(mapId);
                        if (map is not null && !map.IsClosed())
                        {
                            // might be null if map was removed later
                            object? key = record.Key;
                            commitDecisionMaker.SetUndoKey(undoKey);
                            // second parameter (value) is not really
                            // used by CommitDecisionMaker
                            _ = map.Operate(key!, null, commitDecisionMaker);
                        }
                    }
                }

                t.MarkStatementEnd();
            }
            finally
            {
                try
                {
                    undoLog.Clear();
                }
                finally
                {
                    _ = FlipCommittingTransactionsBit(transactionId, false);
                }
            }
        }
    }

    private VersionedBitSet FlipCommittingTransactionsBit(int transactionId, bool flag)
    {
        VersionedBitSet result;
        bool success;
        do
        {
            VersionedBitSet original = Volatile.Read(ref committingTransactions)!;
            Debug.Assert(original.Get(transactionId) != flag,
                    flag ? "Double commit" : "Mysterious bit's disappearance");
            result = new VersionedBitSet(original, transactionId);
            success = Interlocked.CompareExchange(ref committingTransactions, result, original) == original;
        } while (!success);

        return result;
    }

    /// <summary>
    /// The underlying MVStore.
    /// </summary>
    /// <remarks>
    /// The reference reaches it through <c>store.getMvStore()</c> on its separate
    /// <c>mvstore.db.Store</c>; here the store is merged with this class.
    /// </remarks>
    internal MVStore GetMvStore() => Store;

    /// <summary>
    /// Opens a versioned data map by name. The map always holds objects; key and value types
    /// are registered, not built with.
    /// </summary>
    internal MVMap<object, VersionedValue<object>> OpenVersionedMap(string name)
        => Store.OpenMap(name, new TxMapBuilder(TypeRegistry));

    /// <summary>
    /// Opens a versioned data map by name with the given key and value types.
    /// </summary>
    /// <remarks>
    /// The reference's <c>Transaction.openMap(name, keyType, valueType)</c> reaches here. The
    /// types are object-typed only because this port's transactional maps are (see
    /// <see cref="ObjectMapDataType{T}"/>); they are still used for comparison, which is what a
    /// secondary index needs.
    /// </remarks>
    internal MVMap<object, VersionedValue<object>> OpenVersionedMap(string name,
        DataType<object>? keyType, DataType<object>? valueType)
        => Store.OpenMap(name, new TxMapBuilder(TypeRegistry, keyType, valueType));

    /// <summary>
    /// Opens a versioned map by id, or null when it was removed later on.
    /// </summary>
    internal MVMap<object, VersionedValue<object>>? OpenMap(int mapId)
    {
        MVMap<object, VersionedValue<object>>? map = Store.GetMap<object, VersionedValue<object>>(mapId);
        if (map is null)
        {
            string? mapName = Store.GetMapName(mapId);
            if (mapName is null)
            {
                // the map was removed later on
                return null;
            }

            map = Store.OpenMap(mapId, new TxMapBuilder(TypeRegistry));
        }

        return map;
    }

    /// <summary>
    /// The versioned map, opening it during initialization when it is not open yet.
    /// </summary>
    internal MVMap<object, VersionedValue<object>> GetVersionedMap(int mapId)
    {
        MVMap<object, VersionedValue<object>>? map = Store.GetMap<object, VersionedValue<object>>(mapId);
        if (map is null)
        {
            bool initialized = Volatile.Read(ref state) >= Ready;
            if (!initialized)
            {
                map = OpenMap(mapId);
            }

            Debug.Assert(map is not null, "map with id " + mapId + " is missing" +
                    (initialized ? "" : " during initialization"));
        }

        return map!;
    }

    /// <summary>
    /// Ends a transaction: closes the slot and commits the store when due.
    /// </summary>
    internal void EndTransaction(Transaction t, bool hasChanges)
    {
        Debug.Assert(!Volatile.Read(ref committingTransactions)!.Get(t.TransactionId));
        t.CloseIt();
        int txId = t.TransactionId;
        Volatile.Write(ref transactions[txId], null);

        bool success;
        do
        {
            VersionedBitSet original = Volatile.Read(ref openTransactions)!;
            Debug.Assert(original.Get(txId));
            var clone = new VersionedBitSet(original, txId);
            success = Interlocked.CompareExchange(ref openTransactions, clone, original) == original;
        } while (!success);

        if (hasChanges)
        {
            bool wasStored = t.WasStored;
            if (wasStored && !preparedTransactions.IsClosed())
            {
                _ = preparedTransactions.Remove(txId);
            }

            if (Store.IsVersioningRequired())
            {
                if (wasStored || Store.GetAutoCommitDelay() == 0)
                {
                    _ = Store.Commit();
                }
                else
                {
                    if (IsUndoEmpty())
                    {
                        // to avoid having to store the transaction log,
                        // if there is no open transaction,
                        // and if there have been many changes, store them now
                        int unsaved = Store.GetUnsavedMemory();
                        int max = Store.GetAutoCommitMemory();
                        // save at 3/4 capacity
                        if (unsaved * 4 > max * 3)
                        {
                            _ = Store.TryCommit();
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Snapshots of all undo logs, or null when a map can not flush from this thread.
    /// </summary>
    internal RootReference<long, Record<object, object>>[]? CollectUndoLogRootReferences()
    {
        VersionedBitSet openTrans = Volatile.Read(ref openTransactions)!;
        var undoLogRootReferences = new RootReference<long, Record<object, object>>?[openTrans.Length()];
        for (int i = openTrans.NextSetBit(0); i >= 0; i = openTrans.NextSetBit(i + 1))
        {
            MVMap<long, Record<object, object>>? undoLog = undoLogs[i];
            if (undoLog is not null)
            {
                RootReference<long, Record<object, object>> rootReference = undoLog.GetRoot();
                if (rootReference.NeedFlush())
                {
                    // abort attempt to collect snapshots for all undo logs
                    // because map's append buffer can't be flushed from a non-owning thread
                    return null;
                }

                undoLogRootReferences[i] = rootReference;
            }
        }

        return undoLogRootReferences!;
    }

    /// <summary>
    /// The total entries of undo log snapshots.
    /// </summary>
    internal static long CalculateUndoLogsTotalSize(
        RootReference<long, Record<object, object>>[]? undoLogRootReferences)
    {
        long undoLogsTotalSize = 0;
        if (undoLogRootReferences is not null)
        {
            foreach (RootReference<long, Record<object, object>>? rootReference in undoLogRootReferences)
            {
                if (rootReference is not null)
                {
                    undoLogsTotalSize += rootReference.GetTotalCount();
                }
            }
        }

        return undoLogsTotalSize;
    }

    private bool IsUndoEmpty()
    {
        VersionedBitSet openTrans = Volatile.Read(ref openTransactions)!;
        for (int i = openTrans.NextSetBit(0); i >= 0; i = openTrans.NextSetBit(i + 1))
        {
            MVMap<long, Record<object, object>>? undoLog = undoLogs[i];
            if (undoLog is not null && !undoLog.IsEmpty())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The open transaction object, or null when the slot is empty.
    /// </summary>
    internal Transaction? GetTransaction(int transactionId)
        => Volatile.Read(ref transactions[transactionId]);

    /// <summary>
    /// Rolls back to a savepoint.
    /// </summary>
    internal void RollbackTo(Transaction t, long maxLogId, long toLogId)
    {
        int transactionId = t.GetId();
        MVMap<long, Record<object, object>>? undoLog = undoLogs[transactionId];
        Debug.Assert(undoLog is not null);
        var decisionMaker = new RollbackDecisionMaker(this, transactionId, toLogId, t.Listener!);
        for (long logId = maxLogId - 1; logId >= toLogId; logId--)
        {
            long undoKey = GetOperationId(transactionId, logId);
            _ = undoLogs[transactionId]!.Operate(undoKey, null, decisionMaker);
            decisionMaker.Reset();
        }

        t.NotifyAllWaitingTransactions();
    }

    /// <summary>
    /// The changes from the latest log id back to the savepoint.
    /// </summary>
    internal IEnumerable<Change> GetChanges(Transaction t, long maxLogId, long toLogId)
    {
        MVMap<long, Record<object, object>>? undoLog = undoLogs[t.GetId()];
        Debug.Assert(undoLog is not null);
        long logId = maxLogId - 1;
        int transactionId = t.GetId();
        while (logId >= toLogId)
        {
            long undoKey = GetOperationId(transactionId, logId);
            Record<object, object>? op = undoLog!.Get(undoKey);
            logId--;
            if (op is null)
            {
                // partially rolled back: load previous
                long? prev = undoLog.FloorKey(undoKey);
                if (prev is null || GetTransactionId(prev.Value) != transactionId)
                {
                    break;
                }

                logId = GetLogId(prev.Value);
                continue;
            }

            int mapId = op.MapId;
            MVMap<object, VersionedValue<object>>? m = OpenMap(mapId);
            if (m is not null)
            {
                // could be null if map was removed later on
                VersionedValue<object>? oldValue = op.OldValue;
                yield return new Change(m.GetName(), op.Key,
                        oldValue is null ? null : oldValue.GetCurrentValue());
            }
        }
    }

    /// <summary>
    /// One change of one map: the value before the change.
    /// </summary>
    public sealed class Change
    {
        /// <summary>
        /// The map where the change occurred.
        /// </summary>
        public readonly string? MapName;

        /// <summary>
        /// The key.
        /// </summary>
        public readonly object? Key;

        /// <summary>
        /// The value.
        /// </summary>
        public readonly object? Value;

        /// <summary>
        /// A change.
        /// </summary>
        public Change(string? mapName, object? key, object? value)
        {
            MapName = mapName;
            Key = key;
            Value = value;
        }
    }

    /// <summary>
    /// Notified about every compensating change during rollback. Only external resources
    /// touched by triggers need this; transactional maps restore themselves.
    /// </summary>
    public interface RollbackListener
    {
        /// <summary>
        /// One map change (add/update/remove) being compensated.
        /// </summary>
        void OnRollback(MVMap<object, VersionedValue<object>> map, object? key,
                        VersionedValue<object>? existingValue, VersionedValue<object>? restoredValue);
    }

    private sealed class NoneListener : RollbackListener
    {
        public void OnRollback(MVMap<object, VersionedValue<object>> map, object? key,
            VersionedValue<object>? existingValue, VersionedValue<object>? restoredValue)
        {
        }
    }

    private static readonly RollbackListener None = new NoneListener();

    private sealed class TxMapBuilder
        : MVMap<object, VersionedValue<object>>.BasicBuilder<TMVMap, object, VersionedValue<object>>
    {
        private readonly MVMap<string, DataType<object>> typeRegistry;

        private readonly DataType<object>? valueDataType;

        internal TxMapBuilder(MVMap<string, DataType<object>> typeRegistry)
            : this(typeRegistry, null, null)
        {
        }

        internal TxMapBuilder(MVMap<string, DataType<object>> typeRegistry,
            DataType<object>? keyType, DataType<object>? valueType)
        {
            this.typeRegistry = typeRegistry;
            valueDataType = valueType;
            if (keyType is not null)
            {
                SetKeyType(keyType);
            }
        }

        private void RegisterDataType(DataType<object> dataType)
        {
            string key = GetDataTypeRegistrationKey(dataType);
            _ = typeRegistry.PutIfAbsent(key, dataType);
            // TODO: ensure type consistency (reference)
        }

        public override TMVMap Create(MVStore store, IDictionary<string, object?> config)
        {
            DataType<object>? keyType = GetKeyType();
            if (keyType is null)
            {
                if (config.Remove("key", out object? keyTypeKey) && keyTypeKey is string keyTypeName)
                {
                    keyType = typeRegistry.Get(keyTypeName);
                    if (keyType is null)
                    {
                        throw DataUtils.NewMvStoreException(DataUtils.ErrorUnknownDataType,
                                "Data type with hash {0} can not be found", keyTypeName);
                    }

                    SetKeyType(keyType);
                }
            }
            else
            {
                RegisterDataType(keyType);
            }

            // The stored value hash names a versioned wrapper the registry can not express;
            // versioned maps always hold objects, so it is dropped after removal.
            _ = config.Remove("val");

            if (GetKeyType() is null)
            {
                SetKeyType(new ObjectDataType<object>());
                RegisterDataType(GetKeyType()!);
            }

            // The value type is always versioned objects (see the class remark); the caller's
            // value type, when given, is wrapped in the versioned shell it is stored with.
            SetValueType(new VersionedValueType<object, object>(
                valueDataType ?? new ObjectDataType<object>()));

            config["store"] = store;
            config["key"] = GetKeyType()!;
            config["val"] = GetValueType()!;
            return Create(config);
        }

        protected override TMVMap Create(IDictionary<string, object?> config)
            => new TMVMap(config, GetKeyType()!, GetValueType()!);
    }

    private sealed class TMVMap : MVMap<object, VersionedValue<object>>
    {
        private readonly string? type;

        internal TMVMap(IDictionary<string, object?> config, DataType<object> keyType,
            DataType<VersionedValue<object>> valueType)
            : base(config, keyType, valueType)
        {
            _ = config.TryGetValue("type", out object? t);
            type = t as string;
        }

        private TMVMap(TMVMap source)
            : base(source)
        {
            type = source.GetMapType();
        }

        protected override MVMap<object, VersionedValue<object>> CloneIt() => new TMVMap(this);

        protected internal override string? GetMapType() => type;

        protected internal override string AsString(string? name)
        {
            var buff = new StringBuilder(base.AsString(name));
            DataUtils.AppendMap(buff, "key", GetDataTypeRegistrationKey(GetKeyType()));
            DataUtils.AppendMap(buff, "val", GetDataTypeRegistrationKey(GetValueType()));
            return buff.ToString();
        }
    }
}
