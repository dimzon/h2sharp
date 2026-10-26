/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * How a question mark parameter and an at-variable come out of the tokenizer.
 *
 * A parameter written as a **bare question mark** is one token that is then numbered by the
 * order it was read; `?2` is one token that carries the position that was written. `@Age` is
 * two tokens, an `At` and the name as an identifier, and it is an H2 session variable, not a
 * parameter: the ADO.NET layer's named parameters are written `$name` and are rewritten before
 * the text reaches the engine.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using H2Sharp.Command;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ParameterTokenTests
{
    [Fact]
    public void AVariableIsAnAtAndTheName()
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
    public void AVariableNameIsFoldedToUpperCaseLikeAnyOtherIdentifier()
    {
        // The name of a variable is an identifier and is folded like one. It is worth knowing,
        // because the parser binds "@age" and "@AGE" to the same variable — the fold happens
        // once, here, and not again where the variable is looked up.
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
        BitSet seen = new();
        return Tokenizer.TokenizeForTest(sql, false, seen, new TestCastDataProvider());
    }

    private static List<Token.ParameterToken> Parameters(string sql)
        => [.. Tokenize(sql).OfType<Token.ParameterToken>()];
}
