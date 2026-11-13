/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Util.Utils coverage: IndexOf with empty pattern, CopyBytes edge lengths,
 * CloneByteArray(null), ParseBoolean matrix, JavaHashCode(null),
 * single-thread executor shutdown/flush/submit.
 */
using H2Sharp.Util;
using System.Threading.Tasks;
using Xunit;

namespace H2Sharp.Tests;

public sealed class UtilsCoverageTests
{
    [Fact]
    public void IndexOfEmptyPatternReturnsStart()
    {
        Assert.Equal(1, Utils.IndexOf(new byte[] { 1, 2, 3 }, Array.Empty<byte>(), 1));
    }

    [Fact]
    public void CopyBytesZeroLengthReturnsEmpty()
    {
        byte[] result = Utils.CopyBytes(new byte[] { 1, 2 }, 0);
        Assert.Empty(result);
    }

    [Fact]
    public void CopyBytesSameLengthReturnsCopy()
    {
        byte[] result = Utils.CopyBytes(new byte[] { 1, 2 }, 2);
        Assert.Equal(2, result.Length);
        Assert.Equal(1, result[0]);
        Assert.Equal(2, result[1]);
    }

    [Fact]
    public void CloneByteArrayNullReturnsNull()
    {
        Assert.Null(Utils.CloneByteArray(null));
    }

    [Fact]
    public void ParseBooleanValidValuesAnswer()
    {
        Assert.True(Utils.ParseBoolean("1", false, false));
        Assert.False(Utils.ParseBoolean("0", true, false));
        Assert.False(Utils.ParseBoolean("no", true, false));
        Assert.True(Utils.ParseBoolean("yes", false, false));
        Assert.True(Utils.ParseBoolean("true", false, false));
        Assert.False(Utils.ParseBoolean("false", true, false));
    }

    [Fact]
    public void ParseBooleanUnknownWithDefaultReturnsDefault()
    {
        Assert.True(Utils.ParseBoolean("maybe", true, false));
    }

    [Fact]
    public void ParseBooleanUnknownWithThrowThrows()
    {
        Assert.Throws<ArgumentException>(() => Utils.ParseBoolean("maybe", false, true));
    }

    [Fact]
    public void JavaHashCodeNullIsZero()
    {
        Assert.Equal(0, Utils.JavaHashCode(null));
    }

    [Fact]
    public void FlushExecutorAfterShutdownDoesNotThrow()
    {
        var ex = Utils.CreateSingleThreadExecutor("t");
        ex.Shutdown();
        Utils.FlushExecutor(ex);
    }

    [Fact]
    public void ShutdownExecutorNullDoesNotThrow()
    {
        Utils.ShutdownExecutor(null);
    }

    [Fact]
    public async Task SubmitAfterShutdownThrows()
    {
        var ex = Utils.CreateSingleThreadExecutor("t");
        ex.Shutdown();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ex.Submit(() => { }));
    }
}
