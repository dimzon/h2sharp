namespace H2Sharp.MvStore.Tx;

/// <summary>
/// How isolated a transaction is from concurrent ones.
/// </summary>
/// <remarks>
/// A minimal stand-in for the engine's isolation level: the store layer only asks whether
/// non-repeatable reads are allowed and which snapshot path a map takes. When the engine is
/// ported and brings the real type, this one goes away.
/// </remarks>
public enum IsolationLevel
{
    /// <summary>
    /// Sees everything, including uncommitted changes.
    /// </summary>
    ReadUncommitted,

    /// <summary>
    /// Sees committed changes, re-read on every statement.
    /// </summary>
    ReadCommitted,

    /// <summary>
    /// Sees a statement snapshot plus its own changes.
    /// </summary>
    RepeatableRead,

    /// <summary>
    /// Sees the transaction snapshot plus its own changes.
    /// </summary>
    Snapshot,

    /// <summary>
    /// The strongest snapshot isolation here.
    /// </summary>
    Serializable,
}

/// <summary>
/// What the store layer asks of an isolation level.
/// </summary>
public static class IsolationLevels
{
    /// <summary>
    /// Whether a transaction at this level may see different committed data on re-read:
    /// read-uncommitted and read-committed may.
    /// </summary>
    public static bool AllowNonRepeatableRead(this IsolationLevel level) => level <= IsolationLevel.ReadCommitted;
}
