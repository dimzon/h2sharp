/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new tool for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Emits the .NET side of the Java/.NET case mapping comparison, in the same TSV shape
 * as tools/DumpCaseTable.java:
 *
 *   codePoint(hex) <TAB> upper(hex, '+' separated) <TAB> lower(hex, '+' separated)
 *
 * Only code points whose invariant mapping is not the identity are emitted, because the
 * generator reads a missing line as ".NET leaves this code point alone".
 *
 * Usage:
 *   dotnet run --project tools/CaseProbe > case-dotnet.tsv
 */
using System.Globalization;
using System.Text;

namespace H2Sharp.Tools.CaseProbe;

internal static class Program
{
    private static int Main()
    {
        // InvariantGlobalization is on for this project, so ToUpperInvariant is the only
        // mapping available and it is also what StringUtils.ToUpperEnglish uses.
        StringBuilder sb = new();
        for (int cp = 0; cp <= 0x10FFFF; cp++)
        {
            if (cp is >= 0xD800 and <= 0xDFFF)
            {
                // Lone surrogates have no case mapping, and char-based mapping in
                // StringUtils cannot reach an astral code point anyway.
                continue;
            }

            int upper = SimpleUpper(cp);
            int lower = SimpleLower(cp);
            if (upper == cp && lower == cp)
            {
                continue;
            }

            sb.Append(cp.ToString("x", CultureInfo.InvariantCulture)).Append('\t')
                .Append(upper.ToString("x", CultureInfo.InvariantCulture)).Append('\t')
                .Append(lower.ToString("x", CultureInfo.InvariantCulture)).Append('\n');
        }

        Console.Out.Write(sb.ToString());
        return 0;
    }

    /// <summary>
    /// The .NET simple uppercase mapping of one code point, as a single code point.
    /// Astral input is handled through its UTF-16 pair, mirroring how
    /// <c>char.ToUpperInvariant</c> behaves on the characters of a string.
    /// </summary>
    private static int SimpleUpper(int cp) => cp <= char.MaxValue
        ? char.ToUpperInvariant((char)cp)
        : MapSurrogatePair(cp, static c => char.ToUpperInvariant(c));

    private static int SimpleLower(int cp) => cp <= char.MaxValue
        ? char.ToLowerInvariant((char)cp)
        : MapSurrogatePair(cp, static c => char.ToLowerInvariant(c));

    /// <summary>
    /// Maps the high surrogate of an astral code point. The low surrogate never has a
    /// case mapping, so an unchanged high surrogate means .NET leaves the code point
    /// alone.
    /// </summary>
    private static int MapSurrogatePair(int cp, Func<char, char> map)
    {
        char high = (char)(0xD800 + ((cp - 0x10000) >> 10));
        char low = (char)(0xDC00 + ((cp - 0x10000) & 0x3FF));
        char mapped = map(high);
        return mapped == high ? cp : Combine(mapped, low);
    }

    private static int Combine(char high, char low)
        => 0x10000 + ((high - 0xD800) << 10) + (low - 0xDC00);
}
