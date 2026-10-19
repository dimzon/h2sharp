/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new tests for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Exhaustive differential test of the java.text.MessageFormat subset that
 * Message/ErrorMessages.Format implements. Every pattern over the alphabet
 * {', '{', '}', '0'} up to length 4 (340 patterns) is evaluated by both the reference
 * JVM and H2Sharp and the results must agree, including which patterns throw.
 */
using System;
using System.Collections.Generic;
using System.IO;
using H2Sharp.Api;
using H2Sharp.Message;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

public sealed class MessageFormatOracleTests
{
    private const int MaxPatternLength = 4;

    [JavaOracleFact]
    public void Format_MatchesReferenceJvm_Exhaustively()
    {
        List<string> patterns = MessageFormatCorpus.Build(MaxPatternLength);

        List<string> java = RunOracle(patterns);
        List<string> csharp = new(patterns.Count);
        for (int i = 0; i < patterns.Count; i++)
        {
            csharp.Add(Evaluate(patterns[i]));
        }

        Assert.True(
            java.Count == csharp.Count,
            $"line count: java={java.Count} csharp={csharp.Count}");

        List<string> mismatches = new();
        for (int i = 0; i < java.Count; i++)
        {
            if (java[i] != csharp[i])
            {
                mismatches.Add(
                    $"{i,3} [{patterns[i]}] java=[{java[i]}] csharp=[{csharp[i]}]");
            }
        }

        File.WriteAllLines(
            Path.Combine(Path.GetTempPath(), "h2sharp-messageformat-mismatches.txt"),
            mismatches);

        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} of {patterns.Count} patterns differ; full list at "
            + Path.Combine(Path.GetTempPath(), "h2sharp-messageformat-mismatches.txt") + ":\n"
            + string.Join("\n", mismatches.Take(60)));
    }

    [Fact]
    public void Format_AgreesWithOracleOnKnownAwkwardPatterns()
    {
        // A selection of the behaviours the exhaustive test pins down, recorded so
        // that a future reader does not have to run the JVM to learn them. Every value
        // here was read off the reference JVM.
        Assert.Equal("{0}", Format("'{0}'"));      // quoted braces are literal, not a placeholder
        Assert.Equal("''", Format("''''"));        // '' is a literal apostrophe
        Assert.Equal("", Format("'"));             // an unmatched quote is closed at the end
        Assert.Equal("'0", Format("''0"));
        Assert.Equal("{0}", Format("'{'0'}'"));
        Assert.Equal("'0}", Format("''0}'"));
        Assert.Equal("{", Format("'{"));           // a quoted brace never opens a placeholder
        Assert.Equal("0{", Format("'0{"));
        Assert.Equal("ab", Format("a'b"));          // a lone apostrophe vanishes
        Assert.Equal("AB", ErrorMessages.Format("{0}{1}", "A", "B"));   // adjacent placeholders
        Assert.Equal("xByAzB", ErrorMessages.Format("x{1}y{0}z{1}", "A", "B"));
    }

    /// <summary>
    /// The test that matters most: every real message template in H2's English
    /// catalogue, formatted with several argument sets, must render identically to
    /// org.h2.message.DbException.translate. Real templates are longer and more
    /// varied than the short patterns of the exhaustive test, and this is what users
    /// actually see.
    /// </summary>
    [JavaOracleFact]
    public void Translate_MatchesReferenceJvm_ForEveryCatalogueTemplate()
    {
        List<string> states = new();
        foreach ((string _, int value) in ErrorCodeTests.GetConstantValues())
        {
            string state = ErrorCode.GetState(value);
            if (!states.Contains(state))
            {
                states.Add(state);
            }
        }

        Dictionary<string, string> english = ErrorMessages.BuildCatalogue("en");

        List<string> cases = new();
        foreach (string state in states)
        {
            // One case per (template, argument-set) pair, so that the oracle and the
            // C# side can be driven from a single flat list.
            foreach (string[] arguments in ArgumentSets)
            {
                cases.Add(state);
                cases.AddRange(arguments);
            }
        }

        List<string> java = RunTranslateOracle(states, ArgumentSets);

        List<string> csharp = new(cases.Count);
        for (int i = 0; i < cases.Count;)
        {
            string state = cases[i++];
            string[] arguments = new string[CasesPerTemplate];
            for (int a = 0; a < arguments.Length; a++)
            {
                arguments[a] = cases[i++];
            }

            csharp.Add("=" + ErrorMessages.Translate(state, arguments));
        }

        Assert.True(
            java.Count == csharp.Count,
            $"line count: java={java.Count} csharp={csharp.Count}");

        List<string> mismatches = new();
        for (int i = 0; i < java.Count; i++)
        {
            if (java[i] != csharp[i])
            {
                mismatches.Add($"case {i}: java=[{java[i]}] csharp=[{csharp[i]}]");
            }
        }

        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} of {java.Count} translated messages differ:\n"
            + string.Join("\n", mismatches.Take(30)));
    }

    private const int CasesPerTemplate = 3;

    /// <summary>
    /// Argument sets covering quoting-relevant characters, an apostrophe, a brace and
    /// an empty value.
    /// </summary>
    private static List<string[]> ArgumentSets { get; } =
    [
        ["T1", "42", "x"],
        ["it's", "has 'quotes'", "a{b}c"],
        ["", "tab\there", "tail"],
    ];

    private static List<string> RunTranslateOracle(List<string> states, List<string[]> argumentSets)
    {
        // DbException.translate(sqlstate, params) is private; it is the exact routine
        // under test, so it is invoked reflectively rather than reimplemented here.
        // The catalogue is chosen from the JVM default locale, so -Duser.language pins
        // it to English.
        string source = """
            import java.lang.reflect.Method;
            import java.nio.file.Files;
            import java.nio.file.Path;

            public class Oracle {
              public static void main(String[] args) throws Exception {
                Method m = Class.forName("org.h2.message.DbException")
                    .getDeclaredMethod("translate", String.class, String[].class);
                m.setAccessible(true);
                String[] lines = readInputs();
                int i = 0;
                while (i < lines.length) {
                  String state = lines[i++];
                  int count = Integer.parseInt(lines[i++]);
                  String[] params = new String[count];
                  for (int p = 0; p < count; p++) {
                    params[p] = lines[i++];
                  }
                  String message = (String) m.invoke(null, state, params);
                  System.out.println("=" + message);
                }
              }

              static String[] readInputs() throws Exception {
                java.util.List<String> lines = Files.readAllLines(Path.of("inputs.hex"));
                String[] out = new String[lines.size()];
                for (int i = 0; i < lines.size(); i++) {
                  out[i] = fromHex(lines.get(i));
                }
                return out;
              }

              static String fromHex(String s) {
                StringBuilder b = new StringBuilder(s.length() / 4);
                for (int i = 0; i + 3 < s.length(); i += 4) {
                  b.append((char) Integer.parseInt(s.substring(i, i + 4), 16));
                }
                return b.toString();
              }
            }
            """;

        List<string> payload = new();
        foreach (string state in states)
        {
            foreach (string[] arguments in argumentSets)
            {
                payload.Add(state);
                payload.Add(arguments.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                payload.AddRange(arguments);
            }
        }

        if (JavaOracle.H2Jar is null)
        {
            throw new InvalidOperationException(
                "H2 jar not found. Build it with: "
                + "(cd reference/h2/h2 && mvn -B -q -DskipTests package)");
        }

        return JavaOracle.OutputLines(
            JavaOracle.RunWithInputs(
                source,
                "Oracle",
                payload,
                [JavaOracle.H2Jar],
                ["-Duser.language=en", "-Duser.country=", "-Duser.variant="]));
    }

    [Fact]
    public void Format_PrintsThePlaceholderWhenTheArgumentIsUnavailable()
    {
        // MessageFormat substitutes the placeholder text back rather than failing, so a
        // message template with too many parameters still produces readable output.
        Assert.Equal("{9}", Format("{9}"));
    }

    [JavaOracleFact]
    public void Format_ThrowsOnMalformedPatterns()
    {
        Assert.Throws<FormatException>(() => ErrorMessages.Format("{", "A"));
        Assert.Throws<FormatException>(() => ErrorMessages.Format("{0", "A"));
        Assert.Throws<FormatException>(() => ErrorMessages.Format("{}", "A"));
        Assert.Throws<FormatException>(() => ErrorMessages.Format("'{'{", "A"));
        Assert.Throws<FormatException>(() => ErrorMessages.Format("{x}", "A"));
    }

    [JavaOracleFact]
    public void Format_RendersNullArgumentsAsNull()
    {
        // H2 passes null slots when a message parameter is absent.
        Assert.Equal("null and null", ErrorMessages.Format("{0} and {1}", null!, null!));
        Assert.Equal("null", ErrorMessages.Format("{0}", (object?)null!));
    }

    private static string Format(string pattern)
        => ErrorMessages.Format(pattern, MessageFormatCorpus.Arguments);

    /// <summary>
    /// Formats a pattern and returns the result, or "THROWS" for a malformed pattern,
    /// so that throwing patterns can be compared too. The "=" prefix keeps an empty
    /// result visible, since the harness drops blank lines.
    /// </summary>
    private static string Evaluate(string pattern)
    {
        try
        {
            return "=" + ErrorMessages.Format(pattern, MessageFormatCorpus.Arguments);
        }
        catch (FormatException)
        {
            return "=THROWS";
        }
    }

    private static List<string> RunOracle(List<string> patterns)
    {
        string source = """
            import java.nio.charset.StandardCharsets;
            import java.nio.file.Files;
            import java.nio.file.Path;
            import java.text.MessageFormat;

            public class Oracle {
              public static void main(String[] args) throws Exception {
                String[] inputs = readInputs();
                for (String pattern : inputs) {
                  String result;
                  try {
                    result = MessageFormat.format(pattern, "A", 1, 2);
                  } catch (RuntimeException e) {
                    // Both runtimes throw their own exception type for a malformed
                    // pattern; only the fact of throwing is compared.
                    result = "THROWS";
                  }
                  // "=" prefix so that an empty result is not dropped as a blank line.
                  System.out.println("=" + result);
                }
              }

              static String[] readInputs() throws Exception {
                java.util.List<String> lines = Files.readAllLines(Path.of("inputs.hex"));
                String[] out = new String[lines.size()];
                for (int i = 0; i < lines.size(); i++) {
                  out[i] = fromHex(lines.get(i));
                }
                return out;
              }

              static String fromHex(String s) {
                StringBuilder b = new StringBuilder(s.length() / 4);
                for (int i = 0; i + 3 < s.length(); i += 4) {
                  b.append((char) Integer.parseInt(s.substring(i, i + 4), 16));
                }
                return b.toString();
              }
            }
            """;

        if (JavaOracle.H2Jar is null)
        {
            throw new InvalidOperationException(
                "H2 jar not found. Build it with: "
                + "(cd reference/h2/h2 && mvn -B -q -DskipTests package)");
        }

        return JavaOracle.OutputLines(
            JavaOracle.RunWithInputs(source, "Oracle", patterns, [JavaOracle.H2Jar]));
    }
}
