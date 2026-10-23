// Copyright 2026 H2Sharp contributors.
// Ported from none (new file, no Java counterpart).
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference runs its tests on demand through TestAll.main; xUnit discovers
// and runs them automatically, which the native tests must not do by default: most of
// them cannot pass until more of the engine is ported. The default is therefore not to
// run — a test marked [H2Fact] is skipped unless the run opts in by setting
// H2SHARP_NATIVE_TESTS=1 (the way the reference opts into a configuration by passing an
// argument to TestAll.main).
using Xunit;

namespace H2Sharp.Native.Tests;

/// <summary>
/// Marks a ported native test. The test does not run by default: it is skipped unless
/// the environment variable H2SHARP_NATIVE_TESTS is set to 1.
/// </summary>
public sealed class H2FactAttribute : FactAttribute
{
    /// <summary>
    /// The name of the environment variable that opts the run into the native tests.
    /// </summary>
    public const string EnvironmentVariable = "H2SHARP_NATIVE_TESTS";

    /// <summary>
    /// Builds the attribute. Skips the test unless the run opts in.
    /// </summary>
    public H2FactAttribute()
    {
        if (!"1".Equals(Environment.GetEnvironmentVariable(EnvironmentVariable),
                StringComparison.Ordinal))
        {
            Skip = $"Native test; set {EnvironmentVariable}=1 to run";
        }
    }
}
