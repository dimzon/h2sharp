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
