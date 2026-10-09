using System;

namespace H2Sharp.Values;

/// <summary>
/// Something that knows its own type.
/// </summary>
/// <remarks>
/// The reference declares it as <c>Typed.getType()</c>. This port spells that <c>GetTypeInfo</c>,
/// because <c>GetType()</c> would land next to <c>object.GetType()</c> and be confused with it, and
/// <see cref="Value"/> already answers the same question under that name.
/// </remarks>
public interface Typed
{
    /// <summary>
    /// The type of this object.
    /// </summary>
    /// <returns>the type</returns>
    TypeInfo GetTypeInfo();
}

/// <summary>
/// A value in the version history, before the versions are taken into account.
/// </summary>
/// <typeparam name="T">the value type</typeparam>
/// <remarks>
/// The base class says everything is committed and has no operation and no entry of its own, which is
/// right for a value that does not live in the store. Subclasses in the versioned store override
/// that, and until those exist this is the honest answer for everything else: the value you hold is
/// the value that is committed.
/// </remarks>
public abstract class VersionedValue<T>
{
    /// <summary>
    /// The entry id that means there is none.
    /// </summary>
    public const long NoEntryId = -1L;

    /// <summary>
    /// The operation id that means there is none.
    /// </summary>
    public const long NoOperationId = 0L;

    /// <summary>
    /// Whether the value is committed.
    /// </summary>
    /// <returns>true for a value outside the version history</returns>
    public virtual bool IsCommitted() => true;

    /// <summary>
    /// The operation that wrote this value.
    /// </summary>
    /// <returns>the operation id</returns>
    public virtual long GetOperationId() => NoOperationId;

    /// <summary>
    /// The entry this value belongs to.
    /// </summary>
    /// <returns>the entry id</returns>
    public virtual long GetEntryId() => NoEntryId;

    /// <summary>
    /// The value as it is now.
    /// </summary>
    /// <returns>this</returns>
    public T GetCurrentValue() => (T)(object)this;

    /// <summary>
    /// The value as it was committed.
    /// </summary>
    /// <returns>this</returns>
    public T GetCommittedValue() => (T)(object)this;
}
