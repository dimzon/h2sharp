/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 273: the token/value helpers stay internal so their Java-specific rules can be pinned
 * directly: radix digits, underscore removal and the BigDecimal-shaped numeric test.
 */
using System;
using System.Numerics;
using H2Sharp.Command;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ParserValueHelperTests
{
    [Fact]
    public void ParseIntegerHonoursRadixAndSignOnly()
    {
        Assert.Equal(new BigInteger(255), Tokenizer.ParseInteger("FF", 16));
        Assert.Equal(new BigInteger(-255), Tokenizer.ParseInteger("-ff", 16));
        Assert.Equal(new BigInteger(255), Tokenizer.ParseInteger("+FF", 16));
        Assert.Equal(new BigInteger(10), Tokenizer.ParseInteger("10", 10));
        Assert.Equal(new BigInteger(35), Tokenizer.ParseInteger("z", 36));
        Assert.Throws<FormatException>(() => Tokenizer.ParseInteger("12", 2));
        Assert.Throws<FormatException>(() => Tokenizer.ParseInteger("+", 16));
    }

    [Fact]
    public void UnderscoresAreRemovedOnlyWhenAsked()
    {
        Assert.Equal("123", Tokenizer.ReadAndRemoveUnderscores("x123y", 1, 4, false));
        Assert.Equal("123", Tokenizer.ReadAndRemoveUnderscores("x1_2_3y", 1, 6, true));
        Assert.Equal(string.Empty, Tokenizer.ReadAndRemoveUnderscores("x__y", 1, 3, true));
    }

    [Fact]
    public void JavaNumberTestCoversExponentFormsThePlatformParserCannot()
    {
        Assert.True(ValueStringBase.IsJavaNumber("1e-400"));
        Assert.True(ValueStringBase.IsJavaNumber("-1.5E+3"));
        Assert.True(ValueStringBase.IsJavaNumber("+12"));
        Assert.True(ValueStringBase.IsJavaNumber(".5"));
        Assert.False(ValueStringBase.IsJavaNumber("1e"));
        Assert.False(ValueStringBase.IsJavaNumber("1e+"));
        Assert.False(ValueStringBase.IsJavaNumber("+"));
        Assert.False(ValueStringBase.IsJavaNumber("x"));
        Assert.False(ValueStringBase.IsJavaNumber("NaN"));
        Assert.False(ValueStringBase.IsJavaNumber("Infinity"));
    }
}
