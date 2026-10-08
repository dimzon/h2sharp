/*
 * Copyright 2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from the message-loading and format parts of org.h2.message.DbException.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: extracted from DbException into its own type. The catalogue is loaded
 * from the same embedded H2 resource files, and java.text.MessageFormat is replaced
 * by the positional subset H2 actually uses ({0}..{9} plus the standard apostrophe
 * quoting rules).
 *
 * Translations follow H2: the English catalogue is always loaded, and a translation
 * for the current UI culture is *prepended* to each message rather than replacing
 * it, because H2 formats errors as "translated message\nenglish message".
 *
 * Note on Unicode categories: H2 branches on java.lang.Character.getType
 * ordinals. Those ordinals differ from System.GlobalCategory, so
 * JavaCharacterType maps explicitly; see GetUnicodeCategory.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using H2Sharp.Util;

namespace H2Sharp.Message;

/// <summary>
/// The SQL error message catalogue and the <c>MessageFormat</c> subset used to
/// parameterise messages.
/// </summary>
public static class ErrorMessages
{
    private const string ResourcePrefix = "H2Sharp.Resources._messages_";
    private const string EnglishResource = ResourcePrefix + "en.prop";

    private static readonly Dictionary<string, string> Messages = Load();

    /// <summary>
    /// Number of entries in the catalogue. Exposed for tests.
    /// </summary>
    internal static int Count => Messages.Count;

    /// <summary>
    /// Returns the raw, unformatted message for a SQLSTATE, or H2's placeholder
    /// when the state is unknown.
    /// </summary>
    /// <param name="sqlState">the SQLSTATE</param>
    /// <returns>the message template</returns>
    internal static string GetTemplate(string sqlState)
        => Messages.TryGetValue(sqlState, out string? template)
            ? template
            : "(Message " + sqlState + " not found)";

    /// <summary>
    /// Looks up a message template in an explicit catalogue. Used by the tests, which
    /// must not depend on the process UI culture.
    /// </summary>
    /// <param name="catalogue">the catalogue</param>
    /// <param name="sqlState">the SQLSTATE</param>
    /// <returns>the message template</returns>
    internal static string GetTemplate(
        Dictionary<string, string> catalogue, string sqlState)
        => catalogue.TryGetValue(sqlState, out string? template)
            ? template
            : "(Message " + sqlState + " not found)";

    /// <summary>
    /// Returns the message for a SQLSTATE with the parameters substituted.
    /// Non-empty parameters are quoted first, exactly as H2 does.
    /// </summary>
    /// <param name="sqlState">the SQLSTATE</param>
    /// <param name="parameters">the parameters; may be null or empty</param>
    /// <returns>the formatted message</returns>
    public static string Translate(string sqlState, params string?[]? parameters)
    {
        string message = GetTemplate(sqlState);
        if (parameters is null || parameters.Length == 0)
        {
            return message;
        }

        object?[] quoted = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            string? s = parameters[i];
            quoted[i] = !string.IsNullOrEmpty(s) ? Quote(s) : s;
        }

        return Format(message, quoted);
    }

    /// <summary>
    /// Wraps a value in double quotes, escaping anything H2 considers non-printable
    /// with <c>\xx</c> for BMP code points and <c>\+xxx</c> for astral ones.
    /// A double quote or backslash is doubled. Plain spaces are kept.
    /// </summary>
    /// <param name="s">the value</param>
    /// <returns>the quoted value</returns>
    public static string Quote(string s)
    {
        int l = s.Length;
        StringBuilder builder = new StringBuilder(l + 2).Append('"');
        int i = 0;
        while (i < l)
        {
            int cp = ReadCodePoint(s, ref i);

            int t = GetUnicodeCategory(cp);

            if (t == 0 || (t >= JavaCharacterType.SpaceSeparator && t <= JavaCharacterType.Surrogate
                && cp != ' '))
            {
                if (cp <= 0xFFFF)
                {
                    Hex.AppendHex(builder.Append('\\'), cp, 2);
                }
                else
                {
                    Hex.AppendHex(builder.Append("\\+"), cp, 3);
                }
            }
            else
            {
                if (cp == '"' || cp == '\\')
                {
                    builder.Append((char)cp);
                }

                builder.Append(char.ConvertFromUtf32(cp));
            }
        }

        return builder.Append('"').ToString();
    }

    /// <summary>
    /// Reads one code point starting at <paramref name="i"/> and advances it.
    /// Matches Java's <c>String.codePointAt</c> + <c>Character.charCount</c>:
    /// a valid surrogate pair yields one astral code point and advances two
    /// characters; an unpaired surrogate yields the surrogate code unit itself and
    /// advances one character (Java does not throw here).
    /// </summary>
    private static int ReadCodePoint(string s, ref int i)
    {
        int cp = s[i];
        if (i + 1 < s.Length)
        {
            int next = s[i + 1];
            if (cp >= 0xD800 && cp <= 0xDBFF && next >= 0xDC00 && next <= 0xDFFF)
            {
                i += 2;
                return 0x10000 + ((cp - 0xD800) << 10) + (next - 0xDC00);
            }
        }

        i += 1;
        return cp;
    }

    /// <summary>
    /// The java.lang.Character.UnicodeCategory ordinals that H2's quoting rule
    /// compares against.
    /// </summary>
    private static class JavaCharacterType
    {
        internal const int Unassigned = 0;
        internal const int SpaceSeparator = 12;
        internal const int Surrogate = 18;
    }

    /// <summary>
    /// Maps a code point to its java.lang.Character.getType ordinal.
    /// Only the distinction H2 makes matters: 0 (UNASSIGNED) and the inclusive
    /// range SPACE_SEPARATOR(12)..SURROGATE(18) are escapable. Everything else is
    /// reported as <see cref="JavaCharacterType.Surrogate"/> + 1 so that it compares
    /// as "not escapable".
    /// </summary>
    private static int GetUnicodeCategory(int codePoint)
    {
        if (codePoint is >= 0xD800 and <= 0xDFFF)
        {
            // Java: Character.getType reports SURROGATE for a lone surrogate.
            return 18;
        }

        UnicodeCategory category = Rune.GetUnicodeCategory(new Rune(codePoint));
        switch (category)
        {
            case UnicodeCategory.Control:
                return 15; // Character.CONTROL
            case UnicodeCategory.Format:
                return 16; // Character.FORMAT
            case UnicodeCategory.Surrogate:
                return 18; // Character.SURROGATE
            case UnicodeCategory.PrivateUse:
                return 17; // Character.PRIVATE_USE
            case UnicodeCategory.OtherNotAssigned:
                return 0; // Character.UNASSIGNED
            case UnicodeCategory.LineSeparator:
                return 13; // Character.LINE_SEPARATOR
            case UnicodeCategory.ParagraphSeparator:
                return 14; // Character.PARAGRAPH_SEPARATOR
            case UnicodeCategory.SpaceSeparator:
                return 12; // Character.SPACE_SEPARATOR
            default:
                // Java ordinals 19 and above (punctuation, symbols, letters, digits,
                // marks, ...) are all outside the range H2 escapes.
                return 19;
        }
    }

    /// <summary>
    /// The positional subset of <c>java.text.MessageFormat</c> that H2's messages
    /// use, ported from the JDK's two-phase algorithm.
    /// </summary>
    /// <remarks>
    /// Phase 1 (<c>applyPatternImpl</c>) splits the pattern into raw text plus the
    /// argument indices, and phase 2 (<c>subformat</c>) splices the formatted
    /// arguments into the raw text. The two phases must stay separate because phase 1
    /// is what decides whether a brace is a placeholder or literal text.
    ///
    /// The quoting rules, which are easy to get subtly wrong and which H2 relies on:
    /// <list type="bullet">
    /// <item>A single apostrophe toggles "in quote". Inside a quoted region every
    /// character is literal, braces included.</item>
    /// <item>A doubled apostrophe is a literal apostrophe and does not toggle quoting.</item>
    /// <item>An apostrophe that toggles quoting is not itself emitted. So the H2
    /// message "...so we can't auto-create it" renders as "...so we cant auto-create
    /// it"; that is H2's behaviour, verified against the reference JVM.</item>
    /// <item>An unmatched apostrophe is closed at the end of the pattern.</item>
    /// <item>A <c>{</c> inside a placeholder opens a nested brace; if such a brace is
    /// never closed the pattern is malformed and the whole result is empty rather
    /// than an error, whereas a top-level <c>{</c> that is never closed is an error.</item>
    /// <item>There is no <c>{{</c> escape: braces are quoted with an apostrophe.</item>
    /// </list>
    /// The exhaustive behaviour is pinned down by
    /// <c>MessageFormatOracleTests.Format_MatchesReferenceJvm_Exhaustively</c>, which
    /// compares every pattern over <c>{', '{', '}', '0'}</c> up to length 4 against
    /// the reference JVM.
    /// </remarks>
    /// <param name="pattern">the message template</param>
    /// <param name="arguments">the arguments</param>
    /// <returns>the formatted message</returns>
    /// <exception cref="FormatException">if the pattern is malformed</exception>
    public static string Format(string pattern, params object?[] arguments)
    {
        SplitPattern(pattern, out string raw, out int[] offsets, out int[] argumentIndices);

        StringBuilder result = new(raw.Length + 16);
        int position = 0;
        foreach (int offset in offsets)
        {
            result.Append(raw, position, offset - position);
            position = offset;

            // argumentNumbers and offsets are parallel; find the index for this offset.
            int argumentIndex = Array.IndexOf(offsets, offset);
            int index = argumentIndices[argumentIndex];
            if (index < 0 || index >= arguments.Length)
            {
                // MessageFormat prints the placeholder itself when the argument is
                // unavailable.
                result.Append('{').Append(index).Append('}');
            }
            else
            {
                // java.text.MessageFormat renders a null argument as the text "null";
                // StringBuilder.Append(object) would render it as nothing.
                result.Append(arguments[index]?.ToString() ?? "null");
            }
        }

        result.Append(raw, position, raw.Length - position);
        return result.ToString();
    }

    /// <summary>
    /// Phase 1: splits a pattern into the raw text and the placeholder offsets.
    /// Mirrors <c>MessageFormat.applyPatternImpl</c>.
    /// </summary>
    private static void SplitPattern(
        string pattern, out string raw, out int[] offsets, out int[] argumentIndices)
    {
        StringBuilder rawBuilder = new(pattern.Length);

        // The placeholder index text, e.g. "0" in "{0}".
        StringBuilder indexBuilder = new();
        List<int> offsetList = new();
        List<int> indexList = new();

        bool inQuote = false;
        bool inPlaceholder = false;
        int braceStack = 0;
        int i = 0;

        while (i < pattern.Length)
        {
            char c = pattern[i];

            if (!inPlaceholder)
            {
                if (c == '\'')
                {
                    if (i + 1 < pattern.Length && pattern[i + 1] == '\'')
                    {
                        rawBuilder.Append('\'');
                        i++;
                    }
                    else
                    {
                        inQuote = !inQuote;
                    }
                }
                else if (c == '{' && !inQuote)
                {
                    inPlaceholder = true;
                    indexBuilder.Clear();
                }
                else
                {
                    rawBuilder.Append(c);
                }
            }
            else if (inQuote)
            {
                // Inside a quoted region of a placeholder every character is copied,
                // including braces, and the closing apostrophe ends the region.
                indexBuilder.Append(c);
                if (c == '\'')
                {
                    inQuote = false;
                }
            }
            else if (c == '\'')
            {
                // An apostrophe inside a placeholder opens a quoted region and is itself
                // kept, so a braced {0} inside it is literal text rather than a
                // placeholder.
                inQuote = true;
                indexBuilder.Append(c);
            }
            else if (c == '{')
            {
                braceStack++;
                indexBuilder.Append(c);
            }
            else if (c == '}')
            {
                if (braceStack == 0)
                {
                    offsetList.Add(rawBuilder.Length);
                    indexList.Add(ParseArgumentIndex(indexBuilder, pattern));
                    inPlaceholder = false;
                }
                else
                {
                    braceStack--;
                    indexBuilder.Append(c);
                }
            }
            else
            {
                indexBuilder.Append(c);
            }

            i++;
        }

        if (braceStack == 0 && inPlaceholder)
        {
            throw new FormatException("Unmatched braces in the pattern: " + pattern);
        }

        // A placeholder that was left with a nested brace unclosed is not an error, but
        // it is never completed either, so nothing is substituted: the raw prefix is
        // emitted as-is, which is what the JDK does by leaving maxOffset at -1.
        raw = rawBuilder.ToString();
        offsets = offsetList.ToArray();
        argumentIndices = indexList.ToArray();
    }

    private static int ParseArgumentIndex(StringBuilder indexBuilder, string pattern)
    {
        string text = indexBuilder.ToString();
        if (text.Length == 0
            || !int.TryParse(
                text, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        {
            throw new FormatException("Invalid argument index: " + text + " in pattern: " + pattern);
        }

        return index;
    }

    private static Dictionary<string, string> Load()
        => BuildCatalogue(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    /// <summary>
    /// Builds the catalogue for a given UI language: the English messages, plus a
    /// translation for that language when one is available.
    /// </summary>
    /// <param name="language">a two-letter ISO language name, or an empty string for
    /// English only</param>
    /// <returns>the message catalogue</returns>
    internal static Dictionary<string, string> BuildCatalogue(string language)
    {
        Dictionary<string, string> messages = ReadResource(EnglishResource, javaProperties: true);

        if (language.Length > 0 && language != "en")
        {
            // H2 names catalogues by language, e.g. _messages_ru.prop, and
            // org.h2.res also has region variants such as _messages_pt_br.prop.
            foreach (string candidate in new[]
            {
                ResourcePrefix + language + ".prop",
                ResourcePrefix + language.ToLowerInvariant() + ".prop",
            })
            {
                if (!HasResource(candidate))
                {
                    continue;
                }

                Dictionary<string, string> translations =
                    ReadResource(candidate, javaProperties: false);
                foreach (KeyValuePair<string, string> entry in translations)
                {
                    // message: translated message + english
                    // (otherwise certain applications don't work)
                    if (!entry.Value.StartsWith('#'))
                    {
                        string original = messages.TryGetValue(entry.Key, out string? o) ? o : "";
                        messages[entry.Key] = entry.Value + "\n" + original;
                    }
                }

                break;
            }
        }

        return messages;
    }

    private static bool HasResource(string name)
        => typeof(ErrorMessages).Assembly
            .GetManifestResourceNames()
            .Contains(name, StringComparer.Ordinal);

    private static Dictionary<string, string> ReadResource(string resourceName, bool javaProperties)
    {
        Assembly assembly = typeof(ErrorMessages).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using StreamReader reader = new(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        string content = reader.ReadToEnd();

        return javaProperties
            ? PropertiesFile.LoadJavaProperties(content)
            : PropertiesFile.LoadPlainLines(content);
    }
}
