using System;
using System.Collections.Generic;
using System.Diagnostics;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// Rewrites one transaction's uncommitted entries as committed during post-commit processing,
/// sweeping whole leaves: every entry carrying this transaction's operation id loses it.
/// </summary>
internal sealed class CommitDecisionMaker<V> : DecisionMaker<VersionedValue<V>>
{
    private readonly int transactionId;
    private long undoKey;
    private Decision? decision;

    private readonly HashSet<int> entryIds = new();
    private readonly int[] pageEntryIds;
    private int entryIdsCount;

    internal CommitDecisionMaker(Transaction transaction, int maxKeysPerPage)
    {
        transactionId = transaction.GetId();
        pageEntryIds = new int[maxKeysPerPage];
    }

    internal void SetUndoKey(long undoKey)
    {
        this.undoKey = undoKey;
        Reset();
    }

    public override void OnPageReplaced()
    {
        for (int i = 0; i < entryIdsCount; i++)
        {
            _ = entryIds.Add(pageEntryIds[i]);
        }

        Reset();
    }

    internal bool HaveSeenEntry(int entryId) => entryIds.Contains(entryId);

    public override CursorPos<K, VersionedValue<V>>? Decide<K>(
        CursorPos<K, VersionedValue<V>>? tip, K key, VersionedValue<V>? providedValue)
    {
        Page<K, VersionedValue<V>> p = tip!.page;
        Debug.Assert(p.IsLeaf());
        bool update = false;
        long toRemove = 0L;
        for (int src = 0; src < p.GetKeyCount(); src++)
        {
            VersionedValue<V>? value = p.GetValue(src);
            long operationId = value!.GetOperationId();
            if (operationId != VersionedValue<V>.NoOperationId
                && TransactionStore.GetTransactionId(operationId) == transactionId)
            {
                long entryId = value.GetEntryId();
                Debug.Assert(entryId != VersionedValue<V>.NoEntryId);
                Debug.Assert(!entryIds.Contains((int)entryId));
                pageEntryIds[entryIdsCount++] = (int)entryId;

                V? currentValue = value.GetCurrentValue();
                if (currentValue is null)
                {
                    toRemove |= 1L << src;
                }
                else
                {
                    update = true;
                }
            }
        }

        if (toRemove != 0L)
        {
            p = p.Remove(toRemove);
            if (p.GetKeyCount() == 0)
            {
                CursorPos<K, VersionedValue<V>>? pos = tip.parent;
                if (pos is not null)
                {
                    int keyCount;
                    int index;
                    do
                    {
                        p = pos.page;
                        index = pos.index;
                        pos = pos.parent;
                        keyCount = p.GetKeyCount();
                        // condition below should always be false, but older
                        // versions (up to 1.4.197) may create
                        // single-childed (with no keys) internal nodes,
                        // which we skip here
                    } while (keyCount == 0 && pos is not null);

                    if (keyCount <= 1)
                    {
                        if (keyCount == 1)
                        {
                            Debug.Assert(index <= 1);
                            p = p.GetChildPage(1 - index).Copy();
                        }
                        else
                        {
                            // if root happens to be such single-childed
                            // (with no keys) internal node, then just
                            // replace it with empty leaf
                            p = Page<K, VersionedValue<V>>.CreateEmptyLeaf(p.map);
                        }

                        return new CursorPos<K, VersionedValue<V>>(p, 0, pos);
                    }

                    p = p.Copy();
                    p.Remove(index);
                }

                return new CursorPos<K, VersionedValue<V>>(p, 0, pos);
            }
        }
        else if (update)
        {
            p = p.Copy();
        }
        else
        {
            return tip;
        }

        if (update)
        {
            for (int i = 0; i < p.GetKeyCount(); i++)
            {
                VersionedValue<V>? value = p.GetValue(i);
                long operationId = value!.GetOperationId();
                if (operationId != VersionedValue<V>.NoOperationId &&
                        TransactionStore.GetTransactionId(operationId) == transactionId)
                {
                    V? currentValue = value.GetCurrentValue();
                    Debug.Assert(currentValue is not null);
                    _ = p.SetValue(i, VersionedValueCommitted<V>.GetInstance(currentValue)!);
                }
            }
        }

        return new CursorPos<K, VersionedValue<V>>(p, p.GetMemory(), tip.parent);
    }

    public override Decision Decide(VersionedValue<V>? existingValue, VersionedValue<V>? providedValue)
    {
        Debug.Assert(decision is null);
        if (existingValue is null ||
            // map entry was treated as already committed, and then
            // it has been removed by another transaction (committed and closed by now)
            existingValue.GetOperationId() != undoKey)
        {
            // this is not a final undo log entry for this key,
            // or map entry was treated as already committed and then
            // overwritten by another transaction
            // see TxDecisionMaker.Decide()
            decision = Decision.Abort;
        }
        else if (existingValue.GetCurrentValue() is null)
        {
            decision = Decision.Remove;
        }
        else
        {
            decision = Decision.Put;
        }

        return decision!.Value;
    }

    public override T SelectValue<T>(T existingValue, T providedValue)
    {
        Debug.Assert(decision == Decision.Put);
        Debug.Assert(existingValue is not null);
        return (T)(object)VersionedValueCommitted<V>.GetInstance(
            ((VersionedValue<V>)(object)existingValue!).GetCurrentValue())!;
    }

    public override void Reset()
    {
        decision = null;
        entryIdsCount = 0;
    }

    public override string ToString() => "commit " + TransactionStore.GetTransactionId(undoKey);
}
