/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new tests for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Differential tests for the message catalogue and the parameter quoting/formatting
 * that org.h2.message.DbException.quote and translate perform. Quoting is the most
 * error-prone part: it branches on java.lang.Character.getType ordinals, and .NET
 * UnicodeCategory has a different numbering, so every case is checked against the
 * reference JVM rather than reasoned about.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Message;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ErrorMessagesTests
{
    [Fact]
    public void Catalogue_ContainsTheEnglishMessages()
    {
        Assert.True(ErrorMessages.Count >= 200, "unexpected catalogue size");
        Assert.Equal("No data is available", ErrorMessages.GetTemplate("02000"));
        Assert.Equal(
            "Invalid parameter count for {0}, expected count: {1}",
            ErrorMessages.GetTemplate("07001"));
        Assert.Equal("Error opening database: {0}", ErrorMessages.GetTemplate("08000"));
    }

    [Fact]
    public void GetTemplate_ReturnsH2PlaceholderForUnknownState()
        => Assert.Equal("(Message NOSUCH not found)", ErrorMessages.GetTemplate("NOSUCH"));

    [Theory]
    // plain text
    [InlineData("hello", "\"hello\"")]
    [InlineData("", "\"\"")]
    // H2 doubles these two characters inside the quotes
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("a\\b", "\"a\\\\b\"")]
    // control characters become \xx - note that appendHex(cp, 2) emits FOUR hex
    // digits, because H2 loops bytes * 8 bits in steps of 4
    [InlineData("a\tb", "\"a\\0009b\"")]
    [InlineData("a\nb", "\"a\\000ab\"")]
    [InlineData("a\rb", "\"a\\000db\"")]
    [InlineData("\u0001", "\"\\0001\"")]
    [InlineData("\u000f", "\"\\000f\"")]
    // format characters, separators and private use are escaped
    [InlineData("\u00ad", "\"\\00ad\"")]   // SOFT HYPHEN, category FORMAT
    [InlineData("\u00a0", "\"\\00a0\"")]   // NO-BREAK SPACE, category SPACE_SEPARATOR
    [InlineData("\u2028", "\"\\2028\"")] // LINE SEPARATOR
    [InlineData("\u2029", "\"\\2029\"")] // PARAGRAPH SEPARATOR
    // letters, digits, symbols and punctuation are kept literal
    [InlineData("\u03a9", "\"\u03a9\"")]         // OMEGA, other letter
    [InlineData("\u00e9", "\"\u00e9\"")]         // e with acute, lower case letter
    [InlineData("\ud83d\ude00", "\"\ud83d\ude00\"")] // U+1F600, other symbol
    // space is explicitly kept by H2 (cp != ' ')
    [InlineData(" ", "\" \"")]
    [InlineData("a b", "\"a b\"")]
    public void Quote_MatchesVerifiedJavaBehaviour(string input, string expected)
        => Assert.Equal(expected, ErrorMessages.Quote(input));

    [Fact]
    public void Quote_TreatsUnpairedSurrogateAsSurrogateCategory()
    {
        // Java's codePointAt returns the lone surrogate itself and getType reports
        // SURROGATE, so it is hex-escaped like a BMP character.
        Assert.Equal("\"\\d800\"", ErrorMessages.Quote("\uD800"));
    }

    [Fact]
    public void Quote_LeavesNonEscapableAstralCharactersLiteral()
    {
        // U+1F600 has category So, which is outside the range H2 escapes, so it stays
        // literal. The \+xxx form exists for astral characters that DO get escaped.
        Assert.Equal("\"\uD83D\uDE00\"", ErrorMessages.Quote("\uD83D\uDE00"));
    }

    [Fact]
    public void Quote_EscapesPrivateUseWithBmpForm()
    {
        // U+E000 is in the Private Use Area, which H2 does escape (character type 17
        // is inside SPACE_SEPARATOR..SURROGATE), but it is below the BMP, so it uses
        // the \xx form rather than the three-digit \+xxx form.
        Assert.Equal("\"\\e000\"", ErrorMessages.Quote("\uE000"));
    }

    [Fact]
    public void Translate_SubstitutesAndQuotesParameters()
    {
        Assert.Equal(
            "Error opening database: \"/tmp/x\"",
            ErrorMessages.Translate("08000", "/tmp/x"));

        Assert.Equal(
            "Invalid parameter count for \"ABS\", expected count: \"2\"",
            ErrorMessages.Translate("07001", "ABS", "2"));
    }

    [Fact]
    public void Translate_LeavesEmptyAndNullParametersUnquoted()
    {
        // H2 only quotes non-empty parameters, so an empty slot stays empty.
        // java.text.MessageFormat then renders a null argument as the text "null".
        Assert.Equal(
            "Invalid parameter count for , expected count: null",
            ErrorMessages.Translate("07001", "", null));
    }

    [Fact]
    public void Translate_WithoutParameters_ReturnsTemplate()
        => Assert.Equal("No data is available", ErrorMessages.Translate("02000"));

[Theory]
    [InlineData("{0}", "a")]
    [InlineData("a{0}b{1}c", "aab1c")]
    [InlineData("no placeholders", "no placeholders")]
    [InlineData("''{0}''", "'a'")]
    [InlineData("}", "}")]
    public void Format_MatchesMessageFormatQuotingRules(string pattern, string expected)
        => Assert.Equal(expected, ErrorMessages.Format(pattern, "a", 1, 2));

    [Fact]
    public void Format_ThrowsOnBadPatternLikeJavaMessageFormat()
    {
        // Note that MessageFormat has no "{{" escape: braces are quoted with an
        // apostrophe, and a doubled opening brace is a malformed argument.
        Assert.Throws<FormatException>(() => ErrorMessages.Format("no closing brace {0", "x"));
        Assert.Throws<FormatException>(() => ErrorMessages.Format("{{0}}", "x"));
        Assert.Throws<FormatException>(() => ErrorMessages.Format("{x}", "x"));
    }

    [JavaOracleFact]
    public void Quote_MatchesReferenceJvm()
    {
        List<string> inputs = BuildQuoteInputs();
        List<string> java = RunQuoteOracle(inputs);
        List<string> csharp = new(inputs.Count);
        foreach (string s in inputs)
        {
            csharp.Add(JavaOracle.ToUtf16LeBytesHex(ErrorMessages.Quote(s)));
        }

        Assert.Equal(java, csharp);
    }

    [JavaOracleFact]
    public void Catalogue_MatchesReferenceJvm()
    {
        // Every SQLSTATE reachable from H2's error codes must resolve to the same
        // message the reference JVM produces. Both sides are forced to English:
        // H2 picks a translation from Locale.getDefault(), which is whatever this
        // machine is configured for, so the comparison would otherwise depend on the
        // developer's locale.
        List<string> distinct = new();
        foreach ((string _, int value) in ErrorCodeTests.GetConstantValues())
        {
            string state = ErrorCode.GetState(value);
            if (!distinct.Contains(state))
            {
                distinct.Add(state);
            }
        }

        List<string> java = RunCatalogueOracle(distinct, "en");

        Dictionary<string, string> english = ErrorMessages.BuildCatalogue("en");
        List<string> csharp = new(distinct.Count);
        foreach (string s in distinct)
        {
            csharp.Add(JavaOracle.ToUtf16LeBytesHex(ErrorMessages.GetTemplate(english, s)));
        }

        Assert.Equal(java, csharp);
    }

    [JavaOracleFact]
    public void TranslatedCatalogue_MatchesReferenceJvm()
    {
        // H2 prepends the translation to the English text rather than replacing it,
        // because some applications only handle the English wording. Verify that for
        // every state in one non-English catalogue.
        const string Language = "ru";

        List<string> distinct = new();
        foreach ((string _, int value) in ErrorCodeTests.GetConstantValues())
        {
            string state = ErrorCode.GetState(value);
            if (!distinct.Contains(state))
            {
                distinct.Add(state);
            }
        }

        List<string> java = RunCatalogueOracle(distinct, Language);

        Dictionary<string, string> translated = ErrorMessages.BuildCatalogue(Language);
        Assert.NotEmpty(translated);

        List<string> csharp = new(distinct.Count);
        foreach (string s in distinct)
        {
            csharp.Add(JavaOracle.ToUtf16LeBytesHex(ErrorMessages.GetTemplate(translated, s)));
        }

        Assert.Equal(java, csharp);
    }

    private static List<string> BuildQuoteInputs()
    {
        List<string> inputs = new()
        {
            "hello", "", " ", "a b", "a\"b", "a\\b", "a\tb", "a\nb", "a\rb",
            "a\u0001b", "\u00ad", "\u2028", "\u2029", "\u00a0", "\u3000",
            "\uD83D\uDE00", "\uD800", "\uDC00", "\uFFFD", "Ω", "Ω", "é",
            "table \"T\"", "SELECT * FROM T", "-42", "1e400",
        };

        // Sweep the first 512 BMP code points so category boundaries are covered.
        for (int cp = 0; cp < 512; cp++)
        {
            inputs.Add(char.ConvertFromUtf32(cp));
        }

        return inputs;
    }

    private static List<string> RunQuoteOracle(List<string> inputs)
    {
        // DbException.quote is private; it is the exact routine under test, so it is
        // invoked reflectively rather than reimplemented in the oracle.
        string source = """
            import java.lang.reflect.Method;
            import java.nio.charset.StandardCharsets;
            import java.nio.file.Files;
            import java.nio.file.Path;

            public class Oracle {
              public static void main(String[] args) throws Exception {
                Method m = Class.forName("org.h2.message.DbException")
                    .getDeclaredMethod("quote", String.class);
                m.setAccessible(true);
                for (String s : readInputs()) {
                  String quoted = (String) m.invoke(null, s);
                  System.out.println(org.h2.util.StringUtils.convertBytesToHex(
                      quoted.getBytes(StandardCharsets.UTF_16LE)));
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

              /** Decodes four hex digits per UTF-16 code unit; keeps lone surrogates. */
              static String fromHex(String s) {
                StringBuilder b = new StringBuilder(s.length() / 4);
                for (int i = 0; i + 3 < s.length(); i += 4) {
                  b.append((char) Integer.parseInt(s.substring(i, i + 4), 16));
                }
                return b.toString();
              }
            }
            """;

        return RunOracle(source, inputs, language: null);
    }

    private static List<string> RunCatalogueOracle(List<string> states, string language)
    {
        // DbException.translate(sqlstate, params) is the private routine that resolves
        // a SQLSTATE to a message template. Calling it directly avoids the
        // "[code-build]" suffix that the public factory appends. The catalogue it uses
        // is chosen from the JVM default locale, so -Duser.language pins it.
        string source = """
            import java.lang.reflect.Method;
            import java.nio.charset.StandardCharsets;
            import java.nio.file.Files;
            import java.nio.file.Path;

            public class Oracle {
              public static void main(String[] args) throws Exception {
                Method m = Class.forName("org.h2.message.DbException")
                    .getDeclaredMethod("translate", String.class, String[].class);
                m.setAccessible(true);
                for (String state : readInputs()) {
                  String message = (String) m.invoke(null, state, (String[]) null);
                  System.out.println(org.h2.util.StringUtils.convertBytesToHex(
                      message.getBytes(StandardCharsets.UTF_16LE)));
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

        return RunOracle(source, states, language);
    }

    private static List<string> RunOracle(string source, List<string> inputs, string? language)
    {
        if (JavaOracle.H2Jar is null)
        {
            throw new InvalidOperationException(
                "H2 jar not found. Build it with: "
                + "(cd reference/h2/h2 && mvn -B -q -DskipTests package)");
        }

        string[]? extraJavaArgs = language is null
            ? null
            :
            [
                "-Duser.language=" + language,
                "-Duser.country=",
                "-Duser.variant=",
            ];

        return JavaOracle.OutputLines(
            JavaOracle.RunWithInputs(source, "Oracle", inputs, [JavaOracle.H2Jar], extraJavaArgs));
    }
}