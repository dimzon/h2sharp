/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The tests that measure something all share one collection, and that collection refuses to
/// run alongside anything else.
/// </summary>
/// <remarks>
/// A test that counts allocated bytes or times a method is reading a shared resource: the
/// processor's time and, on .NET, the collector. Both are shared with everything else running
/// at the same moment, so these tests fail at random whenever the machine is busy — which is
/// what happened while the reference-JVM tests were spawning dozens of processes. Putting them
/// in a collection that disables parallelization is the fix: they then have the machine to
/// themselves, and their numbers mean something.
/// <para>
/// This is why the oracle tests are a separate collection that does allow other work beside it:
/// the two sets of tests each need quiet, and putting every test in one collection would buy
/// the first set its quiet at the cost of doubling the suite for everything else.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MeasurementCollection
{
    /// <summary>
    /// The name the measuring test classes put in their <c>Collection</c> attribute.
    /// </summary>
    public const string Name = "measurements";
}
