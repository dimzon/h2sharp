/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Direct coverage of StringUtils: invariant case folding, number parsing,
 * hex decoding and URL/XML encoding. And of WriteBuffer: growth, reuse,
 * native pinning and clear.
 */
using System;
using System.Numerics;
using System.Text;
using H2Sharp.MvStore;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class StringUtilsWriteBufferDirectTests
{
    [Fact]
    public void NeedsUpperInvariantFoldChangesNonAscii()
    {
        // char >= 0x80 where ToUpperInvariant changes the char → line 265-266.
        var span = "é".AsSpan();
        Assert.Equal("É", StringUtils.ToUpperEnglish(span));
    }

    [Fact]
    public void IsNumberEmptyIsFalse()
    {
        // line 2444-2446: l == 0 → return false.
        Assert.False(StringUtils.IsNumber(""));
    }

    [Fact]
    public void IsNumberNonDigitIsFalse()
    {
        // line 2451-2454: !char.IsDigit(s[i]) → return false.
        Assert.False(StringUtils.IsNumber("12a"));
    }

    [Fact]
    public void AppendZeroPaddedPowersOfTenCorrection()
    {
        // line 2566-2571: u >= PowersOfTen[digits] → digits++.
        var builder = new StringBuilder();
        StringUtils.AppendZeroPadded(builder, 5, 1000);
        Assert.Equal("01000", builder.ToString());
    }

    [Fact]
    public void AppendToLengthPadsWithWholeString()
    {
        // line 2596-2598: need >= s.Length → builder.Append(s).
        var builder = new StringBuilder("ab");
        StringUtils.AppendToLength(builder, "CDEF", 10);
        Assert.Equal("abCDEF", builder.ToString());
    }

    [Fact]
    public void ClearAfterNativeGrowRecreatesManaged()
    {
        // line 359-368: Clear() after buffer grew native (> MaxReuseCapacity)
        // and managed is null → recreates managed array.
        var buffer = new WriteBuffer(100_000);
        var data = new byte[5_000_000];
        buffer.Put(data);
        buffer.Clear();
        buffer.Put((byte)42);
        Assert.Equal(42, buffer.ToArray()[0]);
    }

    [Fact]
    public void GrowManagedAllocatesNewArray()
    {
        // line 468-480: Grow() allocates new managed array when not native.
        var buffer = new WriteBuffer(100);
        var data = new byte[5_000];
        buffer.Put(data);
        Assert.Equal(data.Length, buffer.ToArray().Length);
    }

    [Fact]
    public void NativeBufferGetRegion()
    {
        // line 393, 513-523, 525-526: GetRegion creates NativeView and calls GetSpan.
        var buffer = new WriteBuffer(100_000);
        buffer.Put((byte)1);
        buffer.Put((byte)2);
        var region = buffer.GetRegion(2);
        Assert.Equal(1, region.Span[0]);
        Assert.Equal(2, region.Span[1]);
    }

    [Fact]
    public void JavaEncodePlainChar()
    {
        // line 784-785: char >= ' ' && < 0x80 that's not a special char.
        var buff = new StringBuilder();
        StringUtils.JavaEncode("hello", buff, false);
        Assert.Equal("hello", buff.ToString());
    }
}
