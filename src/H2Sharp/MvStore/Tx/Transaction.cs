using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using H2Sharp.MvStore.Type;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// One transaction: an id in a slot, a status with an undo log id packed into one atomic long,
/// and the maps it opened.
/// </summary>
public sealed class Transaction
{
    /// <summary>
    /// A closed transaction (committed or rolled back).
    /// </summary>
    public const int StatusClosed = 0;

    /// <summary>
    /// An open transaction.
    /// </summary>
    public const int StatusOpen = 1;

    /// <summary>
    /// A prepared transaction.
    /// </summary>
    public const int StatusPrepared = 2;

    /// <summary>
    /// A logically committed transaction: others already see its changes, but the id stays
    /// reserved until post-commit rewriting and closing are done.
    /// </summary>
    public const int StatusCommitted = 3;

    private const int StatusRollingBack = 4;

    private const int StatusRolledBack = 5;

    private static readonly string[] StatusNames =
    {
        "CLOSED", "OPEN", "PREPARED", "COMMITTED", "ROLLING_BACK", "ROLLED_BACK",
    };

    /// <summary>
    /// How many bits of an operation id are the log id (the rest are the transaction id).
    /// </summary>
    internal const int LogIdBits = 40;
    private const int LogIdBits1 = LogIdBits + 1;
    private const long LogIdLimit = 1L << LogIdBits;
    private const long LogIdMask = (1L << LogIdBits1) - 1;
    private const int StatusBits = 4;
    private const int StatusMask = (1 << StatusBits) - 1;

    /// <summary>
    /// The transaction store.
    /// </summary>
    private readonly TransactionStore? store;

    /// <summary>
    /// The transaction store. Never touched on a leftover recovered transaction.
    /// </summary>
    internal TransactionStore Store => store!;

    /// <summary>
    /// The rollback listener of this transaction.
    /// </summary>
    internal TransactionStore.RollbackListener? Listener { get; }

    /// <summary>
    /// The transaction id (really a slot id).
    /// </summary>
    internal readonly int TransactionId;

    /// <summary>
    /// The identity, never re-used.
    /// </summary>
    private readonly long sequenceNum;

    /*
     * Transaction state is an atomic composite field:
     * bit  45      : flag whether transaction had rollback(s)
     * bits 44-41   : status
     * bits 40      : overflow control bit, 1 indicates overflow
     * bits 39-0    : log id of the last entry in the undo log map
     */
    private readonly AtomicLong statusAndLogId;

    /// <summary>
    /// The earliest store version this transaction uses: it and newer ones can not be
    /// discarded until the transaction ends.
    /// </summary>
    private MVStore.TxCounter? txCounter;

    private string? name;

    /// <summary>
    /// Whether this transaction was stored in the prepared-transactions map.
    /// </summary>
    internal bool WasStored;

    /// <summary>
    /// How long to wait for a blocking transaction.
    /// </summary>
    internal int TimeoutMillis;

    /// <summary>
    /// The owner, usually a database session.
    /// </summary>
    private readonly int ownerId;

    /// <summary>
    /// The transaction this one waits on, if any.
    /// </summary>
    private volatile Transaction? blockingTransaction;

    private string? blockingMapName;

    private object? blockingKey;

    /// <summary>
    /// Whether others wait for this transaction to close.
    /// </summary>
    private volatile bool notificationRequested;

    private readonly object sync = new();

    /// <summary>
    /// Root references for undo log snapshots.
    /// </summary>
    private RootReference<long, Record<object, object>>[]? undoLogRootReferences;

    private readonly Dictionary<int, object> transactionMaps = new();

    /// <summary>
    /// The isolation level.
    /// </summary>
    internal readonly IsolationLevel? isolationLevel;

    internal Transaction(TransactionStore? store, int transactionId, long sequenceNum, int status,
                string? name, long logId, int timeoutMillis, int ownerId,
                IsolationLevel? isolationLevel, TransactionStore.RollbackListener? listener)
    {
        this.store = store;
        TransactionId = transactionId;
        this.sequenceNum = sequenceNum;
        statusAndLogId = new AtomicLong();
        statusAndLogId.Set(ComposeState(status, logId, false));
        this.name = name;
        SetTimeoutMillis(timeoutMillis);
        this.ownerId = ownerId;
        this.isolationLevel = isolationLevel;
        Listener = listener;
    }

    /// <summary>
    /// The transaction id.
    /// </summary>
    public int GetId() => TransactionId;

    /// <summary>
    /// The identity, never re-used.
    /// </summary>
    public long GetSequenceNum() => sequenceNum;

    /// <summary>
    /// The status.
    /// </summary>
    public int GetStatus() => GetStatus(statusAndLogId.Get());

    internal RootReference<long, Record<object, object>>[]? GetUndoLogRootReferences() => undoLogRootReferences;

    private long SetStatus(int status)
    {
        while (true)
        {
            long currentState = statusAndLogId.Get();
            long logId = GetLogId(currentState);
            int currentStatus = GetStatus(currentState);
            bool valid;
            switch (status)
            {
                case StatusRollingBack:
                case StatusPrepared:
                    valid = currentStatus == StatusOpen;
                    break;
                case StatusCommitted:
                    valid = currentStatus == StatusOpen ||
                            currentStatus == StatusPrepared ||
                            // this case is only possible if called
                            // from endLeftoverTransactions()
                            currentStatus == StatusCommitted;
                    break;
                case StatusRolledBack:
                    valid = currentStatus == StatusOpen ||
                            currentStatus == StatusPrepared ||
                            currentStatus == StatusRollingBack;
                    break;
                case StatusClosed:
                    valid = currentStatus == StatusCommitted ||
                            currentStatus == StatusRolledBack ||
                            currentStatus == StatusClosed;
                    break;
                case StatusOpen:
                default:
                    valid = false;
                    break;
            }

            if (!valid)
            {
                throw DataUtils.NewMvStoreException(
                        DataUtils.ErrorTransactionIllegalState,
                        "Transaction was illegally transitioned from {0} to {1}",
                        GetStatusName(currentStatus), GetStatusName(status));
            }

            long newState = ComposeState(status, logId, HasRollback(currentState));
            if (statusAndLogId.CompareAndSet(currentState, newState))
            {
                return currentState;
            }
        }
    }

    /// <summary>
    /// Whether any database changes were made as part of this transaction.
    /// </summary>
    public bool HasChanges() => HasChanges(statusAndLogId.Get());

    /// <summary>
    /// Names the transaction, persisting the name when prepared or named.
    /// </summary>
    public void SetName(string? name)
    {
        CheckNotClosed();
        this.name = name;
        Store!.StoreTransaction(this);
    }

    /// <summary>
    /// The name.
    /// </summary>
    public string? GetName() => name;

    /// <summary>
    /// The owner id of the blocking transaction, or 0 when not blocked.
    /// </summary>
    public int GetBlockerId()
    {
        Transaction? blocker = blockingTransaction;
        return blocker is null ? 0 : blocker.ownerId;
    }

    /// <summary>
    /// Creates a savepoint: the current log id.
    /// </summary>
    public long SetSavepoint() => GetLogId();

    /// <summary>
    /// Whether the transaction opened any maps.
    /// </summary>
    public bool HasStatementDependencies() => transactionMaps.Count != 0;

    /// <summary>
    /// The isolation level.
    /// </summary>
    public IsolationLevel? GetIsolationLevel() => isolationLevel;

    internal bool IsReadCommitted() => isolationLevel == IsolationLevel.ReadCommitted;

    /// <summary>
    /// Whether this transaction may see different committed data on re-read.
    /// </summary>
    public bool AllowNonRepeatableRead() => isolationLevel?.AllowNonRepeatableRead() ?? false;

    /// <summary>
    /// Marks the start of a statement: snapshots the maps it will touch.
    /// </summary>
    public void MarkStatementStart(HashSet<MVMap<object, VersionedValue<object>>>? maps)
    {
        MarkStatementEnd();
        if (txCounter is null && Store!.Store.IsVersioningRequired())
        {
            txCounter = Store.Store.RegisterVersionUsage();
        }

        if (maps is not null && maps.Count != 0)
        {
            // The purpose of the following loop is to get a coherent picture
            // In order to get such a "snapshot", we wait for a moment of silence,
            // when no new transaction were committed / closed.
            VersionedBitSet committingTransactions;
            do
            {
                committingTransactions = Store.CommittingTransactions;
                foreach (MVMap<object, VersionedValue<object>> map in maps)
                {
                    TransactionMap<object, object> txMap = OpenMapX<object, object>(map);
                    txMap.SetStatementSnapshot(new Snapshot<object, VersionedValue<object>>(
                        map.FlushAndGetRoot(), committingTransactions.Bits));
                }

                if (IsReadCommitted())
                {
                    undoLogRootReferences = Store.CollectUndoLogRootReferences();
                }
            } while (!ReferenceEquals(committingTransactions, Store.CommittingTransactions));
            // Now we have a snapshot, where each map RootReference point to state of the map,
            // undoLogRootReferences captures the state of undo logs
            // and committingTransactions mask tells us which of seemingly uncommitted changes
            // should be considered as committed.
            // Subsequent processing uses this snapshot info only.
            foreach (MVMap<object, VersionedValue<object>> map in maps)
            {
                TransactionMap<object, object> txMap = OpenMapX<object, object>(map);
                txMap.PromoteSnapshot();
            }
        }
    }

    /// <summary>
    /// Marks the end of a statement.
    /// </summary>
    public void MarkStatementEnd()
    {
        if (AllowNonRepeatableRead())
        {
            ReleaseSnapshot();
        }

        foreach (object transactionMap in transactionMaps.Values)
        {
            ((TransactionMap<object, object>)transactionMap).SetStatementSnapshot(null);
        }
    }

    private void MarkTransactionEnd()
    {
        if (!AllowNonRepeatableRead())
        {
            ReleaseSnapshot();
        }
    }

    private void ReleaseSnapshot()
    {
        transactionMaps.Clear();
        undoLogRootReferences = null;
        MVStore.TxCounter? counter = txCounter;
        if (counter is not null)
        {
            txCounter = null;
            Store!.Store.DeregisterVersionUsage(counter);
        }
    }

    /// <summary>
    /// Adds a log entry, answering its undo key.
    /// </summary>
    internal long Log(int mapId, object? key, VersionedValue<object>? oldValue)
    {
        long currentState = statusAndLogId.GetAndIncrement();
        long logId = GetLogId(currentState);
        if (logId >= LogIdLimit)
        {
            throw DataUtils.NewMvStoreException(
                    DataUtils.ErrorTransactionTooBig,
                    "Transaction {0} has too many changes",
                    TransactionId);
        }

        int currentStatus = GetStatus(currentState);
        CheckOpen(currentStatus);
        long undoKey = Store!.AddUndoLogRecord(TransactionId, logId, new Record<object, object>(mapId, key!, oldValue));
        return undoKey;
    }

    /// <summary>
    /// Removes the last log entry.
    /// </summary>
    internal void LogUndo()
    {
        long currentState = statusAndLogId.DecrementAndGet();
        long logId = GetLogId(currentState);
        if (logId >= LogIdLimit)
        {
            throw DataUtils.NewMvStoreException(
                    DataUtils.ErrorTransactionCorrupt,
                    "Transaction {0} has internal error",
                    TransactionId);
        }

        int currentStatus = GetStatus(currentState);
        CheckOpen(currentStatus);
        Store!.RemoveUndoLogRecord(TransactionId);
    }

    /// <summary>
    /// Opens a data map.
    /// </summary>
    public TransactionMap<K, V> OpenMap<K, V>(string name) => OpenMap<K, V>(name, null, null);

    /// <summary>
    /// Opens a data map with explicit types.
    /// </summary>
    /// <remarks>
    /// Every versioned map is keyed and valued by plain objects (see
    /// <see cref="TransactionStore"/>), so the types are adapted rather than installed directly.
    /// They still order the entries: a secondary index passes its row type here, and without
    /// it the object type would try to serialize the key.
    /// </remarks>
    public TransactionMap<K, V> OpenMap<K, V>(string name, DataType<K>? keyType, DataType<V>? valueType)
    {
        MVMap<object, VersionedValue<object>> map = Store!.OpenVersionedMap(name,
            keyType is null ? null : new ObjectMapDataType<K>(keyType),
            valueType is null ? null : new ObjectMapDataType<V>(valueType));
        return OpenMapX<K, V>(map);
    }

    /// <summary>
    /// Opens the transactional version of the given map.
    /// </summary>
    public TransactionMap<K, V> OpenMapX<K, V>(MVMap<object, VersionedValue<object>> map)
    {
        CheckNotClosed();
        int id = map.GetId();
        if (!transactionMaps.TryGetValue(id, out object? transactionMap))
        {
            transactionMap = new TransactionMap<K, V>(this, map);
            transactionMaps[id] = transactionMap;
        }

        return (TransactionMap<K, V>)transactionMap;
    }

    /// <summary>
    /// Prepares the transaction: afterward it can only be committed or rolled back.
    /// </summary>
    public void Prepare()
    {
        _ = SetStatus(StatusPrepared);
        Store!.StoreTransaction(this);
    }

    /// <summary>
    /// Commits the transaction, closing it.
    /// </summary>
    public void Commit()
    {
        Debug.Assert(!Store!.IsTransactionClosed(TransactionId));
        Exception? ex = null;
        bool wasActive = false;
        bool hasChanges = false;
        try
        {
            long lastState = SetStatus(StatusCommitted);
            hasChanges = HasChanges(lastState);
            int previousStatus = GetStatus(lastState);
            wasActive = IsActive(previousStatus);
            if (wasActive && hasChanges)
            {
                Store.Commit(this, previousStatus == StatusCommitted);
            }

            MarkTransactionEnd();
        }
        catch (Exception e)
        {
            if (wasActive)
            {
                ex = e;
                throw;
            }
        }
        finally
        {
            if (wasActive)
            {
                Close(hasChanges, ex);
            }
        }
    }

    internal void Close(bool hasChanges, Exception? ex)
    {
        try
        {
            Store!.EndTransaction(this, hasChanges);
        }
        catch (Exception)
        {
            if (ex is null)
            {
                throw;
            }

            throw ex;
        }
    }

    /// <summary>
    /// Rolls back to a savepoint. Only allowed while open.
    /// </summary>
    public void RollbackToSavepoint(long savepointId)
    {
        long lastState = SetStatus(StatusRollingBack);
        long logId = GetLogId(lastState);
        bool success;
        try
        {
            Store!.RollbackTo(this, logId, savepointId);
        }
        finally
        {
            long expectedState = ComposeState(StatusRollingBack, logId, HasRollback(lastState));
            long newState = ComposeState(StatusOpen, savepointId, true);
            do
            {
                success = statusAndLogId.CompareAndSet(expectedState, newState);
            } while (!success && statusAndLogId.Get() == expectedState);
        }

        // this is moved outside finally block to avert masking original exception, if any
        if (!success)
        {
            throw DataUtils.NewMvStoreException(
                    DataUtils.ErrorTransactionIllegalState,
                    "Transaction {0} concurrently modified while rollback to savepoint was in progress",
                    TransactionId);
        }
    }

    /// <summary>
    /// Rolls the transaction back, closing it.
    /// </summary>
    public void Rollback()
    {
        MarkTransactionEnd();
        Exception? ex = null;
        bool wasActive = false;
        bool hasChanges = false;
        try
        {
            long lastState = SetStatus(StatusRolledBack);
            wasActive = IsActive(GetStatus(lastState));
            long logId = GetLogId(lastState);
            if (logId > 0)
            {
                hasChanges = true;
                Store!.RollbackTo(this, logId, 0);
            }
        }
        catch (Exception e)
        {
            if (wasActive)
            {
                ex = e;
                throw;
            }
        }
        finally
        {
            if (wasActive)
            {
                Close(hasChanges, ex);
            }
        }
    }

    private static bool IsActive(int status)
        => status != StatusClosed
            && status != StatusCommitted
            && status != StatusRolledBack;

    /// <summary>
    /// The changes from the latest log id back to the savepoint.
    /// </summary>
    public IEnumerable<TransactionStore.Change> GetChanges(long savepointId)
        => Store!.GetChanges(this, GetLogId(), savepointId);

    /// <summary>
    /// Sets the lock timeout.
    /// </summary>
    public void SetTimeoutMillis(int timeoutMillis)
        => TimeoutMillis = timeoutMillis > 0 ? timeoutMillis : store?.TimeoutMillis ?? 0;

    internal long GetLogId() => GetLogId(statusAndLogId.Get());

    private void CheckOpen(int status)
    {
        if (status != StatusOpen)
        {
            throw DataUtils.NewMvStoreException(
                    DataUtils.ErrorTransactionIllegalState,
                    "Transaction {0} has status {1}, not OPEN", TransactionId, GetStatusName(status));
        }
    }

    private void CheckNotClosed()
    {
        if (GetStatus() == StatusClosed)
        {
            throw DataUtils.NewMvStoreException(
                    DataUtils.ErrorClosed, "Transaction {0} is closed", TransactionId);
        }
    }

    /// <summary>
    /// Moves to closed, releasing the held version.
    /// </summary>
    internal void CloseIt()
    {
        transactionMaps.Clear();
        long lastState = SetStatus(StatusClosed);
        if (GetStatus(lastState) != StatusClosed)
        {
            MVStore.TxCounter? counter = txCounter;
            if (counter is not null)
            {
                Store!.Store.DeregisterVersionUsage(counter);
            }
        }
    }

    internal void NotifyAllWaitingTransactions()
    {
        if (notificationRequested)
        {
            lock (sync)
            {
                Monitor.PulseAll(sync);
            }
        }
    }

    /// <summary>
    /// Waits for another transaction touching the same entry to end.
    /// </summary>
    /// <returns>true when it ended, false on timeout</returns>
    public bool WaitFor(Transaction toWaitFor, string? mapName, object? key, int timeoutMillis)
    {
        blockingTransaction = toWaitFor;
        blockingMapName = mapName;
        blockingKey = key;
        if (IsDeadlocked(toWaitFor))
        {
            TryThrowDeadLockException(false);
        }

        bool result = toWaitFor.WaitForThisToEnd(timeoutMillis == -1 ? TimeoutMillis : timeoutMillis, this);
        blockingMapName = null;
        blockingKey = null;
        blockingTransaction = null;
        return result;
    }

    private bool IsDeadlocked(Transaction toWaitFor)
    {
        // use transaction sequence No as a tie-breaker
        // the youngest transaction should be selected as a victim
        Transaction youngest = toWaitFor;
        int backstop = Store!.GetMaxTransactionId();
        for (Transaction tx = toWaitFor, nextTx;
            (nextTx = tx.blockingTransaction!) is not null && tx.GetStatus() == StatusOpen && backstop > 0;
            tx = nextTx, --backstop)
        {
            if (nextTx.sequenceNum > youngest.sequenceNum)
            {
                youngest = nextTx;
            }

            if (ReferenceEquals(nextTx, this))
            {
                if (ReferenceEquals(youngest, this))
                {
                    return true;
                }

                Transaction? btx = youngest.blockingTransaction;
                if (btx is not null)
                {
                    _ = youngest.SetStatus(StatusRollingBack);
                    btx.NotifyAllWaitingTransactions();
                    return false;
                }
            }
        }

        return false;
    }

    private void TryThrowDeadLockException(bool throwIt)
    {
        var visited = new HashSet<int>();
        var details = new StringBuilder(
                string.Format("Transaction {0} has been chosen as a deadlock victim. Details:\n", TransactionId));
        for (Transaction tx = this, nextTx;
                !visited.Contains(tx.TransactionId) && (nextTx = tx.blockingTransaction!) is not null; tx = nextTx)
        {
            _ = visited.Add(tx.TransactionId);
            _ = details.Append(string.Format(
                    "Transaction {0} attempts to update map <{1}> entry with key <{2}> modified by transaction {3}\n",
                    tx.TransactionId, tx.blockingMapName, tx.blockingKey, tx.blockingTransaction));
            if (ReferenceEquals(nextTx, this))
            {
                throwIt = true;
            }
        }

        if (throwIt)
        {
            throw DataUtils.NewMvStoreException(DataUtils.ErrorTransactionsDeadlock, "{0}", details.ToString());
        }
    }

    private bool WaitForThisToEnd(int millis, Transaction waiter)
    {
        lock (sync)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            notificationRequested = true;
            long state;
            while (IsActive(GetStatus(state = statusAndLogId.Get())) && !HasRollback(state))
            {
                if (waiter.GetStatus() != StatusOpen)
                {
                    waiter.TryThrowDeadLockException(true);
                }

                int elapsed = (int)((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000L
                    / System.Diagnostics.Stopwatch.Frequency);
                int remaining = millis - elapsed;
                if (remaining <= 0)
                {
                    return false;
                }

                try
                {
                    _ = Monitor.Wait(sync, remaining);
                }
                catch (ThreadInterruptedException)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Removes the map.
    /// </summary>
    public void RemoveMap<K, V>(TransactionMap<K, V> map) => Store!.RemoveMap(map.InnerMap);

    public override string ToString() => TransactionId + "(" + sequenceNum + ") " + StateToString();

    private string StateToString() => StateToString(statusAndLogId.Get());

    private static string StateToString(long state)
        => GetStatusName(GetStatus(state)) + (HasRollback(state) ? "<" : "") + " " + GetLogId(state);

    private static int GetStatus(long state) => (int)(state >> LogIdBits1) & StatusMask;

    private static long GetLogId(long state) => state & LogIdMask;

    private static bool HasRollback(long state) => (state & (1L << (StatusBits + LogIdBits1))) != 0;

    private static bool HasChanges(long state) => GetLogId(state) != 0;

    private static long ComposeState(int status, long logId, bool hasRollback)
    {
        Debug.Assert(logId < LogIdLimit, logId.ToString());
        Debug.Assert((status & ~StatusMask) == 0, status.ToString());

        if (hasRollback)
        {
            status |= 1 << StatusBits;
        }

        return ((long)status << LogIdBits1) | logId;
    }

    private static string GetStatusName(int status)
        => status >= 0 && status < StatusNames.Length ? StatusNames[status] : "UNKNOWN_STATUS_" + status;
}
