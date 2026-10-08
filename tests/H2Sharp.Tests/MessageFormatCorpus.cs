/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new test corpus generator for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * java.text.MessageFormat's quoting rules are famously easy to get subtly wrong, and
 * H2 feeds its SQL error messages through them. This enumerates every pattern over the
 * alphabet {', '{', '}', '0'} up to a given length and asks the reference JVM what
 * each one produces, so that ErrorMessages.Format can be validated against the full
 * behaviour rather than against hand-picked examples.
 */
using System;
using System.Collections.Generic;

namespace H2Sharp.Tests;

public static class MessageFormatCorpus
{
    private static readonly char[] Alphabet = ['\'', '{', '}', '0'];

    /// <summary>
    /// Builds every non-empty pattern over <see cref="Alphabet"/> whose length is
    /// between 1 and <paramref name="maxLength"/>, shortest first.
    /// </summary>
    /// <remarks>
    /// The empty pattern is excluded because its result cannot be distinguished from a
    /// dropped output line by the oracle harness.
    /// </remarks>
    /// <param name="maxLength">the longest pattern to generate</param>
    /// <returns>the patterns</returns>
    internal static List<string> Build(int maxLength)
    {
        List<string> patterns = new();

        for (int length = 1; length <= maxLength; length++)
        {
            int combinations = 1;
            for (int i = 0; i < length; i++)
            {
                combinations *= Alphabet.Length;
            }

            for (int value = 0; value < combinations; value++)
            {
                char[] chars = new char[length];
                int remaining = value;
                for (int i = length - 1; i >= 0; i--)
                {
                    chars[i] = Alphabet[remaining % Alphabet.Length];
                    remaining /= Alphabet.Length;
                }

                patterns.Add(new string(chars));
            }
        }

        return patterns;
    }

    /// <summary>
    /// The arguments passed to <c>MessageFormat.format</c> in the corpus tests.
    /// Index 0 is a letter so that it is distinguishable from the literal '0' in the
    /// alphabet; index 1 and 2 are numbers.
    /// </summary>
    internal static object?[] Arguments { get; } = ["A", 1, 2];
}
