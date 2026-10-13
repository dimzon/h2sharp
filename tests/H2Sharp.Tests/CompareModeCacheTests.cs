/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * GetInstance hands the same mode back for the same arguments, as the reference does.
 *
 * The cache is one static slot shared by the whole process, so this runs in the
 * non-parallel collection: a GetInstance with other arguments from a parallel suite would
 * evict the slot between the two calls and fail the identity check for scheduling reasons
 * rather than code ones.
 */
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(MeasurementCollection.Name)]
public sealed class CompareModeCacheTests
{
    [Fact]
    public void SameArgumentsReturnTheSameMode()
    {
        Assert.Same(CompareMode.GetInstance(null, 0), CompareMode.GetInstance(null, 0));
    }

    [Fact]
    public void DifferentArgumentsReturnDifferentModes()
    {
        Assert.NotSame(CompareMode.GetInstance(null, 0), CompareMode.GetInstance("CHARSET_UTF-8", 0));
    }
}
