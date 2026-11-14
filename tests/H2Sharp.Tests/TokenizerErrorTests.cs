/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 204: the tokenizer paths SQL rarely reaches — malformed numeric, hex and quoted
 * literals, the national-character prefix, string pieces written across non-ASCII gaps,
 * and the identifier lengths that fall through their keyword tables.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using H2Sharp;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class TokenizerErrorTests
{
    private static List<Token> Tokenize(string sql, CastDataProvider? provider = null)
        => Tokenizer.TokenizeForTest(sql, false, new BitSet(), provider ?? new TestCastDataProvider());

    private static H2SharpException Refuses(string sql)
        => Assert.Throws<H2SharpException>(() => Tokenize(sql));

    [Theory]
    [InlineData("SELECT X'1G'", Api.ErrorCode.HexStringWrong1)]
    [InlineData("SELECT 1_", Api.ErrorCode.SyntaxError2)]
    [InlineData("SELECT 1_A", Api.ErrorCode.SyntaxError2)]
    [InlineData("SELECT 1e", Api.ErrorCode.SyntaxError2)]
    [InlineData("SELECT 1E+", Api.ErrorCode.SyntaxError2)]
    [InlineData("SELECT 1e1_", Api.ErrorCode.SyntaxError2)]
    [InlineData("SELECT 'abc", Api.ErrorCode.SyntaxError1)]
    [InlineData("SELECT \"abc", Api.ErrorCode.SyntaxError1)]
    [InlineData("SELECT 'a''b", Api.ErrorCode.SyntaxError1)]
    [InlineData("SELECT \"a\"\"b", Api.ErrorCode.SyntaxError1)]
    public void MalformedLiteralsAreRefused(string sql, int expected)
        => Assert.Equal(expected, Refuses(sql).H2ErrorCode);

    /// <summary>
    /// The SQL Server spelling of the binary string: 0x… bytes, and a stray digit after them
    /// names the literal, as the reference does.
    /// </summary>
    [Fact]
    public void TheSqlServerBinaryStringRefusesStrayDigits()
    {
        List<Token> tokens = Tokenize("SELECT 0x0102", new ModeProvider("MSSQLServer"));
        Assert.Contains(tokens, token => token.TokenType == Token.Literal);
        Assert.Equal(Api.ErrorCode.HexStringWrong1, RefusesMssql("SELECT 0x0102G").H2ErrorCode);
    }

    private static H2SharpException RefusesMssql(string sql)
        => Assert.Throws<H2SharpException>(() => Tokenize(sql, new ModeProvider("MSSQLServer")));

    /// <summary>
    /// The national character string prefix: N'abc' is the same text as 'abc'.
    /// </summary>
    [Fact]
    public void TheNationalCharacterPrefixIsRead()
    {
        List<Token> tokens = Tokenize("SELECT N'abc'");
        Token literal = Assert.Single(tokens, token => token.TokenType == Token.Literal);
        Assert.Equal("abc", literal.Text);
    }

    /// <summary>
    /// A literal written in pieces may carry its gap outside ASCII: the ideographic space is
    /// whitespace, and a comment written there still joins the pieces. Whitespace and comments
    /// in the plain statement take the same reader.
    /// </summary>
    [Fact]
    public void NonAsciiWhitespaceAndCommentsSeparateTokens()
    {
        CastDataProvider provider = new TestCastDataProvider();
        Assert.Equal(2, Tokenize("SELECT\u30001").Count - 1);
        Assert.Equal("ab", Literal("SELECT 'a'\u3000'b'", provider));
        Assert.Equal("ab", Literal("SELECT 'a'\u3000/* c */'b'", provider));
        Assert.Equal("ab", Literal("SELECT 'a'\u3000// c\n'b'", provider));
        Assert.Equal(1, Tokenize("SELECT\u3000/* c */ 1")
            .Single(token => token.TokenType == Token.Literal).GetValue(provider)!.GetInt());
        Assert.Equal("A", Tokenize("\u3000A\u3000")
            .Single(token => token.AsIdentifier == "A").Text);
    }

    private static string Literal(string sql, CastDataProvider provider)
        => Tokenize(sql).Single(token => token.TokenType == Token.Literal)
            .GetValue(provider)!.GetString()!;

    /// <summary>
    /// An identifier of the length its keyword table checks but of another text is an
    /// identifier: the keyword table falls through.
    /// </summary>
    [Fact]
    public void KeywordTableLengthsFallThroughForOtherNames()
    {
        Assert.Equal("CURRENT_ABCDEF", Tokenize("SELECT CURRENT_ABCDEF")
            .Single(token => token.AsIdentifier == "CURRENT_ABCDEF").Text);
        Assert.Equal("CURRENT_ABCDEFGHI", Tokenize("SELECT CURRENT_ABCDEFGHI")
            .Single(token => token.AsIdentifier == "CURRENT_ABCDEFGHI").Text);
        Assert.Equal("ABCDEFGHIJKLMN", Tokenize("SELECT abcdefghijklmn")
            .Single(token => token.AsIdentifier == "ABCDEFGHIJKLMN").Text);
    }

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
