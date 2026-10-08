/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new file for H2Sharp; implements java.util.Properties.load
 * and org.h2.util.SortedProperties.fromLines, which have no .NET equivalent).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * H2 ships its SQL error messages as Java .properties files, so the engine has to
 * read that format. Two different loaders are used by H2 and both are reproduced:
 *
 *   LoadJavaProperties  - java.util.Properties.load(Reader) for _messages_en.prop:
 *                        escape processing, '#'/'!' comments, ':' or '=' or
 *                        whitespace as key/value separator, backslash line
 *                        continuation.
 *   LoadPlainLines      - org.h2.util.SortedProperties.fromLines for translations:
 *                        a plain "key=value" split per line with no escapes.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace H2Sharp.Util;

/// <summary>
/// Readers for the Java properties format used by H2's message resources.
/// </summary>
public static class PropertiesFile
{
    /// <summary>
    /// Reads a Java properties file, reproducing
    /// <c>java.util.Properties.load(Reader)</c>.
    /// </summary>
    /// <param name="content">file content</param>
    /// <returns>the loaded key/value pairs; later duplicates win</returns>
    public static Dictionary<string, string> LoadJavaProperties(string content)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        using StringReader reader = new(content);

        while (true)
        {
            string? line = ReadLogicalLine(reader);
            if (line is null)
            {
                break;
            }

            // strip comments and blank lines
            line = line.TrimStart();
            if (line.Length == 0 || line[0] is '#' or '!')
            {
                continue;
            }

            SplitKeyValue(line, result);
        }

        return result;
    }

    /// <summary>
    /// Reproduces <c>org.h2.util.SortedProperties.fromLines</c>, which does not
    /// process escapes: every line is split at its first '='.
    /// </summary>
    /// <param name="content">file content</param>
    /// <returns>the loaded key/value pairs; later duplicates win</returns>
    public static Dictionary<string, string> LoadPlainLines(string content)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (string line in SplitLines(content))
        {
            int idx = line.IndexOf('=', StringComparison.Ordinal);
            if (idx > 0)
            {
                result[line[..idx]] = line[(idx + 1)..];
            }
        }

        return result;
    }

    /// <summary>
    /// Reads one logical line, joining continuation lines that end with an odd
    /// number of backslashes. Returns null at end of input.
    /// </summary>
    private static string? ReadLogicalLine(StringReader reader)
    {
        StringBuilder builder = new();
        bool first = true;
        while (true)
        {
            string? raw = reader.ReadLine();
            if (raw is null)
            {
                return builder.Length == 0 && first ? null : builder.ToString();
            }

            // Java skips a leading whitespace run on continuation lines.
            string line = first ? raw : raw.TrimStart(' ', '\t', '\f');
            first = false;

            int backslashes = 0;
            while (backslashes < line.Length && line[line.Length - 1 - backslashes] == '\\')
            {
                backslashes++;
            }

            if (backslashes % 2 == 1)
            {
                builder.Append(line, 0, line.Length - 1);
                continue;
            }

            builder.Append(line);
            return builder.ToString();
        }
    }

    private static void SplitKeyValue(string line, Dictionary<string, string> result)
    {
        int i = 0;
        // skip leading whitespace of the key
        while (i < line.Length && char.IsWhiteSpace(line[i]))
        {
            i++;
        }

        int keyStart = i;
        while (i < line.Length)
        {
            char c = line[i];
            if (c is '\\' or '=' or ':' || char.IsWhiteSpace(c))
            {
                break;
            }

            i++;
        }

        string rawKey = line[keyStart..i];

        // skip whitespace between key and separator
        while (i < line.Length && char.IsWhiteSpace(line[i]))
        {
            i++;
        }

        if (i < line.Length && line[i] is '=' or ':')
        {
            i++;
        }
        else if (rawKey.Length == 0)
        {
            // no key and no separator: a bare comment-ish line
            return;
        }

        while (i < line.Length && char.IsWhiteSpace(line[i]))
        {
            i++;
        }

        string rawValue = line[i..];
        result[Unescape(rawKey)] = Unescape(rawValue);
    }

    /// <summary>
    /// Processes the escape sequences Java's Properties format defines.
    /// </summary>
    public static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0)
        {
            return s;
        }

        StringBuilder builder = new(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i++];
            if (c != '\\' || i >= s.Length)
            {
                builder.Append(c);
                continue;
            }

            c = s[i++];
            switch (c)
            {
                case 't':
                    builder.Append('\t');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'u':
                    // \uXXXX
                    if (i + 4 > s.Length)
                    {
                        throw new FormatException("Malformed \\uxxxx encoding: " + s);
                    }

                    if (!ushort.TryParse(
                            s.AsSpan(i, 4),
                            System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out ushort code))
                    {
                        throw new FormatException("Malformed \\uxxxx encoding: " + s);
                    }

                    builder.Append((char)code);
                    i += 4;
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Splits on line terminators without dropping empty lines, matching the
    /// <c>StringUtils.arraySplit(s, '\n', false)</c> call H2 uses.
    /// </summary>
    private static IEnumerable<string> SplitLines(string content)
    {
        int start = 0;
        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (c == '\n')
            {
                yield return content[start..i];
                start = i + 1;
            }
            else if (c == '\r')
            {
                yield return content[start..i];
                if (i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }

                start = i + 1;
            }
        }

        yield return content[start..];
    }
}
