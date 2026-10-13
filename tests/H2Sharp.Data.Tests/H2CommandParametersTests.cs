/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The parameter collection is created once per command, not once per access: Dapper touches
 * Parameters on every execution, and a fresh wrapper each time is garbage per statement.
 */
using Xunit;

namespace H2Sharp.Data.Tests;

public sealed class H2CommandParametersTests
{
    [Fact]
    public void ParametersAreStableAcrossAccesses()
    {
        using var cmd = new H2Command();
        Assert.Same(cmd.Parameters, cmd.Parameters);
    }
}
