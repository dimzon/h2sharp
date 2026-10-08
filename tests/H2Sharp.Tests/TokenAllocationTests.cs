/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Pins the allocation behaviour of the token layer.
 *
 * The tokenizer looks up a word for every word of every statement and prints a token for every
 * error message and every rewritten statement, so both are measured here rather than left to be
 * discovered in a profile. The lookup is the interesting one: it folds the case of the word and
 * then reads a frozen dictionary, and the fold is cached, so a statement parsed twice allocates
 * nothing for its keywords at all.
 */
using System;
using System.Globalization;
using H2Sharp.Command;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

[Collection(MeasurementCollection.Name)]

public sealed class TokenAllocationTests
{
    private static long Measure(Action action)
    {
        for (int i = 0; i < 3; i++)
        {
            action();
        }

        long smallest = long.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocated < smallest)
            {
                smallest = allocated;
            }
        }

        return smallest;
    }

    private static long MeasurePerCall(Action action) => Measure(() =>
    {
        for (int i = 0; i < 1000; i++)
        {
            action();
        }
    }) / 1000;

    [Fact]
    public void LookingUpAKeywordInUpperCaseCostsNothing()
    {
        // The whole lookup: the length filter, the case fold and the frozen dictionary read.
        long bytes = Measure(() =>
        {
            ParserUtil.GetTokenType("SELECT", true, false);
            ParserUtil.GetTokenType("CURRENT_TIMESTAMP", true, true);
            ParserUtil.GetTokenType("ILIKE", true, false);
        });

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void LookingUpAKeywordInAnyCaseCostsNothingAfterTheFirstTime()
    {
        // Folding the case of a mixed case word is the one part that allocates, and the result is
        // cached, so the second statement with the same word pays nothing.
        ParserUtil.GetTokenType("current_schema", true, false);

        long bytes = Measure(() => ParserUtil.GetTokenType("current_schema", true, false));

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void PrintingAKeywordCostsNothing()
    {
        // A keyword prints its own text, which is the entry of the table and not a new string.
        Token token = new Token.KeywordToken(0, ParserUtil.Select);

        Assert.Equal(0, Measure(() => Assert.Equal("SELECT", token.ToString())));
        Assert.Equal(0, Measure(() => Assert.Equal("SELECT", token.AsIdentifier)));
    }

    [Fact]
    public void PrintingANameCostsNothingAndPrintingAQuotedNameCostsAString()
    {
        // A bare name prints as the string it already is. A quoted one is a new string, because
        // the quotes have to go around it; the reference builds one too, so this is the cost of
        // the operation rather than of the port.
        Token bare = new Token.IdentifierToken(0, "Customers", false, false);
        Token quoted = new Token.IdentifierToken(0, "Customers", true, false);

        Assert.Equal(0, Measure(() => Assert.Equal("Customers", bare.ToString())));

        long perQuoted = MeasurePerCall(() => Assert.Equal("\"Customers\"", quoted.ToString()));
        Assert.InRange(perQuoted, 32, 160);
    }

    [Fact]
    public void AParameterTokenPrintsWithoutAllocatingForTheFirst()
    {
        // The first question mark is written without a number, which is a constant, and every
        // other one writes its number into the builder the reference writes it into.
        Token first = new Token.ParameterToken(0, 0);

        Assert.Equal(0, Measure(() => Assert.Equal("?", first.ToString())));
    }

    [Fact]
    public void CloningATokenCostsOneObject()
    {
        Token token = new Token.KeywordOrIdentifierToken(0, ParserUtil.Order, "Order");

        long perClone = MeasurePerCall(() => token.Clone());

        Assert.InRange(perClone, 24, 64);
    }

    [Fact]
    public void TheValueOfALiteralIsBuiltOnceAndKept()
    {
        Token number = new Token.IntegerToken(0, 7);

        Assert.Same(number.GetValue(null), number.GetValue(null));
        Assert.Equal(0, Measure(() => number.GetValue(null)));
        Assert.Equal(
            7.ToString(CultureInfo.InvariantCulture),
            ((H2Sharp.Values.Value)number.GetValue(null)!).GetTraceSql());
    }
}
