/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
namespace H2Sharp.Data.Extensions;

/// <summary>
/// Optimizer metadata for a host-provided aggregate.
/// </summary>
/// <remarks>
/// These are declarations made by the aggregate author, not hints the engine can verify.
/// A wrong flag produces wrong results; the defaults are deliberately conservative.
/// </remarks>
public sealed class AggregateOptions
{
    /// <summary>The default options: no optimizer assumptions.</summary>
    public static AggregateOptions Default { get; } = new();

    /// <summary>
    /// The aggregate returns the same result for the same arguments.
    /// </summary>
    /// <remarks>
    /// When true, the expression can answer the deterministic visitor. It is still not an
    /// optimizable aggregate: the engine has no fast path for host-provided aggregates.
    /// </remarks>
    public bool Deterministic { get; init; }

    /// <summary>
    /// The result does not depend on duplicate input rows.
    /// </summary>
    /// <remarks>
    /// When true, <c>DISTINCT</c> does not need to collect and deduplicate rows; the
    /// aggregate is called for every row. The aggregate author is responsible for the
    /// result being the same as it would be with duplicate inputs removed.
    /// </remarks>
    public bool InvariantToDuplicates { get; init; }

    /// <summary>
    /// The result does not depend on the input row order.
    /// </summary>
    /// <remarks>
    /// Reserved for ordered aggregate execution. The current engine feeds an ordered window
    /// in its order and does not expose a separate ordered aggregate path, so this flag has
    /// no execution effect yet.
    /// </remarks>
    public bool InvariantToOrder { get; init; }
}
