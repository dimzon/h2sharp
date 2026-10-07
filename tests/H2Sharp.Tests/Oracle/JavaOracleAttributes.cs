/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new tests for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// A fact that runs the reference JVM and compares against it.
/// <para>
/// By default it is skipped when the oracle is unavailable, so that a plain
/// <c>dotnet test</c> run does not fail merely because the optional cross-language
/// oracle is not set up. That default is exactly what makes a green run meaningless for
/// compatibility, so the conformance job in CI sets <c>H2SHARP_REQUIRE_ORACLE</c> and
/// then an unavailable oracle throws instead of skipping. See
/// <see cref="JavaOracle.Required"/>.
/// </para>
/// </summary>
public sealed class JavaOracleFactAttribute : FactAttribute
{
    public JavaOracleFactAttribute()
    {
        if (JavaOracle.RequiredFailureReason is { } failure)
        {
            throw new InvalidOperationException(failure);
        }

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
        if (JavaOracle.RequiredFailureReason is { } failure)
        {
            throw new InvalidOperationException(failure);
        }

        if (!JavaOracle.IsAvailable)
        {
            Skip = JavaOracle.SkipReason;
        }
    }
}
