using System;
using H2Sharp.Engine;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using H2Sharp.MvStore.Type;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// The non-generic statement-snapshot surface of <see cref="TransactionMap{K,V}"/>.
/// </summary>
internal interface ITransactionMap
{
    void SetStatementSnapshot(Snapshot<object, VersionedValue<object>>? snapshot);
}

/// <summary>
/// A map with transactional reads and writes over one <see cref="Transaction"/>.
/// Keys and values cross the boundary as <c>K</c>/<c>V</c>; inside, everything is an
/// object holding a <see cref="VersionedValue{T}"/>.
/// </summary>
public sealed class TransactionMap<K, V> : ITransactionMap
{
    /// <summary>
    /// The map used for writing (the latest version).
    /// </summary>
    public readonly MVMap<object, VersionedValue<object>> map;

    private readonly Transaction transaction;

    private Snapshot<object, VersionedValue<object>>? snapshot;

    private Snapshot<object, VersionedValue<object>>? statementSnapshot;

    private bool hasChanges;

    private readonly TxDecisionMaker<K, V> txDecisionMaker;
    private readonly TxDecisionMaker<K, V>.PutIfAbsentDecisionMaker ifAbsentDecisionMaker;
    private readonly TxDecisionMaker<K, V>.LockDecisionMaker lockDecisionMaker;

    internal TransactionMap(Transaction transaction, MVMap<object, VersionedValue<object>> map)
    {
        this.transaction = transaction;
        this.map = map;
        txDecisionMaker = new TxDecisionMaker<K, V>(map.GetId(), transaction);
        ifAbsentDecisionMaker = new TxDecisionMaker<K, V>.PutIfAbsentDecisionMaker(map.GetId(),
                transaction, GetFromSnapshot);
        lockDecisionMaker = transaction.AllowNonRepeatableRead()
                ? new TxDecisionMaker<K, V>.LockDecisionMaker(map.GetId(), transaction)
                : new TxDecisionMaker<K, V>.RepeatableReadLockDecisionMaker(map.GetId(), transaction,
                        map.GetValueType(), GetFromSnapshot);
    }

    /// <summary>
    /// The underlying map, for the transaction store's commit and rollback passes.
    /// </summary>
    internal MVMap<object, VersionedValue<object>> InnerMap => map;

    /// <summary>
    /// A clone of this map for the given transaction.
    /// </summary>
    public TransactionMap<K, V> GetInstance(Transaction transaction) => transaction.OpenMapX<K, V>(map);

    /// <summary>
    /// The number of entries, capped at int range.
    /// </summary>
    public int Size()
    {
        long size = SizeAsLong();
        return size > int.MaxValue ? int.MaxValue : (int)size;
    }

    /// <summary>
    /// The size of the raw map, including uncommitted and transiently removed entries.
    /// </summary>
    public long SizeAsLongMax() => map.SizeAsLong();

    /// <summary>
    /// The size as seen by this transaction.
    /// </summary>
    public long SizeAsLong()
    {
        IsolationLevel? isolationLevel = transaction.GetIsolationLevel();
        if (isolationLevel is not null && !isolationLevel.Value.AllowNonRepeatableRead() && hasChanges)
        {
            return SizeAsLongRepeatableReadWithChanges();
        }

        // getting coherent picture of the map, committing transactions, and undo logs
        // either from values stored in transaction (never loops in that case),
        // or current values from the transaction store (loops until moment of silence)
        Snapshot<object, VersionedValue<object>> snapshot;
        RootReference<long, Record<object, object>>[]? undoLogRootReferences;
        do
        {
            snapshot = GetSnapshot();
            undoLogRootReferences = transaction.GetUndoLogRootReferences();
        } while (!snapshot.Equals(GetSnapshot()));

        RootReference<object, VersionedValue<object>> mapRootReference = snapshot.Root;
        long size = mapRootReference.GetTotalCount();
        long undoLogsTotalSize = undoLogRootReferences is null ? size
                : TransactionStore.CalculateUndoLogsTotalSize(undoLogRootReferences);
        // if we are looking at the map without any uncommitted values
        if (undoLogsTotalSize == 0)
        {
            return size;
        }

        return AdjustSize(undoLogRootReferences, mapRootReference,
                isolationLevel == IsolationLevel.ReadUncommitted ? null : snapshot.CommittingTransactions,
                size, undoLogsTotalSize);
    }

    private long AdjustSize(RootReference<long, Record<object, object>>[]? undoLogRootReferences,
            RootReference<object, VersionedValue<object>> mapRootReference, long[]? committingTransactions,
            long size, long undoLogsTotalSize)
    {
        // Entries describing removals from the map by this transaction and all transactions,
        // which are committed but not closed yet,
        // and entries about additions to the map by other uncommitted transactions were counted,
        // but they should not contribute into total count.
        if (2 * undoLogsTotalSize > size)
        {
            // the undo log is larger than half of the map - scan the entries of the map directly
            Cursor<object, VersionedValue<object>> cursor = map.Cursor(mapRootReference, null, null, false);
            while (cursor.HasNext())
            {
                _ = cursor.Next();
                VersionedValue<object>? currentValue = cursor.GetValue();
                Debug.Assert(currentValue is not null);
                long operationId = currentValue!.GetOperationId();
                if (operationId != VersionedValue<object>.NoOperationId &&
                        IsIrrelevant(operationId, currentValue, committingTransactions))
                {
                    --size;
                }
            }
        }
        else
        {
            Debug.Assert(undoLogRootReferences is not null);
            // The undo logs are much smaller than the map - scan all undo logs,
            // and then lookup relevant map entry.
            foreach (RootReference<long, Record<object, object>>? undoLogRootReference in undoLogRootReferences!)
            {
                if (undoLogRootReference is not null)
                {
                    Cursor<long, Record<object, object>> cursor =
                        undoLogRootReference.root.map.Cursor(undoLogRootReference, null, null, false);
                    while (cursor.HasNext())
                    {
                        _ = cursor.Next();
                        Record<object, object>? op = cursor.GetValue();
                        if (op!.MapId == map.GetId())
                        {
                            VersionedValue<object>? currentValue =
                                map.Get(mapRootReference.root, (K)op.Key!);
                            // If map entry is not there, then we never counted
                            // it, in the first place, so skip it.
                            // This is possible when undo entry exists because
                            // it belongs to a committed but not yet closed
                            // transaction, and it was later deleted by some
                            // other already committed and closed transaction.
                            if (currentValue is not null)
                            {
                                // only the last undo entry for any given map
                                // key should be considered
                                long operationId = cursor.GetKey();
                                Debug.Assert(operationId != VersionedValue<object>.NoOperationId);
                                if (currentValue.GetOperationId() == operationId &&
                                        IsIrrelevant(operationId, currentValue, committingTransactions))
                                {
                                    --size;
                                }
                            }
                        }
                    }
                }
            }
        }

        return size;
    }

    private bool IsIrrelevant(long operationId, VersionedValue<object> currentValue, long[]? committingTransactions)
    {
        object? v;
        if (committingTransactions is null)
        {
            v = currentValue.GetCurrentValue();
        }
        else
        {
            int txId = TransactionStore.GetTransactionId(operationId);
            v = txId == transaction.TransactionId || BitSetHelper.Get(committingTransactions, txId)
                    ? currentValue.GetCurrentValue() : currentValue.GetCommittedValue();
        }

        return v is null;
    }

    private long SizeAsLongRepeatableReadWithChanges()
    {
        long count = 0L;
        var iterator = new RepeatableIterator<K, V, K>(this, null, null, false);
        while (iterator.TryFetchNext(out _))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Removes the entry, retrying while it is locked by another transaction.
    /// </summary>
    public V? Remove(object? key) => Set((K)key!, null);

    /// <summary>
    /// Puts the value, retrying while the entry is locked by another transaction.
    /// </summary>
    public V? Put(K key, V value)
    {
        DataUtils.CheckArgument(value is not null, "The value may not be null");
        return Set(key, value);
    }

    /// <summary>
    /// Puts when nothing is there: contains-then-put, atomically.
    /// </summary>
    public V? PutIfAbsent(K key, V value)
    {
        DataUtils.CheckArgument(value is not null, "The value may not be null");
        ifAbsentDecisionMaker.Initialize(key, value);
        V? result = Set(key, ifAbsentDecisionMaker, -1);
        if (ifAbsentDecisionMaker.GetDecision() == Decision.Abort)
        {
            result = ifAbsentDecisionMaker.GetLastValue();
        }

        return result;
    }

    /// <summary>
    /// Appends an entry without an undo log read: the key must sort after every existing one.
    /// </summary>
    public void Append(K key, V value)
    {
        long undoKey = transaction.Log(map.GetId(), key, null);
        long entryId = TransactionStore.GetLogId(undoKey);
        map.Append((object)key!, VersionedValueUncommitted<object>.GetInstance<object>(
            undoKey, (object?)value, null, entryId));
        hasChanges = true;
    }

    /// <summary>
    /// Locks the row.
    /// </summary>
    public V? Lock(K key) => Lock(key, -1);

    /// <summary>
    /// Locks the row, waiting at most the timeout (-1 for default, -2 to skip locked rows).
    /// </summary>
    public V? Lock(K key, int timeoutMillis)
    {
        lockDecisionMaker.Initialize(key, null);
        return Set(key, lockDecisionMaker, timeoutMillis);
    }

    /// <summary>
    /// Puts without an undo log entry.
    /// </summary>
    public V? PutCommitted(K key, V value)
    {
        DataUtils.CheckArgument(value is not null, "The value may not be null");
        VersionedValue<object>? newValue = VersionedValueCommitted<object>.GetInstance((object?)value);
        VersionedValue<object>? oldValue = map.Put((object)key!, newValue!);
        object? result = oldValue is null ? null : oldValue.GetCurrentValue();
        return TransactionStore.Unbox<V>(result);
    }

    /// <summary>
    /// Sets the remapped value when the entry exists, atomically.
    /// </summary>
    public V? ComputeIfPresent(K key, Func<K, V?, V?> remapFunction)
    {
        V? oldValue = Get(key);
        if (oldValue is not null)
        {
            V? newValue = remapFunction(key, oldValue);
            if (newValue is not null)
            {
                Put(key, newValue);
            }
            else
            {
                _ = Remove(key);
            }

            return newValue;
        }

        return default;
    }

    private V? Set(K key, object? value)
    {
        txDecisionMaker.Initialize(key, value);
        return Set(key, txDecisionMaker, -1);
    }

    private V? Set(object? key, TxDecisionMaker<K, V> decisionMaker, int timeoutMillis)
    {
        Transaction? blockingTransaction;
        VersionedValue<object>? result;
        string? mapName = null;
        do
        {
            Debug.Assert(transaction.GetBlockerId() == 0);
            K k = (K)key!;
            // second parameter (value) is not really used,
            // since TxDecisionMaker has it embedded
            result = map.Operate((object)k!, null, decisionMaker);

            Decision? decision = decisionMaker.GetDecision();
            Debug.Assert(decision is not null);
            Debug.Assert(decision != Decision.Repeat);
            blockingTransaction = decisionMaker.GetBlockingTransaction();
            if (decision != Decision.Abort || blockingTransaction is null)
            {
                hasChanges |= decision != Decision.Abort;
                object? res = result is null ? null : result.GetCurrentValue();
                return TransactionStore.Unbox<V>(res);
            }

            decisionMaker.Reset();
            if (timeoutMillis == -2)
            {
                return default;
            }

            mapName ??= map.GetName();
        } while (timeoutMillis != 0 && transaction.WaitFor(blockingTransaction, mapName, key, timeoutMillis));

        throw DataUtils.NewMvStoreException(DataUtils.ErrorTransactionLocked,
                "Map entry <{0}> with key <{1}> and value {2} is locked by tx {3} and can not be updated by tx {4}"
                        + " within allocated time interval {5} ms.",
                mapName, key, result, blockingTransaction.TransactionId, transaction.TransactionId,
                timeoutMillis == -1 ? transaction.TimeoutMillis : timeoutMillis);
    }

    /// <summary>
    /// Removes when not locked by another transaction.
    /// </summary>
    public bool TryRemove(K key) => TrySet(key, default);

    /// <summary>
    /// Puts when not locked by another transaction.
    /// </summary>
    public bool TryPut(K key, V value)
    {
        DataUtils.CheckArgument(value is not null, "The value may not be null");
        return TrySet(key, value);
    }

    /// <summary>
    /// Sets when not locked by another transaction.
    /// </summary>
    public bool TrySet(K key, V? value)
    {
        try
        {
            // TODO: effective transaction.timeoutMillis should be set to 0 here
            // and restored before return
            // TODO: eliminate exception usage as part of normal control flaw
            _ = Set(key, value);
            return true;
        }
        catch (MvStoreException)
        {
            return false;
        }
    }

    /// <summary>
    /// The effective value for the key.
    /// </summary>
    public V? Get(object? key) => GetImmediate((K)key!);

    /// <summary>
    /// The value under transactional rules, from the appropriate snapshot.
    /// </summary>
    public V? GetFromSnapshot(K key)
    {
        switch (transaction.GetIsolationLevel())
        {
            case IsolationLevel.ReadUncommitted:
                {
                    Snapshot<object, VersionedValue<object>> snapshot = GetStatementSnapshot();
                    VersionedValue<object>? data = map.Get(snapshot.Root.root, (object)key!);
                    if (data is not null)
                    {
                        return TransactionStore.Unbox<V>(data.GetCurrentValue());
                    }

                    return default;
                }

            case IsolationLevel.RepeatableRead:
            case IsolationLevel.Snapshot:
            case IsolationLevel.Serializable:
                if (transaction.HasChanges())
                {
                    Snapshot<object, VersionedValue<object>> snapshot = GetStatementSnapshot();
                    VersionedValue<object>? data = map.Get(snapshot.Root.root, (object)key!);
                    if (data is not null)
                    {
                        long id = data.GetOperationId();
                        if (id != VersionedValue<object>.NoOperationId
                            && transaction.TransactionId == TransactionStore.GetTransactionId(id))
                        {
                            return TransactionStore.Unbox<V>(data.GetCurrentValue());
                        }
                    }
                }

                goto case IsolationLevel.ReadCommitted;
            case IsolationLevel.ReadCommitted:
            default:
                {
                    Snapshot<object, VersionedValue<object>> snapshot = GetSnapshot();
                    return GetFromSnapshot(snapshot.Root, snapshot.CommittingTransactions, key);
                }
        }
    }

    private V? GetFromSnapshot(RootReference<object, VersionedValue<object>> rootRef,
        long[] committingTransactions, K key)
        => TransactionStore.Unbox<V>(GetFromSnapshotObject(rootRef, committingTransactions, key));

    private object? GetFromSnapshotObject(RootReference<object, VersionedValue<object>> rootRef,
        long[] committingTransactions, K key)
    {
        VersionedValue<object>? data = map.Get(rootRef.root, (object)key!);
        if (data is null)
        {
            // doesn't exist
            return null;
        }

        long id = data.GetOperationId();
        if (id != VersionedValue<object>.NoOperationId)
        {
            int tx = TransactionStore.GetTransactionId(id);
            if (tx != transaction.TransactionId && !BitSetHelper.Get(committingTransactions, tx))
            {
                // added/modified/removed by uncommitted transaction, change should not be visible
                return data.GetCommittedValue();
            }
        }

        // added/modified/removed by this transaction or another transaction which is committed by now
        return data.GetCurrentValue();
    }

    /// <summary>
    /// The value from a snapshot taken during the call.
    /// </summary>
    public V? GetImmediate(K key)
        => TransactionStore.Unbox<V>(UseSnapshot((rootReference, committedTransactions) =>
                                GetFromSnapshotObject(rootReference, committedTransactions, key)));

    internal Snapshot<object, VersionedValue<object>> GetSnapshot()
        => transaction.AllowNonRepeatableRead()
            ? CreateSnapshot()
            : snapshot ??= CreateSnapshot();

    internal Snapshot<object, VersionedValue<object>> GetStatementSnapshot()
        => statementSnapshot ?? CreateSnapshot();

    internal void SetStatementSnapshot(Snapshot<object, VersionedValue<object>>? snapshot)
        => statementSnapshot = snapshot;

    void ITransactionMap.SetStatementSnapshot(Snapshot<object, VersionedValue<object>>? snapshot)
        => SetStatementSnapshot(snapshot);

    internal void PromoteSnapshot()
    {
        snapshot ??= statementSnapshot;
    }

    /// <summary>
    /// A new snapshot of this map.
    /// </summary>
    internal Snapshot<object, VersionedValue<object>> CreateSnapshot() => UseSnapshot(
        (root, committing) => new Snapshot<object, VersionedValue<object>>(root, committing));

    private R UseSnapshot<R>(Func<RootReference<object, VersionedValue<object>>, long[], R> snapshotConsumer)
    {
        // The purpose of the following loop is to get a coherent picture
        // of a state of two independent volatile / atomic variables,
        // which they had at some recent moment in time.
        // In order to get such a "snapshot", we wait for a moment of silence,
        // when neither of the variables concurrently changes it's value.
        TransactionStore store = transaction.Store!;
        VersionedBitSet committingTransactions = store.CommittingTransactions;
        while (true)
        {
            VersionedBitSet prevCommittingTransactions = committingTransactions;
            RootReference<object, VersionedValue<object>> root = map.GetRoot();
            committingTransactions = store.CommittingTransactions;
            if (ReferenceEquals(committingTransactions, prevCommittingTransactions))
            {
                return snapshotConsumer(root, committingTransactions.Bits);
            }
        }
    }

    /// <summary>
    /// Whether the map holds the key, transactionally. Decided on the entry itself rather
    /// than its value, so a missing value-typed entry does not read as present.
    /// </summary>
    public bool ContainsKey(object? key)
        => UseSnapshot((rootReference, committedTransactions) =>
            GetFromSnapshotObject(rootReference, committedTransactions, (K)key!)) is not null;

    /// <summary>
    /// Whether the row was deleted by this transaction.
    /// </summary>
    public bool IsDeletedByCurrentTransaction(K key)
    {
        VersionedValue<object>? data = map.Get((object)key!);
        if (data is not null)
        {
            long id = data.GetOperationId();
            return id != VersionedValue<object>.NoOperationId
                && TransactionStore.GetTransactionId(id) == transaction.TransactionId
                && data.GetCurrentValue() is null;
        }

        return false;
    }

    /// <summary>
    /// Whether the entry was added or removed in this transaction.
    /// </summary>
    public bool IsSameTransaction(K key)
    {
        VersionedValue<object>? data = map.Get((object)key!);
        if (data is null)
        {
            // doesn't exist or deleted by a committed transaction
            return false;
        }

        int tx = TransactionStore.GetTransactionId(data.GetOperationId());
        return tx == transaction.TransactionId;
    }

    /// <summary>
    /// Whether the map is closed.
    /// </summary>
    public bool IsClosed() => map.IsClosed();

    /// <summary>
    /// Whether the map holds no entries as seen by this transaction.
    /// </summary>
    public bool IsEmpty() => Size() == 0;

    /// <summary>
    /// Drops all entries.
    /// </summary>
    public void Clear()
    {
        // TODO truncate transactionally?
        map.Clear();
        hasChanges = true;
    }

    /// <summary>
    /// The entries as seen by this transaction.
    /// </summary>
    public IEnumerable<KeyValuePair<K, V>> EntrySet()
    {
        TMIterator<K, V, KeyValuePair<K, V>> it = EntryIterator(null, null);
        while (it.TryFetchNext(out KeyValuePair<K, V> next))
        {
            yield return next;
        }
    }

    /// <summary>
    /// The first entry, or null when empty.
    /// Bounds travel as <c>object</c> because a missing bound is null, and a default key
    /// (0 for numbers) is a real key, not an unbounded end.
    /// </summary>
    public KeyValuePair<K, V>? FirstEntry()
    {
        var it = ChooseIterator<KeyValuePair<K, V>>(null, null, false);
        if (it.TryFetchNext(out KeyValuePair<K, V> value))
        {
            return value;
        }

        return null;
    }

    /// <summary>
    /// The first key, or default when empty.
    /// </summary>
    public K? FirstKey()
    {
        var it = ChooseIterator<K>(null, null, false);
        return it.TryFetchNext(out K value) ? value : default;
    }

    /// <summary>
    /// The last entry, or null when empty.
    /// </summary>
    public KeyValuePair<K, V>? LastEntry()
    {
        var it = ChooseIterator<KeyValuePair<K, V>>(null, null, true);
        if (it.TryFetchNext(out KeyValuePair<K, V> value))
        {
            return value;
        }

        return null;
    }

    /// <summary>
    /// The last key, or default when empty.
    /// </summary>
    public K? LastKey()
    {
        var it = ChooseIterator<K>(null, null, true);
        return it.TryFetchNext(out K value) ? value : default;
    }

    /// <summary>
    /// The entry above the key.
    /// </summary>
    public KeyValuePair<K, V>? HigherEntry(K key) => HigherLowerEntry(key, false);

    /// <summary>
    /// The key above the key.
    /// </summary>
    public K? HigherKey(K key) => HigherLowerKey(key, false);

    /// <summary>
    /// The entry at or above the key, or default when missing.
    /// </summary>
    public KeyValuePair<K, V>? CeilingEntry(K key)
    {
        var it = ChooseIterator<KeyValuePair<K, V>>((object?)key, null, false);
        if (it.TryFetchNext(out KeyValuePair<K, V> value))
        {
            return value;
        }

        return null;
    }

    /// <summary>
    /// The key at or above the key, or default when missing.
    /// </summary>
    public K? CeilingKey(K key)
    {
        var it = ChooseIterator<K>((object?)key, null, false);
        return it.TryFetchNext(out K value) ? value : default;
    }

    /// <summary>
    /// The entry at or below the key, or default when missing.
    /// </summary>
    public KeyValuePair<K, V>? FloorEntry(K key)
    {
        var it = ChooseIterator<KeyValuePair<K, V>>((object?)key, null, true);
        if (it.TryFetchNext(out KeyValuePair<K, V> value))
        {
            return value;
        }

        return null;
    }

    /// <summary>
    /// The key at or below the key, or default when missing.
    /// </summary>
    public K? FloorKey(K key)
    {
        var it = ChooseIterator<K>((object?)key, null, true);
        return it.TryFetchNext(out K value) ? value : default;
    }

    /// <summary>
    /// The entry below the key.
    /// </summary>
    public KeyValuePair<K, V>? LowerEntry(K key) => HigherLowerEntry(key, true);

    /// <summary>
    /// The key below the key.
    /// </summary>
    public K? LowerKey(K key) => HigherLowerKey(key, true);

    private KeyValuePair<K, V>? HigherLowerEntry(K key, bool lower)
    {
        var it = ChooseIterator<KeyValuePair<K, V>>((object?)key, null, lower);
        KeyValuePair<K, V>? result = TryFetchEntry(it);
        if (result is not null && map.GetKeyType().Compare((object?)key, (object?)result.Value.Key) == 0)
        {
            result = TryFetchEntry(it);
        }

        return result;
    }

    private static KeyValuePair<K, V>? TryFetchEntry(TMIterator<K, V, KeyValuePair<K, V>> it)
    {
        if (it.TryFetchNext(out KeyValuePair<K, V> value))
        {
            return value;
        }

        return null;
    }

    private K? HigherLowerKey(K key, bool lower)
    {
        var it = ChooseIterator<K>((object?)key, null, lower);
        K? result = it.TryFetchNext(out K first) ? first : default;
        if (map.GetKeyType().Compare((object?)key, (object?)result) == 0)
        {
            result = it.TryFetchNext(out K second) ? second : default;
        }

        return result;
    }

    /// <summary>
    /// Iterates keys from the first one. Bounds are objects: null is unbounded, and a
    /// default key is a real bound.
    /// </summary>
    public TMIterator<K, V, K> KeyIterator(object? from) => ChooseIterator<K>(from, null, false);

    /// <summary>
    /// Iterates keys in the given order.
    /// </summary>
    public TMIterator<K, V, K> KeyIterator(object? from, bool reverse)
        => ChooseIterator<K>(from, null, reverse);

    /// <summary>
    /// Iterates keys in a range.
    /// </summary>
    public TMIterator<K, V, K> KeyIterator(object? from, object? to)
        => ChooseIterator<K>(from, to, false);

    /// <summary>
    /// Iterates keys in a range and order.
    /// </summary>
    public TMIterator<K, V, K> KeyIterator(object? from, object? to, bool reverse)
        => ChooseIterator<K>(from, to, reverse);

    /// <summary>
    /// Iterates keys, including uncommitted entries.
    /// </summary>
    public TMIterator<K, V, K> KeyIteratorUncommitted(object? from, object? to)
        => new ValidationIterator<K, V, K>(this, from, to);

    /// <summary>
    /// Iterates entries in a range.
    /// </summary>
    public TMIterator<K, V, KeyValuePair<K, V>> EntryIterator(object? from, object? to)
        => ChooseIterator<KeyValuePair<K, V>>(from, to, false);

    /// <summary>
    /// Iterates entries in a range and order.
    /// </summary>
    public TMIterator<K, V, KeyValuePair<K, V>> EntryIterator(object? from, object? to, bool reverse)
        => ChooseIterator<KeyValuePair<K, V>>(from, to, reverse);

    private TMIterator<K, V, X> ChooseIterator<X>(object? from, object? to, bool reverse)
    {
        switch (transaction.GetIsolationLevel())
        {
            case IsolationLevel.ReadUncommitted:
                return new UncommittedIterator<K, V, X>(this, from, to, reverse);
            case IsolationLevel.RepeatableRead:
            case IsolationLevel.Snapshot:
            case IsolationLevel.Serializable:
                if (hasChanges)
                {
                    return new RepeatableIterator<K, V, X>(this, from, to, reverse);
                }

                goto case IsolationLevel.ReadCommitted;
            case IsolationLevel.ReadCommitted:
            default:
                return new CommittedIterator<K, V, X>(this, from, to, reverse);
        }
    }

    /// <summary>
    /// The transaction.
    /// </summary>
    public Transaction GetTransaction() => transaction;

    /// <summary>
    /// The key type of the underlying map.
    /// </summary>
    public DataType<object> GetKeyType() => map.GetKeyType();

    /// <summary>
    /// Reads at read-uncommitted: current values, skipping removals this iterator ignores.
    /// </summary>
    private class UncommittedIterator<KK, VV, X> : TMIterator<KK, VV, X>
    {
        internal UncommittedIterator(TransactionMap<KK, VV> transactionMap, object? from, object? to,
            bool reverse)
            : base(transactionMap, from, to, transactionMap.CreateSnapshot(), reverse)
        {
        }

        internal UncommittedIterator(TransactionMap<KK, VV> transactionMap, object? from, object? to,
            Snapshot<object, VersionedValue<object>> snapshot, bool reverse)
            : base(transactionMap, from, to, snapshot, reverse)
        {
        }

        public override bool TryFetchNext(out X value)
        {
            while (cursor.HasNext())
            {
                object key = cursor.Next();
                VersionedValue<object>? data = cursor.GetValue();
                if (data is not null)
                {
                    object? currentValue = data.GetCurrentValue();
                    if (currentValue is not null || ShouldIgnoreRemoval(data))
                    {
                        value = ToElement(key, currentValue);
                        return true;
                    }
                }
            }

            value = default!;
            return false;
        }

        internal virtual bool ShouldIgnoreRemoval(VersionedValue<object> data) => false;
    }

    private sealed class ValidationIterator<KK, VV, X> : UncommittedIterator<KK, VV, X>
    {
        internal ValidationIterator(TransactionMap<KK, VV> transactionMap, object? from, object? to)
            : base(transactionMap, from, to, transactionMap.CreateSnapshot(), false)
        {
        }

        internal override bool ShouldIgnoreRemoval(VersionedValue<object> data)
        {
            Debug.Assert(data.GetCurrentValue() is null);
            long id = data.GetOperationId();
            if (id != VersionedValue<object>.NoOperationId)
            {
                int tx = TransactionStore.GetTransactionId(id);
                return transactionId != tx && !BitSetHelper.Get(committingTransactions, tx);
            }

            return false;
        }
    }

    private sealed class CommittedIterator<KK, VV, X> : TMIterator<KK, VV, X>
    {
        internal CommittedIterator(TransactionMap<KK, VV> transactionMap, object? from, object? to,
            bool reverse)
            : base(transactionMap, from, to, transactionMap.GetSnapshot(), reverse)
        {
        }

        public override bool TryFetchNext(out X value)
        {
            while (cursor.HasNext())
            {
                object key = cursor.Next();
                VersionedValue<object>? data = cursor.GetValue();
                // If value doesn't exist, or it was deleted by a committed transaction,
                // or if value is a committed one, just return it.
                if (data is not null)
                {
                    long id = data.GetOperationId();
                    if (id != VersionedValue<object>.NoOperationId)
                    {
                        int tx = TransactionStore.GetTransactionId(id);
                        if (tx != transactionId && !BitSetHelper.Get(committingTransactions, tx))
                        {
                            // current value comes from another uncommitted transaction
                            // take committed value instead
                            object? committedValue = data.GetCommittedValue();
                            if (committedValue is null)
                            {
                                continue;
                            }

                            value = ToElement(key!, committedValue);
                            return true;
                        }
                    }

                    object? currentValue = data.GetCurrentValue();
                    if (currentValue is not null)
                    {
                        value = ToElement(key!, currentValue);
                        return true;
                    }
                }
            }

            value = default!;
            return false;
        }
    }

    private sealed class RepeatableIterator<KK, VV, X> : TMIterator<KK, VV, X>
    {
        private readonly DataType<object> keyType;

        private object? snapshotKey;

        private object? snapshotValue;

        private readonly Cursor<object, VersionedValue<object>> uncommittedCursor;

        private object? uncommittedKey;

        private object? uncommittedValue;

        internal RepeatableIterator(TransactionMap<KK, VV> transactionMap, object? from, object? to,
            bool reverse)
            : base(transactionMap, from, to, transactionMap.GetSnapshot(), reverse)
        {
            keyType = transactionMap.map.GetKeyType();
            Snapshot<object, VersionedValue<object>> snapshot = transactionMap.GetStatementSnapshot();
            uncommittedCursor = transactionMap.map.Cursor(snapshot.Root, from, to, reverse);
        }

        public override bool TryFetchNext(out X value)
        {
            while (true)
            {
                if (snapshotKey is null)
                {
                    FetchSnapshot();
                }

                if (uncommittedKey is null)
                {
                    FetchUncommitted();
                }

                if (snapshotKey is null && uncommittedKey is null)
                {
                    break;
                }

                int cmp = snapshotKey is null ? 1 :
                            uncommittedKey is null ? -1 :
                            keyType.Compare(snapshotKey, uncommittedKey);
                if (cmp < 0)
                {
                    value = ToElement(snapshotKey!, snapshotValue);
                    snapshotKey = null;
                    return true;
                }

                if (uncommittedValue is not null)
                {
                    // This entry was added / updated by this transaction, use the new value
                    value = ToElement(uncommittedKey!, uncommittedValue);
                    if (cmp == 0)
                    {
                        // This entry was updated / deleted
                        snapshotKey = null;
                    }

                    uncommittedKey = null;
                    return true;
                }

                if (cmp == 0)
                {
                    snapshotKey = null;
                }

                uncommittedKey = null;
            }

            value = default!;
            return false;
        }

        private void FetchSnapshot()
        {
            while (cursor.HasNext())
            {
                object key = cursor.Next();
                VersionedValue<object>? data = cursor.GetValue();
                // If value doesn't exist, or it was deleted by a committed transaction,
                // or if value is a committed one, just return it.
                if (data is not null)
                {
                    object? value = data.GetCommittedValue();
                    long id = data.GetOperationId();
                    if (id != VersionedValue<object>.NoOperationId)
                    {
                        int tx = TransactionStore.GetTransactionId(id);
                        if (tx == transactionId || BitSetHelper.Get(committingTransactions, tx))
                        {
                            // value comes from this transaction or another committed transaction
                            // take current value instead of committed one
                            value = data.GetCurrentValue();
                        }
                    }

                    if (value is not null)
                    {
                        snapshotKey = key;
                        snapshotValue = value;
                        return;
                    }
                }
            }
        }

        private void FetchUncommitted()
        {
            while (uncommittedCursor.HasNext())
            {
                object key = uncommittedCursor.Next();
                VersionedValue<object>? data = uncommittedCursor.GetValue();
                if (data is not null)
                {
                    long id = data.GetOperationId();
                    if (id != VersionedValue<object>.NoOperationId
                        && transactionId == TransactionStore.GetTransactionId(id))
                    {
                        uncommittedKey = key;
                        uncommittedValue = data.GetCurrentValue();
                        return;
                    }
                }
            }
        }
    }

    /// <summary>
    /// An iterator over a transaction map.
    /// </summary>
    public abstract class TMIterator<KK, VV, X>
    {
        internal readonly int transactionId;

        internal readonly long[] committingTransactions;

        /// <summary>
        /// The cursor over the snapshot.
        /// </summary>
        protected readonly Cursor<object, VersionedValue<object>> cursor;

        private readonly bool forEntries;

        private bool havePeeked;

        private X? peeked;

        internal TMIterator(TransactionMap<KK, VV> transactionMap, object? from, object? to,
            Snapshot<object, VersionedValue<object>> snapshot, bool reverse)
        {
            Transaction transaction = transactionMap.GetTransaction();
            transactionId = transaction.TransactionId;
            // Derived, not passed: an entries iterator always answers pairs, a keys iterator
            // always answers keys. A mismatch between the flag and X used to be a silent
            // reinterpretation of memory; now it cannot be expressed.
            forEntries = typeof(X) == typeof(KeyValuePair<KK, VV>);
            cursor = transactionMap.map.Cursor(snapshot.Root, from, to, reverse);
            committingTransactions = snapshot.CommittingTransactions;
        }

        internal X ToElement(object key, object? value)
        {
            if (forEntries)
            {
                KeyValuePair<KK, VV> pair = new((KK)key!, (VV)value!);
                return Unsafe.As<KeyValuePair<KK, VV>, X>(ref pair);
            }

            return (X)key;
        }

        /// <summary>
        /// Tries to fetch the next element.
        /// </summary>
        /// <returns>true when an element was fetched</returns>
        public abstract bool TryFetchNext(out X value);

        /// <summary>
        /// Whether another element is available.
        /// </summary>
        public bool HasNext()
        {
            if (!havePeeked)
            {
                havePeeked = TryFetchNext(out X value);
                peeked = value;
            }

            return havePeeked;
        }

        /// <summary>
        /// The next element.
        /// </summary>
        public X Next()
        {
            if (!HasNext())
            {
                throw new InvalidOperationException();
            }

            havePeeked = false;
            return peeked!;
        }
    }
}



