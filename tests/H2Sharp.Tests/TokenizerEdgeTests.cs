/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 105: tokenizer branches SQL rarely reaches — stopping at the first closing
 * parenthesis, the Oracle '#' name, dollar-quoted strings, hexadecimal binary strings
 * in SQL Server mode, non-ASCII whitespace and identifiers of the lengths that have
 * their own keyword tables.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TokenizerEdgeTests
{
    private static List<Token> Tokenize(string sql, bool stopOnCloseParen = false, CastDataProvider? provider = null)
        => Tokenizer.TokenizeForTest(sql, stopOnCloseParen, new BitSet(), provider ?? new TestCastDataProvider());

    [Fact]
    public void StoppingAtTheCloseParenDropsTheRest()
    {
        List<Token> all = Tokenize("SELECT (1 + 2) + 3");
        List<Token> stopped = Tokenize("SELECT (1 + 2) + 3", stopOnCloseParen: true);

        Assert.True(stopped.Count < all.Count);
        Assert.Equal(Token.CloseParen, stopped[^2].TokenType);
        Assert.Equal(Token.EndOfInput, stopped[^1].TokenType);
    }

    [Fact]
    public void OracleReadsPoundNames()
    {
        List<Token> tokens = Tokenize("SELECT #A FROM T", provider: new ModeProvider("Oracle"));

        Assert.Contains(tokens, token => token.AsIdentifier == "#A");
        // Without the mode flag the character is a syntax error.
        _ = Assert.ThrowsAny<H2SharpException>(() => Tokenize("SELECT #A FROM T"));
    }

    [Fact]
    public void DollarQuotedStringsAndTheirEnd()
    {
        Assert.Contains(Tokenize("SELECT $$abc$$"), token => token.Text == "abc");
        _ = Assert.ThrowsAny<H2SharpException>(() => Tokenize("SELECT $$abc"));
    }

    [Fact]
    public void SqlServerReadsHexadecimalBinaryStrings()
    {
        List<Token> tokens = Tokenize("SELECT 0x0102", provider: new ModeProvider("MSSQLServer"));
        Assert.Contains(tokens, token => token.TokenType == Token.Literal);
        _ = Assert.ThrowsAny<H2SharpException>(
            () => Tokenize("SELECT 0x0", provider: new ModeProvider("MSSQLServer")));
    }

    [Fact]
    public void NonAsciiWhitespaceAndLongIdentifiersAreTokens()
    {
        // A no-break space is Java's space character, not ASCII whitespace.
        Assert.NotEmpty(Tokenize("SELECT 1\u00A0+ 2"));

        // These lengths have their own keyword tables; a name that is not the keyword of
        // that length comes back as an identifier.
        List<Token> names = Tokenize(
            "SELECT ABCDEFGHIJKLMN, ABCDEFGHIJKLMNO, ABCDEFGHIJKLMNOPQ, "
            + "CURRENT_ABCDEF, CURRENT_ABCDEFG, CURRENT_ABCDEFGH");
        List<string?> identifiers = names.Where(token => token.IsIdentifier).Select(token => token.AsIdentifier).ToList();
        Assert.Contains("ABCDEFGHIJKLMN", identifiers);
        Assert.Contains("ABCDEFGHIJKLMNO", identifiers);
        Assert.Contains("ABCDEFGHIJKLMNOPQ", identifiers);
        Assert.Contains("CURRENT_ABCDEF", identifiers);
        Assert.Contains("CURRENT_ABCDEFG", identifiers);
        Assert.Contains("CURRENT_ABCDEFGH", identifiers);
    }

    [Fact]
    public void InvalidTokensAreRefused()
    {
        _ = Assert.ThrowsAny<H2SharpException>(() => Tokenize("SELECT 1 ! 2"));
        _ = Assert.ThrowsAny<H2SharpException>(() => Tokenize("SELECT 1 & 2"));
        _ = Assert.ThrowsAny<H2SharpException>(() => Tokenize("SELECT 1 /* unterminated"));
    }

    /// <summary>A cast provider of a chosen mode, otherwise the regular test provider.</summary>
    private sealed class ModeProvider : CastDataProvider
    {
        private readonly TestCastDataProvider inner = new();

        private readonly Mode mode;

        internal ModeProvider(string name) => mode = Mode.GetInstance(name)!;

        public ValueTimestampTimeZone CurrentTimestamp() => inner.CurrentTimestamp();

        public TimeZoneProvider CurrentTimeZone() => inner.CurrentTimeZone();

        public bool ZeroBasedEnums() => inner.ZeroBasedEnums();

        public Mode GetMode() => mode;
    }
}
