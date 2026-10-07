/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new tests for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * SysProperties is a static class whose fields are initialised once per process,
 * so these tests assert the *default* values (i.e. with no h2.* environment
 * variables set) and separately test the parsing helpers that feed them.
 */
using System;
using H2Sharp.Engine;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class SysPropertiesTests
{
    [Fact]
    public void Defaults_MatchJavaValues()
    {
        Assert.True(SysProperties.Check);
        Assert.Equal(32_000, SysProperties.CollatorCacheSize);
        Assert.Equal(250, SysProperties.DelayWrongPasswordMin);
        Assert.Equal(4000, SysProperties.DelayWrongPasswordMax);
        Assert.False(SysProperties.LobCloseBetweenReads);
        Assert.Equal(16, SysProperties.MaxFileRetry);
        Assert.True(SysProperties.MaxTraceDataLength >= 65535);
        Assert.True(SysProperties.ObjectCache);
        Assert.Equal(4096, SysProperties.ObjectCacheMaxPerElementSize);
        Assert.Equal(1024, SysProperties.ObjectCacheSize);
        Assert.Equal("UTF-8", SysProperties.PgDefaultClientEncoding);
        Assert.Equal("h2.temp", SysProperties.PrefixTempFile);
        Assert.False(SysProperties.ForceAutoCommitOffOnCommit);
        Assert.Equal(30L, SysProperties.SplitFileSizeShift);
        Assert.False(SysProperties.TraceIo);
    }

    [Fact]
    public void ObjectCacheSize_IsAPowerOfTwo()
    {
        int size = SysProperties.ObjectCacheSize;
        Assert.True(size > 0);
        // A power of two has exactly one bit set.
        Assert.Equal(
            expected: 1,
            actual: System.Numerics.BitOperations.PopCount((uint)size));
    }

    [Fact]
    public void BaseDir_RoundTripsThroughTheEnvironment()
    {
        string? original = SysProperties.GetBaseDir();
        try
        {
            SysProperties.SetBaseDir("/tmp/h2sharp-base");
            Assert.Equal("/tmp/h2sharp-base/", SysProperties.GetBaseDir());

            // A trailing slash must not be doubled.
            SysProperties.SetBaseDir("/tmp/h2sharp-base/");
            Assert.Equal("/tmp/h2sharp-base/", SysProperties.GetBaseDir());
        }
        finally
        {
            Environment.SetEnvironmentVariable("h2.baseDir", original);
        }
    }

    [Fact]
    public void UserHome_IsNotNull()
        => Assert.NotNull(SysProperties.UserHome);

    [Theory]
    [InlineData("42", 42)]
    [InlineData("-42", -42)]
    [InlineData("0x2A", 42)]
    [InlineData("0X2a", 42)]
    [InlineData("#2A", 42)]
    [InlineData("052", 42)]
    [InlineData("0", 0)]
    [InlineData("", null)]
    [InlineData(" 42", null)]
    [InlineData("42x", null)]
    [InlineData("99999999999999999999", null)]
    public void TryParseInt_FollowsIntegerDecodeSyntax(string input, int? expected)
    {
        bool parsed = Utils.TryParseInt(input, out int value);
        if (expected is null)
        {
            Assert.False(parsed);
            return;
        }

        Assert.True(parsed);
        Assert.Equal(expected.Value, value);
    }

    [Fact]
    public void GetProperty_ReadsEnvironmentVariables()
    {
        const string Key = "H2SHARP_SYSPROP_TEST";
        try
        {
            Assert.Equal("fallback", Utils.GetPropertyOrDefault(Key, "fallback"));

            Environment.SetEnvironmentVariable(Key, "0x20");
            Assert.Equal(32, Utils.GetProperty(Key, 0));
            Assert.Equal(32L, Utils.GetProperty(Key, 0L));
            Assert.Equal("0x20", Utils.GetPropertyOrDefault(Key, "fallback"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Key, null);
        }
    }
}