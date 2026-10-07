/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new tests for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// A fact that is skipped when the reference JVM is unavailable, so that a plain
/// <c>dotnet test</c> run does not fail merely because the optional cross-language
/// oracle is not set up.
/// </summary>
public sealed class JavaOracleFactAttribute : FactAttribute
{
    public JavaOracleFactAttribute()
    {
        if (!JavaOracle.IsAvailable)
        {
            Skip = JavaOracle.SkipReason;
        }
    }
}

/// <summary>
/// Theory counterpart of <see cref="JavaOracleFactAttribute"/>.
/// </summary>
public sealed class JavaOracleTheoryAttribute : TheoryAttribute
{
    public JavaOracleTheoryAttribute()
    {
        if (!JavaOracle.IsAvailable)
        {
            Skip = JavaOracle.SkipReason;
        }
    }
}