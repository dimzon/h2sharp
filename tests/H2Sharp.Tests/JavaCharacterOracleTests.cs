/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the character classes of java.lang.Character.
 *
 * These four predicates decide which characters may be part of a name, which separate tokens, and
 * which are spaces, so a wrong answer anywhere is a statement that cannot be parsed. They are
 * Unicode tables, and .NET's tables are not the JDK's tables: the two count digits differently, a
 * connecting mark is a mark on one side and not on the other, and the whitespace of the two does
 * not agree on the non-breaking space. So the whole of the code space is compared, once, rather
 * than a corpus of interesting characters picked out of it.
 *
 * One code unit per code point carries four answers, packed as bits, and the whole thing travels
 * back the way every other string in this harness does: as UTF-16 code units in hex. A difference
 * is reported as the first code point where the two part company, with both answers spelled out,
 * because "the test failed" without that is a million code points to look at.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]

public sealed class JavaCharacterOracleTests
{
    [JavaOracleFact]
    public void TheCharacterClassesMatchTheJdkForEveryCodePoint()
    {
        List<string> java = OracleRunner.Run(
                [],
                """
                StringBuilder b = new StringBuilder(0x110000);
                for (int cp = 0; cp <= 0x10FFFF; cp++) {
                  if (cp >= 0xD800 && cp <= 0xDFFF) {
                    continue;
                  }
                  int bits = 0;
                  if (java.lang.Character.isJavaIdentifierStart(cp)) {
                    bits |= 1;
                  }
                  if (java.lang.Character.isJavaIdentifierPart(cp)) {
                    bits |= 2;
                  }
                  if (java.lang.Character.isSpaceChar(cp)) {
                    bits |= 4;
                  }
                  if (java.lang.Character.isWhitespace(cp)) {
                    bits |= 8;
                  }
                  b.append((char) ('0' + bits));
                }
                System.out.println(hex(b.toString()));
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        Assert.Single(java);
        Assert.True(java[0] == Packed(), DescribeFirstDifference(java[0], Packed()));
    }

    /// <summary>
    /// The same four answers for every code point, one code unit each.
    /// </summary>
    /// <returns>the packed answers</returns>
    private static string Packed()
    {
        StringBuilder packed = new(0x110000);
        for (int cp = 0; cp <= 0x10FFFF; cp++)
        {
            if (cp >= 0xD800 && cp <= 0xDFFF)
            {
                continue;
            }

            int bits = 0;
            if (JavaCharacter.IsJavaIdentifierStart(cp))
            {
                bits |= 1;
            }

            if (JavaCharacter.IsJavaIdentifierPart(cp))
            {
                bits |= 2;
            }

            if (JavaCharacter.IsSpaceChar(cp))
            {
                bits |= 4;
            }

            if (JavaCharacter.IsWhitespace(cp))
            {
                bits |= 8;
            }

            _ = packed.Append((char)('0' + bits));
        }

        return packed.ToString();
    }

    /// <summary>
    /// Where the two answers first part company, in words rather than as a wall of digits.
    /// </summary>
    private static string DescribeFirstDifference(string java, string csharp)
    {
        int shared = Math.Min(java.Length, csharp.Length);
        for (int i = 0; i < shared; i++)
        {
            if (java[i] != csharp[i])
            {
                return $"U+{i:X4}: jdk {Describe(java[i] - '0')}, port {Describe(csharp[i] - '0')}";
            }
        }

        return $"lengths differ: jdk {java.Length}, port {csharp.Length}";
    }

    private static string Describe(int bits)
        => string.Join(
            ", ",
            new[]
            {
                (bits & 1) == 0 ? null : "identifierStart",
                (bits & 2) == 0 ? null : "identifierPart",
                (bits & 4) == 0 ? null : "spaceChar",
                (bits & 8) == 0 ? null : "whitespace",
            }.Where(static name => name is not null));

    /// <summary>
    /// The code point at a place in a string, for every place of every corpus string, against the
    /// JDK's own <c>String.codePointAt</c>.
    /// </summary>
    /// <remarks>
    /// The corpus is where the answers differ: a plain character, a pair, a leading half with
    /// nothing after it, a leading half followed by an ordinary character, a trailing half on its
    /// own, and an empty string. The port assembles the pair by arithmetic rather than by
    /// <c>char.ConvertToUtf32</c>, so this is what proves the two ways agree.
    /// </remarks>
    [JavaOracleFact]
    public void TheCodePointsMatchTheJdkForEveryPlace()
    {
        List<string> java = OracleRunner.Run(
                Corpus,
                """
                StringBuilder line = new StringBuilder();
                for (int i = 0; i < inputs.length; i++) {
                  String s = inputs[i];
                  line.setLength(0);
                  for (int at = 0; at < s.length(); at++) {
                    line.append((char) s.codePointAt(at));
                  }
                  System.out.println(hex(line.toString()));
                }
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        List<string> csharp = [.. Corpus.Select(PackedCodePoints)];

        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            Assert.True(
                java[i] == csharp[i],
                $"\"{Corpus[i]}\":\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }
    }

    /// <summary>
    /// What the port does where the JDK would throw or is asked for a place past the end.
    /// </summary>
    [Fact]
    public void ThePortAnswersWhereTheJdkHasNothingToAnswer()
    {
        // A leading half with no trailing half after it answers itself, and so does a leading half before
        // an ordinary character and a trailing half on its own.
        Assert.Equal(0xD83D, JavaCharacter.CodePointAt("a\uD83D", 1));
        Assert.Equal(0xD83D, JavaCharacter.CodePointAt("a\uD83Db", 1));
        Assert.Equal(0x1F600, JavaCharacter.CodePointAt("a\U0001F600b", 1));
        Assert.Equal(0xDE00, JavaCharacter.CodePointAt("\U0001F600", 1));
        Assert.Equal('b', JavaCharacter.CodePointAt("a\U0001F600b", 3));
        Assert.Equal('a', JavaCharacter.CodePointAt("a\U0001F600b", 0));

        // A place past the end is refused by the indexer, as it always was: the port never reached
        // past the end itself, where the reference does and answers what its own string holds.
        Assert.Throws<IndexOutOfRangeException>(() => JavaCharacter.CodePointAt("abc", 3));
        Assert.Throws<IndexOutOfRangeException>(() => JavaCharacter.CodePointAt(string.Empty, 0));
    }

    private static readonly List<string> Corpus =
    [
        "abc",
        "a\U0001F600",
        "a\U0001F600b",
        "\U0001F600",
        "\U0001F600\U0001F600",
        "a\uD83D",
        "a\uD83D\U0001F600",
        "\U0001F600abc",
        "имя_столбца",
        "Ω≈ç√",
        "a\uD83Db",
        "\uD83D",
        "",
    ];

    private static string PackedCodePoints(string s)
    {
        StringBuilder b = new(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            _ = b.Append((char)JavaCharacter.CodePointAt(s, i));
        }

        return b.ToString();
    }

    /// <summary>
    /// The case of a character, for every character of the basic multilingual plane.
    /// </summary>
    /// <remarks>
    /// The whole plane rather than a sample, because these two mappings are the same everywhere
    /// except in a dozen places, and a sample has no way of finding those places: they are the
    /// dotless i of the Turkish, the Latin small letter l with a tail, and the Latin capital letters
    /// that were once digraphs.
    /// </remarks>
    [JavaOracleFact]
    public void TheCaseOfACharacterMatchesTheJdkForEveryCharacter()
    {
        List<string> java = OracleRunner.Run(
                [],
                """
                StringBuilder b = new StringBuilder(0x10000);
                for (int c = 0; c <= 0xFFFF; c++) {
                  if (c >= 0xD800 && c <= 0xDFFF) {
                    continue;
                  }
                  b.append((char) Character.toUpperCase((char) c));
                  b.append((char) Character.toLowerCase((char) c));
                }
                System.out.println(hex(b.toString()));
                """,
                extraJavaArgs: ["-Duser.language=en"])
            .Select(Decode)
            .ToList();

        Assert.Single(java);
        Assert.True(java[0] == PackedCase(), DescribeFirstCaseDifference(java[0], PackedCase()));
    }

    /// <summary>
    /// The same two answers for every character, two code units each.
    /// </summary>
    /// <returns>the packed answers</returns>
    private static string PackedCase()
    {
        StringBuilder packed = new(0x10000);
        for (int c = 0; c <= 0xFFFF; c++)
        {
            if (c >= 0xD800 && c <= 0xDFFF)
            {
                continue;
            }

            _ = packed.Append(JavaCharacter.ToUpperCase((char)c)).Append(JavaCharacter.ToLowerCase((char)c));
        }

        return packed.ToString();
    }

    private static string DescribeFirstCaseDifference(string java, string csharp)
    {
        int shared = Math.Min(java.Length, csharp.Length);
        for (int codePoint = 0; codePoint <= 0xFFFF; codePoint++)
        {
            if (codePoint >= 0xD800 && codePoint <= 0xDFFF)
            {
                continue;
            }

            // Every character contributes two code units, and the surrogates none.
            int i = codePoint < 0xD800 ? codePoint * 2 : (codePoint - 0x800) * 2;
            if (i + 1 >= shared)
            {
                break;
            }

            if (java[i] != csharp[i] || java[i + 1] != csharp[i + 1])
            {
                return $"U+{codePoint:X4}: java upper {java[i]:x4} lower {java[i + 1]:x4}, "
                    + $"port upper {csharp[i]:x4} lower {csharp[i + 1]:x4}";
            }
        }

        return $"lengths differ: java {java.Length}, port {csharp.Length}";
    }

    private static string Decode(string hex)
    {
        string only = hex.TrimStart('=');
        StringBuilder b = new(only.Length / 4);
        for (int i = 0; i + 3 < only.Length; i += 4)
        {
            _ = b.Append((char)Convert.ToInt32(only.Substring(i, 4), 16));
        }

        return b.ToString();
    }
}
