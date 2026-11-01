// Copyright 2026 H2Sharp contributors.
// Ported from none (new file, no Java counterpart).
// Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
//
// Modified: the reference runs its tests on demand through TestAll.main; xUnit discovers
// and runs them automatically. A ported test whose feature is not there yet is skipped
// with an explicit reason passed to the attribute, instead of an environment gate — the
// whole native suite runs in the ordinary `dotnet test <solution>` without variables.
using Xunit;

namespace H2Sharp.Native.Tests;

/// <summary>
/// Marks a ported native test. It runs as a normal fact unless a skip reason is given,
/// which records a known port gap at the test itself.
/// </summary>
public sealed class H2FactAttribute : FactAttribute
{
    /// <summary>
    /// Builds the attribute.
    /// </summary>
    /// <param name="skip">the reason to skip the test, or null to run it</param>
    public H2FactAttribute(string? skip = null)
    {
        if (skip is not null)
        {
            Skip = skip;
        }
    }
}
