/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the token vocabulary.
 *
 * The table of token texts is the parser's contract in the least visible way there is: the
 * numbers are never questioned and never written down twice, so a keyword that moves in the
 * reference moves here silently and every statement that uses it breaks. Reading it back out of
 * the reference's own class, rather than out of its source, means the test fails when the two
 * disagree and not only when someone re-reads the Java.
 *
 * The numbers are compared as well as the texts, because a text is meaningless without its index.
 * The kinds of token themselves are exercised through what they print, which for a keyword is its
 * text and for a literal is the trace of its value, the latter already covered by the value tests.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using H2Sharp.Command;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class TokenOracleTests
{
    /// <summary>
    /// The reference's own table, read out of the class rather than out of the source, because
    /// the table is what the reference looks words up in.
    /// </summary>
    private const string ReadTable =
        """
        java.lang.reflect.Field table = Class.forName("org.h2.command.Token").getDeclaredField("TOKENS");
        table.setAccessible(true);
        String[] tokens = (String[]) table.get(null);
        """;

    /// <summary>
    /// Words that the reference reads as a keyword, each in a spelling of its own, so that a
    /// comparison covers both the number and the text as the reference sees them.
    /// </summary>
    private static readonly string[] Spellings =
    [
        "SELECT", "select", "FROM", "from", "WHERE", "where", "CREATE", "create", "TABLE", "table",
        "ORDER", "GROUP", "IS", "is", "NOT", "not", "NULL", "null", "CASE", "case", "WHEN", "when",
        "AS", "as", "JOIN", "join", "LIKE", "like", "PRIMARY", "KEY", "CHECK", "check", "DISTINCT",
        "VALUES", "values", "ARRAY", "INTERSECTS", "USER", "user", "_ROWID_",
    ];

    [JavaOracleFact]
    public void TokenTexts_MatchReference()
    {
        List<string> java = OracleRunner.Run(
                [],
                ReadTable
                + """
                for (int i = 0; i < tokens.length; i++) {
                    System.out.println(i + " " + (tokens[i] == null ? "<none>" : tokens[i]));
                }
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(line => line.Trim())
            .ToList();

        List<string> csharp = new();
        for (int i = 0; i < Token.Tokens.Length; i++)
        {
            csharp.Add(i + " " + (Token.Tokens[i] ?? "<none>"));
        }

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.Equal(java[i], csharp[i]);
        }
    }

    [JavaOracleFact]
    public void WordLookup_GivesTheSameKind()
    {
        // The keyword table is looked up by the tokenizer once per word of every statement, so
        // the lookup is compared for both settings of the two flags: a word that is a keyword
        // only where the grammar allows it is a name everywhere else, and that is the whole
        // meaning of the third argument.
        List<string> java = OracleRunner.Run(
                Spellings,
                """
                for (int i = 0; i < inputs.length; i++) {
                    System.out.println(
                            org.h2.util.ParserUtil.getTokenType(inputs[i], true, false) + " "
                            + org.h2.util.ParserUtil.getTokenType(inputs[i], true, true) + " "
                            + org.h2.util.ParserUtil.getTokenType(inputs[i], false, true));
                }
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(line => line.Trim())
            .ToList();

        Assert.Equal(Spellings.Length, java.Count);
        for (int i = 0; i < Spellings.Length; i++)
        {
            string[] parts = java[i].Split(' ');
            string word = Spellings[i];

            Assert.Equal(
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                ParserUtil.GetTokenType(word, true, false));
            Assert.Equal(
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                ParserUtil.GetTokenType(word, true, true));
            Assert.Equal(
                int.Parse(parts[2], CultureInfo.InvariantCulture),
                ParserUtil.GetTokenType(word, false, true));
        }
    }

    [Fact]
    public void EveryKindWithText_PrintsThatText()
    {
        // A keyword token prints the text of its kind. The table itself is compared with the
        // reference above; this says the two are actually joined up, so a row that was filled
        // with the wrong text, or moved one place along, prints something else here.
        for (int type = 0; type < Token.Tokens.Length; type++)
        {
            string? text = Token.Tokens[type];
            if (text is null)
            {
                Assert.Null(new Token.KeywordToken(0, type).AsIdentifier);
                continue;
            }

            Token token = new Token.KeywordToken(0, type);
            Assert.Equal(text, token.ToString());
            Assert.Equal(text, token.AsIdentifier);
            Assert.Equal(type, token.TokenType);
        }
    }

    [Fact]
    public void ANameThatIsAKeywordStaysTheName()
    {
        // A keyword is also allowed as a name, and a name keeps the case it was written with,
        // which is the whole reason a table may be called "order".
        Token token = new Token.KeywordOrIdentifierToken(0, ParserUtil.Order, "Order");
        Assert.Equal(ParserUtil.Order, token.TokenType);
        Assert.Equal("Order", token.ToString());
        Assert.Equal("Order", token.AsIdentifier);
    }

    [Fact]
    public void QuotedIdentifier_PrintsAsQuoted()
    {
        // A quoted name prints between double quotes, which is what makes a name that is a
        // keyword survive being read back.
        Token quoted = new Token.IdentifierToken(0, "select", true, false);
        Assert.Equal("\"select\"", quoted.ToString());
        Assert.Equal(ParserUtil.Identifier, quoted.TokenType);
        Assert.True(quoted.IsQuoted);

        Token clone = quoted.Clone();
        Assert.NotSame(quoted, clone);
        Assert.Equal(quoted.ToString(), clone.ToString());

        Token bare = new Token.IdentifierToken(0, "Select", false, false);
        Assert.Equal("Select", bare.ToString());
        Assert.False(bare.IsQuoted);
        Assert.Equal(ParserUtil.Identifier, bare.TokenType);
    }

    [Fact]
    public void ParameterToken_PrintsItsNumberOnlyWhenItIsNotTheFirst()
    {
        Assert.Equal("?", new Token.ParameterToken(0, 0).ToString());
        Assert.Equal("?1", new Token.ParameterToken(0, 1).ToString());
        Assert.Equal("?7", new Token.ParameterToken(0, 7).ToString());
        Assert.Equal("?", new Token.ParameterToken(0, 3).AsIdentifier);
        Assert.Equal(3, new Token.ParameterToken(0, 3).Index);
    }

    [Fact]
    public void ALiteralToken_PrintsTheTraceOfItsValue()
    {
        // A literal prints as the value it stands for, which is the one place a token prints
        // something other than itself.
        Token number = new Token.IntegerToken(0, 42);
        Assert.Equal(Token.Literal, number.TokenType);
        Assert.Equal("42", number.ToString());

        Token big = new Token.BigintToken(0, 9223372036854775807L);
        Assert.Equal("9223372036854775807", big.ToString());

        Token text = new Token.CharacterStringToken(0, "it's", false);
        Assert.Equal("'it''s'", text.ToString());
        Assert.Same(text.GetValue(null), text.GetValue(null));

        Token bytes = new Token.BinaryStringToken(0, [0x41, 0x42]);
        Assert.Equal("X'4142'", bytes.ToString());
    }

    [Fact]
    public void EndOfInputToken_IsTheEndOfInput()
    {
        Token end = new Token.EndOfInputToken(7);
        Assert.Equal(Token.EndOfInput, end.TokenType);
        Assert.Null(end.AsIdentifier);
        Assert.False(end.IsQuoted);
        Assert.Equal(7, end.Start);
        end.Start = 9;
        end.SubtractFromStart(4);
        Assert.Equal(5, end.Start);
    }

    [Fact]
    public void UnicodeEscapes_AreConvertedOnceAndOnlyWhereTheyAreNeeded()
    {
        // An escape is the escape character and four hex digits, with no letter in between,
        // and the escape character is whatever the statement said it was.
        Token escaped = new Token.IdentifierToken(0, "a\\0041b", false, true);
        Assert.True(escaped.NeedsUnicodeConversion);
        escaped.ConvertUnicode('\\');
        Assert.Equal("aAb", escaped.ToString());
        Assert.False(escaped.NeedsUnicodeConversion);
        Assert.Throws<Exception>(() => escaped.ConvertUnicode('\\'));

        Token plain = new Token.IdentifierToken(0, "abc", false, false);
        Assert.False(plain.NeedsUnicodeConversion);
        Assert.Throws<Exception>(() => plain.ConvertUnicode('\\'));

        Token hashed = new Token.CharacterStringToken(0, "#0041", true);
        Assert.True(hashed.NeedsUnicodeConversion);
        hashed.ConvertUnicode('#');
        Assert.Equal("'A'", hashed.ToString());

        // Two escape characters in a row are one of them, which is how a backslash is written
        // at all.
        Token doubled = new Token.IdentifierToken(0, "a\\\\b", false, true);
        doubled.ConvertUnicode('\\');
        Assert.Equal("a\\b", doubled.ToString());
    }
}
