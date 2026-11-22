/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The off-heap memory gates share one collection that runs after every parallel collection
/// has finished.
/// </summary>
/// <remarks>
/// The gates read global native-allocation counters (<c>WriteBuffer.LiveAllocations</c>,
/// <c>NativeLobBlock.LiveAllocations</c>). While any other collection runs beside them, a
/// sample describes the whole process, not the gate's own work, and a baseline taken at the
/// start of a test says nothing about what arrives afterwards. Running alone turns the counts
/// back into statements about the gate's own scenario.
/// <para>
/// Isolation removes only the concurrent noise, not what earlier tests left behind: the
/// counter is process-global, so a gate that needs a level must take it relative to its own
/// baseline. The chunk-images gate does exactly that.
/// </para>
/// <para>
/// Like the oracle collection, this costs serial execution — three tests that measure
/// allocations over seconds of file work. Unlike it, the reason is correctness, not duration:
/// no amount of serial scheduling inside a parallel phase would separate these counters from
/// unrelated work.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OffHeapMemoryGateCollection
{
    /// <summary>
    /// The name the memory-gate test classes put in their <c>Collection</c> attribute.
    /// </summary>
    public const string Name = "off-heap memory gates";
}
