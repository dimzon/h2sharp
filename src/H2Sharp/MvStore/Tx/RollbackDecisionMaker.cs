using System.Diagnostics;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// Replays one undo log record during rollback: restores the old value, then drops the record.
/// </summary>
internal sealed class RollbackDecisionMaker : DecisionMaker<Record<object, object>>
{
    private readonly TransactionStore store;
    private readonly long transactionId;
    private readonly long toLogId;
    private readonly TransactionStore.RollbackListener listener;
    private Decision? decision;

    internal RollbackDecisionMaker(TransactionStore store, long transactionId, long toLogId,
                            TransactionStore.RollbackListener listener)
    {
        this.store = store;
        this.transactionId = transactionId;
        this.toLogId = toLogId;
        this.listener = listener;
    }

    public override Decision Decide(Record<object, object>? existingValue, Record<object, object>? providedValue)
    {
        Debug.Assert(decision is null);
        if (existingValue is null)
        {
            // normally existingValue will always be there except of db initialization
            // where some undo log entry was captured on disk but actual map entry was not
            decision = Decision.Abort;
        }
        else
        {
            VersionedValue<object>? valueToRestore = existingValue.OldValue;
            long operationId;
            if (valueToRestore is null ||
                    (operationId = valueToRestore.GetOperationId()) == VersionedValue<object>.NoOperationId ||
                    TransactionStore.GetTransactionId(operationId) == transactionId
                            && TransactionStore.GetLogId(operationId) < toLogId)
            {
                int mapId = existingValue.MapId;
                MVMap<object, VersionedValue<object>>? map = store.OpenMap(mapId);
                if (map is not null && !map.IsClosed())
                {
                    object? key = existingValue.Key;
                    VersionedValue<object>? previousValue = map.Operate(key!,
                        valueToRestore, DecisionMaker<VersionedValue<object>>.DefaultDecision());
                    listener.OnRollback(map, key, previousValue, valueToRestore);
                }
            }

            decision = Decision.Remove;
        }

        return decision!.Value;
    }

    public override void Reset() => decision = null;

    public override string ToString() => "rollback-" + transactionId;
}
