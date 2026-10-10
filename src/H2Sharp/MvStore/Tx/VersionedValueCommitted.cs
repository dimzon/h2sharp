using System.Diagnostics;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// A committed versioned value: what the entry holds, plus the entry id when it has one.
/// </summary>
internal class VersionedValueCommitted<T> : VersionedValue<T>
{
    /// <summary>
    /// The current value.
    /// </summary>
    internal readonly T Value;

    private readonly long entryId;

    protected VersionedValueCommitted(T value, long entryId)
    {
        Value = value;
        this.entryId = entryId;
    }

    /// <summary>
    /// Either casts to a versioned value, or wraps in a committed one.
    /// </summary>
    internal static VersionedValue<X>? GetInstance<X>(X? value)
    {
        Debug.Assert(value is not null);
        return GetInstance(value, VersionedValue<X>.NoEntryId);
    }

    internal static VersionedValue<X>? GetInstance<X>(X? value, long entryId)
    {
        return entryId == VersionedValue<X>.NoEntryId && (value is null || value is VersionedValue<X>)
                ? (VersionedValue<X>?)(object?)value
                : new VersionedValueCommitted<X>(value!, entryId);
    }

    public override long GetEntryId() => entryId;

    public override T GetCurrentValue() => Value;

    public override T GetCommittedValue() => Value;

    public override string ToString() => Value?.ToString() ?? "";
}
