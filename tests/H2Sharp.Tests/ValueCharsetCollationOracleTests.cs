/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential coverage for the charset collation: CharsetCollator driven
 * directly, and CompareModeDefault through CompareMode.GetInstance.
 *
 * The corpus is built around the two places where a byte-order collation can
 * quietly go wrong. First, Java compares bytes signed, so most non-ASCII text
 * in UTF-8 starts with bytes at or above 0x80 (0xC0 for Cyrillic, 0xE6 for
 * Latin-1 letters, 0xD0 for Greek); an unsigned port would sort every one of
 * those after every ASCII string. Second, up to SECONDARY strength the reference
 * folds case with Locale.ROOT, which applies the full Unicode mapping ("ß" to
 * "SS"), where the simple mapping leaves "ß" alone — so ß, SS, ss and ẞ are all
 * here, along with the Turkish dotted/dotless i, the Greek final sigma, and a
 * non-BMP character that becomes a surrogate pair in UTF-16.
 *
 * The charsets are the ones both sides name the same way: UTF-8, ISO-8859-1,
 * US-ASCII, UTF-16LE, UTF-16BE. "UTF-16" itself is absent on purpose — Java
 * encodes it with a byte-order mark and .NET never emits one, so the key bytes
 * would differ while every comparison still agreed; the two halves are tested
 * instead. Both are documented on CharsetCollator, not hidden.
 *
 * Comparisons are compared as exact integers, not signs, because both sides
 * return the byte difference java.util.Arrays.compare returns and the sign
 * alone would let a wrong difference pass. equalsChars is probed on first
 * characters only: it is substring plus compareString, so longer inputs are
 * already covered by the pair loops, and what the loops cannot see is the
 * out-of-range refusal, which is compared as "throws" on both sides. An unknown
 * charset name is compared the same way.
 *
 * The corpus and the charset names travel as UTF-16 code units in hex rather
 * than as Java literals, so nothing here depends on how the harness writes the
 * snippet file.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class ValueCharsetCollationOracleTests
{
    private static readonly string[] Charsets =
    [
        "UTF-8", "ISO-8859-1", "US-ASCII", "UTF-16LE", "UTF-16BE",
    ];

    private static readonly string[] Strings =
    [
        "", "a", "Z", "abc", "ABC", "aBc", "ss", "SS", "ß", "ẞ",
        "ı", "I", "İ", "i", "é", "É", "ü", "Ω", "ω", "ς", "σ",
        "日本", "café", "café ", " café", "🎉",
        // The three characters .NET's Latin-1 encoder folds onto an ASCII lookalike
        // instead of replacing: I with dot above, a right single quote and a trade mark
        // sign. Without them a port that keeps .NET's default fallback still passes
        // everything else in this corpus.
        "’", "™", "€", "İ’", "™€", "a’b",
        // Two astral characters, because the JDK counts one replacement per code point
        // and .NET counts one per UTF-16 code unit.
        "𝄞", "a🎉𝄞b",
        // Unpaired surrogates: the one input no charset here can represent. They are also
        // the input that decides the replacement, because it is a property of the charset —
        // '?' everywhere except UTF-16, which replaces with U+FFFD.
        "\ud83c", "\udc00x",
        new string('a', 200) + "B",
    ];

    /// <summary>Charsets first, then the corpus: nums[0] says where the corpus starts.</summary>
    private static List<string> Inputs()
    {
        List<string> inputs = [.. Charsets, .. Strings];
        return inputs;
    }

    [JavaOracleFact]
    public void CharsetCollator_MatchesReference()
    {
        int[] strengths = [0, 1, 2, 3];

        string body = """
                      int n = nums[0];
                      String[] css = java.util.Arrays.copyOfRange(inputs, 0, n);
                      String[] strings = java.util.Arrays.copyOfRange(inputs, n, inputs.length);
                      for (String cs : css) {
                        for (int i = 1; i < nums.length; i++) {
                          int st = nums[i];
                          org.h2.value.CharsetCollator cc = new org.h2.value.CharsetCollator(
                              java.nio.charset.Charset.forName(cs));
                          cc.setStrength(st);
                          System.out.println("== " + cs + " " + st);
                          for (String s : strings) {
                            byte[] k = cc.getCollationKey(s).toByteArray();
                            StringBuilder hb = new StringBuilder();
                            for (byte x : k) {
                              hb.append(String.format("%02x", x & 0xff));
                            }
                            System.out.println("k " + hb);
                          }
                          for (String x : strings) {
                            for (String y : strings) {
                              System.out.println(cc.compare(x, y));
                            }
                          }
                        }
                      }
            """;

        List<string> java = OracleRunner.Run(Inputs(), body, [Charsets.Length, .. strengths]);
        List<string> csharp = DescribeCollator(strengths);

        Compare(java, csharp);
    }

    /// <summary>
    /// The replacement belongs to the charset, and the JDK keeps it where it can be asked
    /// for: <c>Charset.newEncoder().replacement()</c>. This compares that declared answer
    /// with what the port writes, charset by charset, so the rule in CharsetCollator — '?'
    /// except for UTF-16, which replaces with U+FFFD — is pinned against the reference's own
    /// account of itself and not only against what a lone surrogate happens to produce.
    /// </summary>
    [JavaOracleFact]
    public void ReplacementIsTheCharsetsOwn()
    {
        string body = """
                      for (String cs : inputs) {
                        byte[] r = java.nio.charset.Charset.forName(cs).newEncoder().replacement();
                        StringBuilder hb = new StringBuilder();
                        for (byte x : r) {
                          hb.append(String.format("%02x", x & 0xff));
                        }
                        System.out.println(cs + " " + hb);
                      }
            """;

        List<string> java = OracleRunner.Run(Charsets.ToList(), body);
        List<string> csharp = Charsets
            .Select(cs => cs + " " + Hex(new CharsetCollator(cs).GetCollationKey("\ud83c").ToByteArray()))
            .ToList();

        Compare(java, csharp);
    }

    private static string Hex(byte[] bytes)
    {
        StringBuilder builder = new();
        foreach (byte b in bytes)
        {
            _ = builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }

    [JavaOracleFact]
    public void CompareModeDefault_MatchesReference()
    {
        int[] strengths = [1, 2, 3];

        string body = """
                      int n = nums[0];
                      String[] css = java.util.Arrays.copyOfRange(inputs, 0, n);
                      String[] strings = java.util.Arrays.copyOfRange(inputs, n, inputs.length);
                      for (String cs : css) {
                        for (int i = 1; i < nums.length; i++) {
                          int st = nums[i];
                          org.h2.value.CompareMode m =
                              org.h2.value.CompareMode.getInstance("CHARSET_" + cs, st);
                          System.out.println("== " + cs + " " + st);
                          for (String x : strings) {
                            for (String y : strings) {
                              System.out.println(m.compareString(x, y, false));
                            }
                          }
                          for (String x : strings) {
                            for (String y : strings) {
                              System.out.println(m.compareString(x, y, true));
                            }
                          }
                          for (String x : strings) {
                            for (String y : strings) {
                              String r;
                              try {
                                r = m.equalsChars(x, 0, y, 0, false)
                                    + "|" + m.equalsChars(x, 0, y, 0, true);
                              } catch (RuntimeException ex) {
                                r = "THROW";
                              }
                              System.out.println(r);
                            }
                          }
                        }
                      }
                      try {
                        org.h2.value.CompareMode.getInstance("CHARSET_NOPE_X", 1);
                        System.out.println("no-throw");
                      } catch (RuntimeException ex) {
                        System.out.println("THROW");
                      }
                      try {
                        org.h2.value.CompareMode m =
                            org.h2.value.CompareMode.getInstance("CHARSET_UTF-8", 3);
                        m.equalsChars("a", 1, "b", 0, false);
                        System.out.println("no-throw");
                      } catch (RuntimeException ex) {
                        System.out.println("THROW");
                      }
            """;

        List<string> java = OracleRunner.Run(Inputs(), body, [Charsets.Length, .. strengths]);
        List<string> csharp = DescribeMode(strengths);

        Compare(java, csharp);
    }

    /// <summary>
    /// Compares line by line and, on the first difference, reports where it is: fifty-five
    /// thousand lines of output are unreadable without the block header above the line that
    /// differs, and without knowing how many lines were identical before it.
    /// </summary>
    private static void Compare(List<string> java, List<string> csharp)
    {
        Assert.Equal(java.Count, csharp.Count);
        for (int i = 0; i < java.Count; i++)
        {
            if (java[i] == csharp[i])
            {
                continue;
            }

            StringBuilder message = new();
            _ = message.Append("line ").Append(i).Append(" (the ").Append(i + 1)
                .Append(" of ").Append(java.Count).Append(" lines):\n");
            int from = Math.Max(0, i - 4);
            for (int j = from; j <= i; j++)
            {
                _ = message.Append(j == i ? "> " : "  ").Append(j).Append("  java:   ").Append(java[j])
                    .Append('\n').Append(j == i ? "> " : "  ").Append(j).Append("  csharp: ")
                    .Append(csharp[j]).Append('\n');
            }

            Assert.Fail(message.ToString());
        }
    }

    private static List<string> DescribeCollator(int[] strengths)
    {
        List<string> lines = [];
        foreach (string cs in Charsets)
        {
            foreach (int st in strengths)
            {
                CharsetCollator cc = new(cs) { Strength = st };
                lines.Add("== " + cs + " " + st);
                foreach (string s in Strings)
                {
                    StringBuilder hb = new();
                    foreach (byte x in cc.GetCollationKey(s).ToByteArray())
                    {
                        _ = hb.Append(x.ToString("x2"));
                    }

                    lines.Add("k " + hb);
                }

                foreach (string x in Strings)
                {
                    foreach (string y in Strings)
                    {
                        lines.Add(cc.Compare(x, y).ToString());
                    }
                }
            }
        }

        return lines;
    }

    private static List<string> DescribeMode(int[] strengths)
    {
        List<string> lines = [];
        foreach (string cs in Charsets)
        {
            foreach (int st in strengths)
            {
                CompareMode m = CompareMode.GetInstance("CHARSET_" + cs, st);
                lines.Add("== " + cs + " " + st);
                foreach (string x in Strings)
                {
                    foreach (string y in Strings)
                    {
                        lines.Add(m.CompareString(x, y, false).ToString());
                    }
                }

                foreach (string x in Strings)
                {
                    foreach (string y in Strings)
                    {
                        lines.Add(m.CompareString(x, y, true).ToString());
                    }
                }

                foreach (string x in Strings)
                {
                    foreach (string y in Strings)
                    {
                        // The empty string has no first character, and the reference refuses
                        // there; both sides answer "throws" rather than a boolean.
                        try
                        {
                            lines.Add(J(m.EqualsChars(x, 0, y, 0, false)) + "|" + J(m.EqualsChars(x, 0, y, 0, true)));
                        }
                        catch (Exception)
                        {
                            lines.Add("THROW");
                        }
                    }
                }
            }
        }

        try
        {
            _ = CompareMode.GetInstance("CHARSET_NOPE_X", 1);
            lines.Add("no-throw");
        }
        catch (Exception)
        {
            lines.Add("THROW");
        }

        try
        {
            CompareMode m = CompareMode.GetInstance("CHARSET_UTF-8", 3);
            _ = m.EqualsChars("a", 1, "b", 0, false);
            lines.Add("no-throw");
        }
        catch (Exception)
        {
            lines.Add("THROW");
        }

        return lines;
    }

    /// <summary>
    /// A boolean the way the reference prints one: System.out.println(boolean) says
    /// "true", and C# says "True".
    /// </summary>
    private static string J(bool value) => value ? "true" : "false";
}
