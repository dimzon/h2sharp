using System.Diagnostics;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// A versioned value written by an open transaction: the current (uncommitted) value plus the
/// committed one underneath.
/// </summary>
internal sealed class VersionedValueUncommitted<T> : VersionedValueCommitted<T>
{
    private readonly long operationId;
    private readonly T committedValue;

    private VersionedValueUncommitted(long operationId, T value, T committedValue, long entryId)
        : base(value, entryId)
    {
        Debug.Assert(operationId != 0);
        this.operationId = operationId;
        this.committedValue = committedValue;
    }

    /// <summary>
    /// A new uncommitted versioned value.
    /// </summary>
    internal static VersionedValue<X> GetInstance<X>(long operationId, X? value, X? committedValue, long entryId)
        => new VersionedValueUncommitted<X>(operationId, value!, committedValue!, entryId);

    public override bool IsCommitted() => false;

    public override long GetOperationId() => operationId;

    public override T GetCommittedValue() => committedValue;

    public override string ToString() => base.ToString() +
            " " + TransactionStore.GetTransactionId(operationId) + "/" +
            TransactionStore.GetLogId(operationId) + "/" + GetEntryId() + " " + committedValue;
}
