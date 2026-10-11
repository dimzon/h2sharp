/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * How the three ways of writing a parameter come out of the tokenizer.
 *
 * The finding that matters for the parser: a parameter written **by name** is two tokens, an
 * `At` and the name as an identifier; a parameter written as a **bare question mark** is one
 * token that is then numbered by the order it was read; and `?2` is one token that carries the
 * position that was written. A statement cannot mix the first two ways, and the tokenizer
 * refuses that — so the parser gets either all names or all positions, and has to bind the two
 * tokens of a name itself.
 *
 * That is pinned here rather than discovered later: a parser that expects one token for a name
 * would read "@Age" as an at-sign followed by a column named AGE, and the statement would run
 * against the column instead of the parameter. Nothing in a test of the tokenizer alone would
 * have said so.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using H2Sharp.Command;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ParameterTokenTests
{
    [Fact]
    public void AParameterWrittenByNameIsAnAtAndTheName()
    {
        List<Token> tokens = Tokenize("SELECT * FROM T WHERE ID = @Id AND AGE = @Age");

        // The two names, as the identifiers that follow the two At tokens.
        Assert.Equal(
            ["ID", "AGE"],
            tokens
                .Select((token, i) => (token, i))
                .Where(pair => pair.token.TokenType == Token.At)
                .Select(pair => tokens[pair.i + 1].AsIdentifier)
                .ToList());
    }

    [Fact]
    public void ABareQuestionMarkIsNumberedByOrder()
    {
        List<Token.ParameterToken> parameters = Parameters("SELECT * FROM T WHERE A = ? AND B = ?");

        Assert.Equal([1, 2], parameters.Select(p => p.Index));
    }

    [Fact]
    public void AParameterWrittenByPositionKeepsThatPosition()
    {
        // "?2" is the parameter at position two, so the number that was written is the index.
        Token.ParameterToken only = Assert.Single(Parameters("SELECT * FROM T WHERE ID = ?2"));

        Assert.Equal(2, only.Index);
    }

    [Fact]
    public void ANameIsFoldedToUpperCaseLikeAnyOtherIdentifier()
    {
        // The name of a parameter is an identifier and is folded like one. It is worth knowing,
        // because a caller that binds "@age" must find the parameter the statement wrote as
        // "@AGE" — the fold happens once, here, and not again where the name is bound.
        Assert.Equal(
            ["AGE"],
            Tokenize("SELECT * FROM T WHERE A = @age")
                .Select((token, i) => (token, i))
                .Where(pair => pair.token.TokenType == Token.At)
                .Select(pair => Tokenize("SELECT * FROM T WHERE A = @age")[pair.i + 1].AsIdentifier)
                .ToList());
    }

    private static List<Token> Tokenize(string sql)
    {
        BitArray seen = new(8);
        return Tokenizer.TokenizeForTest(sql, false, seen, new TestCastDataProvider());
    }

    private static List<Token.ParameterToken> Parameters(string sql)
        => [.. Tokenize(sql).OfType<Token.ParameterToken>()];
}
