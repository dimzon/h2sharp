/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The tests that ask the reference JVM a question all share one collection, so they run one
/// after another instead of side by side.
/// </summary>
/// <remarks>
/// Every oracle test compiles a program with <c>javac</c> and runs it with <c>java</c>, in a
/// directory of its own. xUnit runs test classes in parallel by default, so without this a
/// full suite run started a dozen JVMs at once and the tests that measure a duration or that
/// read a freshly written file would fail at random — which is worse than slow, because a
/// failure that cannot be reproduced is a failure nobody believes.
/// <para>
/// The cost is that the oracle tests are serial among themselves. They were serial anyway in
/// practice, because the JVM dominates their runtime. What this definition deliberately does
/// <em>not</em> do is stop the collection from running alongside the others: a test that needs
/// no JVM should not wait for one. Tests inside a collection never run in parallel with each
/// other regardless, so the guarantee is the same and the suite does not pay for the rest.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class OracleCollection
{
    /// <summary>
    /// The name the oracle test classes put in their <c>Collection</c> attribute.
    /// </summary>
    public const string Name = "reference jvm";
}
