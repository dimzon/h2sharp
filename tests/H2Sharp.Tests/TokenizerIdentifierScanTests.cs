/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The derived ASCII identifier-part set and the scan that runs on it.
 *
 * FindIdentifierEnd answers per character in the reference and by one vectorized sweep plus
 * one per-character check in the port. The sweep's set and the mask it is checked against are
 * derived from JavaCharacter at startup, and these tests hold both ends of that derivation:
 * the set must be IsJavaIdentifierPart character for character on ASCII — the ignorable
 * controls Java lets into a name are what a hand-written "A-Za-z0-9_$" would lose — and the
 * sweep must answer exactly what the per-character walk of the same rules answers, on the
 * shapes where the two could disagree.
 */
using System.Collections.Generic;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Pins the derived ASCII identifier-part set and the vectorized identifier scan.
/// </summary>
public sealed class TokenizerIdentifierScanTests
{
    [Fact]
    public void IdentifierPartSetMatchesJavaCharacter()
    {
        // The derivation is the guarantee, and this is the guarantee restated: for every ASCII
        // code the set must answer what IsJavaIdentifierPart answers. A future change to
        // JavaCharacter that moves an ASCII code between the answers must land here as a red
        // test, not as a scan that quietly reads a different rule than the slow path.
        for (int c = 0; c < 128; c++)
        {
            bool expected = JavaCharacter.IsJavaIdentifierPart((char)c);
            Assert.True(expected == Tokenizer.AsciiIdParts.Contains((char)c),
                $"U+{c:X2}: set says {Tokenizer.AsciiIdParts.Contains((char)c)}, IsJavaIdentifierPart says {expected}");
        }
    }

    [Fact]
    public void FindIdentifierEnd_MatchesThePerCharacterWalk()
    {
        TestCastDataProvider regular = new();
        CastDataProvider oracle = new OracleModeCastDataProvider();

        foreach ((string sql, CastDataProvider provider) in new[]
                 {
                     ("select a, b from t", regular),
                     ("select abc, xyz", regular),
                     ("t1.col_name", regular),
                     ("name_2$d", regular),
                     ("ab\u0001c d", regular),      // an ignorable control is part of a name
                     ("ab\u007fcd", regular),        // DEL, the other ignorable shape
                     ("ab\u0009c", regular),         // tab is not ignorable: it terminates
                     ("ab#c d", regular),            // '#' without the Oracle mode terminates
                     ("ab#c d", oracle),             // ... and continues a name with it on
                     ("ab#", oracle),
                     ("ab\U0001D400c d", regular),   // an astral letter is part of a name
                     ("ab\U0001F600c d", regular),   // an astral symbol is not
                     ("ab\U0001D400", regular),      // an astral pair ending the statement
                     ("ab\uD83D", regular),          // an unpaired high surrogate terminates
                     ("ab\U0001D400cd", oracle),
                 })
        {
            int end = sql.Length - 1;
            int actual = new Tokenizer(provider, identifiersToUpper: true, identifiersToLower: false, null)
                .FindIdentifierEnd(sql, end, 0);
            int expected = PerCharacterWalk(sql, end, 0, provider.GetMode().SupportPoundSymbolForColumnNames);
            Assert.True(actual == expected,
                $"sql '{Printable(sql)}': scan said {actual}, the per-character walk says {expected}");
        }
    }

    /// <summary>
    /// The per-character walk of the reference's rule, which is what FindIdentifierEnd answered
    /// before the vectorized sweep: read the character, ask IsJavaIdentifierPart, let the
    /// Oracle '#' through, advance by the code point's length.
    /// </summary>
    private static int PerCharacterWalk(string sql, int end, int i, bool pound)
    {
        i++;
        for (; ; )
        {
            if (i > end)
            {
                break;
            }

            char c = sql[i];
            int cp;
            bool isPart;
            if (JavaCharacter.IsHighSurrogate(c))
            {
                cp = JavaCharacter.CodePointAt(sql, i);
                isPart = JavaCharacter.IsJavaIdentifierPart(cp);
            }
            else
            {
                cp = c;
                isPart = JavaCharacter.IsJavaIdentifierPart(c);
            }

            if (!isPart && (cp != '#' || !pound))
            {
                break;
            }

            i += JavaCharacter.CharCount(cp);
        }

        return i;
    }

    private static string Printable(string s)
    {
        var b = new System.Text.StringBuilder();
        foreach (char c in s)
        {
            _ = b.Append(c < ' ' || c > '~' ? "\\u" + ((int)c).ToString("x4") : c);
        }

        return b.ToString();
    }

    /// <summary>
    /// The regular provider with the Oracle mode's answer to names, which is the only answer
    /// of it this test needs.
    /// </summary>
    private sealed class OracleModeCastDataProvider : CastDataProvider
    {
        private readonly TestCastDataProvider inner = new();

        public ValueTimestampTimeZone CurrentTimestamp() => inner.CurrentTimestamp();

        public TimeZoneProvider CurrentTimeZone() => inner.CurrentTimeZone();

        public bool ZeroBasedEnums() => inner.ZeroBasedEnums();

        public Mode GetMode() => Mode.GetInstance("Oracle")!;
    }
}
