using System;
using System.Diagnostics;
using H2Sharp.MvStore.Type;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// Decides single-entry updates of a transactional map: log the old value, then put.
/// An entry owned by another open transaction aborts (the caller waits and retries);
/// a leftover of an unclean shutdown is rolled forward.
/// </summary>
internal class TxDecisionMaker<K, V> : DecisionMaker<VersionedValue<object>>
{
    private readonly int mapId;

    /// <summary>
    /// The entry's key.
    /// </summary>
    protected K Key;

    /// <summary>
    /// The offered value, or null for a removal. Kept as an object because a default value
    /// (0 for numbers) is a real value, not a removal.
    /// </summary>
    private object? value;

    private readonly Transaction transaction;

    /// <summary>
    /// Id for the undo log entry created for this modification
    /// </summary>
    private long undoKey = VersionedValue<object>.NoEntryId;

    private long lastOperationId;

    private Transaction? blockingTransaction;

    private Decision? decision;

    private object? lastValue;

    internal TxDecisionMaker(int mapId, Transaction transaction)
    {
        this.mapId = mapId;
        this.transaction = transaction;
        Key = default!;
    }

    internal void Initialize(K key, object? value)
    {
        Key = key;
        this.value = value;
        decision = null;
        Reset();
    }

    public override Decision Decide(VersionedValue<object>? existingValue, VersionedValue<object>? providedValue)
    {
        Debug.Assert(decision is null);
        long id;
        int blockingId;
        // if map does not have that entry yet
        if (existingValue is null ||
                // or entry is a committed one
                (id = existingValue.GetOperationId()) == VersionedValue<object>.NoOperationId ||
                // or it came from the same transaction
                IsThisTransaction(blockingId = TransactionStore.GetTransactionId(id)))
        {
            LogAndDecideToPut(existingValue, existingValue?.GetCommittedValue());
        }
        else if (IsCommitted(blockingId))
        {
            // Condition above means that entry belongs to a committing transaction.
            // We assume that we are looking at the final value for this transaction,
            // and if it's not the case, then it will fail later,
            // because a tree root has definitely been changed.
            object? currentValue = existingValue.GetCurrentValue();
            LogAndDecideToPut(currentValue is null ? null : VersionedValueCommitted<object>.GetInstance(currentValue),
                                currentValue);
        }
        else if (GetBlockingTransaction() is not null)
        {
            // this entry comes from a different transaction, and this
            // transaction is not committed yet
            // should wait on blockingTransaction that was determined earlier
            lastValue = existingValue.GetCurrentValue();
            decision = Decision.Abort;
        }
        else if (IsRepeatedOperation(id))
        {
            // There is no transaction with that id, and we've tried it just
            // before, but map root has not changed (which must be the case if
            // we just missed a closed transaction), therefore we came back here
            // again.
            // Now we assume it's a leftover after unclean shutdown (map update
            // was written but not undo log), and will effectively roll it back
            // (just assume committed value and overwrite).
            object? committedValue = existingValue.GetCommittedValue();
            LogAndDecideToPut(committedValue is null ? null : VersionedValueCommitted<object>.GetInstance(committedValue),
                                committedValue);
        }
        else
        {
            // transaction has been committed/rolled back and is closed by now, so
            // we can retry immediately and either that entry become committed
            // or we'll hit case above
            decision = Decision.Repeat;
        }

        return decision!.Value;
    }

    public override void Reset()
    {
        if (decision != Decision.Repeat)
        {
            lastOperationId = VersionedValue<object>.NoOperationId;
            if (decision == Decision.Put)
            {
                // positive decision has been made already and undo record created,
                // but map was updated afterward and undo record deletion required
                transaction.LogUndo();
                undoKey = VersionedValue<object>.NoEntryId;
            }
        }

        blockingTransaction = null;
        decision = null;
        lastValue = default;
    }

    public override T SelectValue<T>(T existingValue, T providedValue)
    {
        // always returns the new uncommitted value (ignores both arguments);
        // the method type is pinned to object: inference would take it from V instead
        return (T)(object)VersionedValueUncommitted<object>.GetInstance<object>(undoKey,
            GetNewValue((VersionedValue<object>?)(object?)existingValue),
            lastValue,
            TransactionStore.GetEntryId((VersionedValue<object>?)(object?)existingValue, undoKey))!;
    }

    /// <summary>
    /// The new value. This implementation always returns the offered one.
    /// </summary>
    protected virtual object? GetNewValue(VersionedValue<object>? existingValue) => value;

    /// <summary>
    /// Logs the previous value and records a put decision with the last known committed value.
    /// </summary>
    protected virtual Decision LogAndDecideToPut(VersionedValue<object>? valueToLog, object? lastValue)
    {
        undoKey = transaction.Log(mapId, Key, valueToLog);
        this.lastValue = lastValue;
        return SetDecision(Decision.Put);
    }

    internal Decision DecideToAbort(object? lastValue)
    {
        this.lastValue = lastValue;
        return SetDecision(Decision.Abort);
    }

    internal bool AllowNonRepeatableRead() => transaction.AllowNonRepeatableRead();

    internal Decision? GetDecision() => decision;

    internal Transaction? GetBlockingTransaction() => blockingTransaction;

    internal V? GetLastValue() => TransactionStore.Unbox<V>(lastValue);

    /// <summary>
    /// Whether the id belongs to the transaction we act within.
    /// </summary>
    internal bool IsThisTransaction(int transactionId) => transactionId == transaction.TransactionId;

    /// <summary>
    /// Whether the id is a logically committed transaction. When it is pending, the blocking
    /// transaction (if any) is remembered for the caller to wait on.
    /// </summary>
    internal bool IsCommitted(int transactionId)
    {
        Transaction? blockingTx;
        bool result;
        TransactionStore store = transaction.Store;
        do
        {
            blockingTx = store.GetTransaction(transactionId);
            result = store.CommittingTransactions.Get(transactionId);
        } while (!ReferenceEquals(blockingTx, store.GetTransaction(transactionId)));

        if (!result)
        {
            blockingTransaction = blockingTx;
        }

        return result;
    }

    /// <summary>
    /// Remembers the operation id, answering whether it is the same as last time: guards
    /// against looping forever on an uncommitted leftover entry.
    /// </summary>
    internal bool IsRepeatedOperation(long id)
    {
        if (id == lastOperationId)
        {
            return true;
        }

        lastOperationId = id;
        return false;
    }

    /// <summary>
    /// Records the decision made, answering it.
    /// </summary>
    internal Decision SetDecision(Decision decision)
    {
        this.decision = decision;
        return decision;
    }

    public override string ToString() => "txdm " + transaction.TransactionId;

    /// <summary>
    /// Puts only when nothing is there (visible or in the snapshot).
    /// </summary>
    internal sealed class PutIfAbsentDecisionMaker : TxDecisionMaker<K, V>
    {
        private readonly Func<K, V?> oldValueSupplier;

        internal PutIfAbsentDecisionMaker(int mapId, Transaction transaction, Func<K, V?> oldValueSupplier)
            : base(mapId, transaction)
        {
            this.oldValueSupplier = oldValueSupplier;
        }

        public override Decision Decide(VersionedValue<object>? existingValue, VersionedValue<object>? providedValue)
        {
            Debug.Assert(GetDecision() is null);
            int blockingId;
            // if map does not have that entry yet
            if (existingValue is null)
            {
                V? snapshotValue = GetValueInSnapshot();
                if (snapshotValue is not null)
                {
                    // value exists in a snapshot but not in current map, therefore
                    // it was removed and committed by another transaction
                    return DecideToAbort(snapshotValue);
                }

                return LogAndDecideToPut(null, default);
            }

            long id = existingValue.GetOperationId();
            if (id == VersionedValue<object>.NoOperationId ||
                    IsThisTransaction(blockingId = TransactionStore.GetTransactionId(id)))
            {
                if (existingValue.GetCurrentValue() is not null)
                {
                    return DecideToAbort(TransactionStore.Unbox<V>(existingValue.GetCurrentValue()));
                }

                if (id == VersionedValue<object>.NoOperationId)
                {
                    V? snapshotValue = GetValueInSnapshot();
                    if (snapshotValue is not null)
                    {
                        return DecideToAbort(snapshotValue);
                    }
                }

                return LogAndDecideToPut(existingValue,
                    TransactionStore.Unbox<V>(existingValue.GetCommittedValue()));
            }
            else if (IsCommitted(blockingId))
            {
                // entry belongs to a committing transaction
                // and therefore will be committed soon
                if (existingValue.GetCurrentValue() is not null)
                {
                    return DecideToAbort(TransactionStore.Unbox<V>(existingValue.GetCurrentValue()));
                }

                // even if that commit will result in entry removal
                // current operation should fail within repeatable read transaction
                // if initial snapshot carries some value
                V? snapshotValue = GetValueInSnapshot();
                if (snapshotValue is not null)
                {
                    return DecideToAbort(snapshotValue);
                }

                return LogAndDecideToPut(null, default);
            }
            else if (GetBlockingTransaction() is not null)
            {
                // this entry comes from a different transaction, and this
                // transaction is not committed yet
                // should wait on blockingTransaction that was determined
                // earlier and then try again
                return DecideToAbort(TransactionStore.Unbox<V>(existingValue.GetCurrentValue()));
            }
            else if (IsRepeatedOperation(id))
            {
                // There is no transaction with that id, and we've tried it
                // just before, but map root has not changed (which must be
                // the case if we just missed a closed transaction),
                // therefore we came back here again.
                // Now we assume it's a leftover after unclean shutdown (map
                // update was written but not undo log), and will
                // effectively roll it back (just assume committed value and
                // overwrite).
                object? committedValue = existingValue.GetCommittedValue();
                if (committedValue is not null)
                {
                    return DecideToAbort(TransactionStore.Unbox<V>(committedValue));
                }

                return LogAndDecideToPut(null, default);
            }
            else
            {
                // transaction has been committed/rolled back and is closed
                // by now, so we can retry immediately and either that entry
                // become committed or we'll hit case above
                return SetDecision(Decision.Repeat);
            }
        }

        private V? GetValueInSnapshot() => AllowNonRepeatableRead() ? default : oldValueSupplier(Key);
    }

    /// <summary>
    /// Locks the entry without changing it: a missing entry is removed again.
    /// </summary>
    internal class LockDecisionMaker : TxDecisionMaker<K, V>
    {
        internal LockDecisionMaker(int mapId, Transaction transaction)
            : base(mapId, transaction)
        {
        }

        public override Decision Decide(VersionedValue<object>? existingValue, VersionedValue<object>? providedValue)
        {
            Decision decision = base.Decide(existingValue, providedValue);
            if (existingValue is null)
            {
                Debug.Assert(decision == Decision.Put);
                decision = SetDecision(Decision.Remove);
            }

            return decision;
        }

        protected override object? GetNewValue(VersionedValue<object>? existingValue)
            => existingValue is null ? null : existingValue.GetCurrentValue();
    }

    /// <summary>
    /// A lock that also fails on write-write conflicts against the snapshot.
    /// </summary>
    internal sealed class RepeatableReadLockDecisionMaker : LockDecisionMaker
    {
        private readonly DataType<VersionedValue<object>> valueType;

        private readonly Func<K, V?> snapshotValueSupplier;

        internal RepeatableReadLockDecisionMaker(int mapId, Transaction transaction,
                DataType<VersionedValue<object>> valueType, Func<K, V?> snapshotValueSupplier)
            : base(mapId, transaction)
        {
            this.valueType = valueType;
            this.snapshotValueSupplier = snapshotValueSupplier;
        }

        protected override Decision LogAndDecideToPut(VersionedValue<object>? valueToLog, object? value)
        {
            V? snapshotValue = snapshotValueSupplier(Key);
            if (snapshotValue is not null && (valueToLog is null
                    || valueType.Compare(VersionedValueCommitted<object>.GetInstance<object>((object?)snapshotValue),
                        valueToLog) != 0))
            {
                throw DataUtils.NewMvStoreException(DataUtils.ErrorTransactionsDeadlock, "");
            }

            return base.LogAndDecideToPut(valueToLog, value);
        }
    }
}
