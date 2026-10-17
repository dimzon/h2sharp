/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * Named parameters to positional ones.
 *
 * The engine only knows "?" with an index, and H2 itself rewrites names to positions at
 * parse time — a named parameter that reached the engine would be a name the database no
 * longer has. So the provider does the rewriting the reference does: "@Id" becomes "?",
 * and the values travel beside the text in the order the placeholders appear. A name used
 * twice binds twice, to the same value, which is what the text means.
 *
 * The scan skips string literals, quoted identifiers and both comment shapes, because a
 * "@Id" inside any of them is text, not a placeholder — rewriting it would change the
 * question while looking like it answered it. Mixing "?" with names is refused for the
 * same reason: the positions would no longer mean what either side counts.
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace H2Sharp.Data;

/// <summary>
/// Rewrites named parameters to positional ones.
/// </summary>
internal static class H2ParameterRewriter
{
    /// <summary>
    /// Rewrites the text and orders the values the placeholders take.
    /// </summary>
    /// <param name="text">the command text</param>
    /// <param name="parameters">the command parameters, by name</param>
    /// <returns>the rewritten text and the values in placeholder order</returns>
    private static readonly System.Buffers.SearchValues<char> Interesting =
        System.Buffers.SearchValues.Create(['\'', '"', '-', '/', '?', '@', ':']);

    internal static (string Text, List<H2Parameter> Ordered) Rewrite(
        string text, H2ParameterCollection parameters)
    {
        // No names, no colons, no question marks outside the fast check below: the text
        // goes through untouched, without a builder or a copy. The scan it costs is the
        // price of not lexing, and it is paid once per execution either way.
        if (text.AsSpan().IndexOfAny(Interesting) < 0)
        {
            return (text, []);
        }

        List<H2Parameter> ordered = [];
        StringBuilder builder = new(text.Length);
        bool sawPositional = false;
        bool sawName = false;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\'')
            {
                i = CopyQuoted(builder, text, i, '\'');
            }
            else if (c == '"')
            {
                i = CopyQuoted(builder, text, i, '"');
            }
            else if (c == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                i = CopyLineComment(builder, text, i);
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i = CopyBlockComment(builder, text, i);
            }
            else if (c == '?')
            {
                sawPositional = true;
                _ = builder.Append(c);
                i++;
            }
            else if ((c == '@' || c == ':') && i + 1 < text.Length && IsNameStart(text[i + 1]))
            {
                int start = i + 1;
                int end = start;
                while (end < text.Length && IsNamePart(text[end]))
                {
                    end++;
                }

                string name = text[start..end];
                int index = parameters.IndexOf(name);
                if (index < 0)
                {
                    throw new InvalidOperationException(
                        "No value was given for parameter '" + name + "'");
                }

                sawName = true;
                ordered.Add((H2Parameter)parameters[index]);
                _ = builder.Append('?');
                i = end;
            }
            else
            {
                _ = builder.Append(c);
                i++;
            }
        }

        if (!sawName)
        {
            // No placeholder was a name, so the walk reproduced the text character for
            // character: a purely positional text, or one whose quotes and comments merely
            // passed through. Handing back the original keeps that identity instead of
            // paying for a builder and a copy on every execution.
            if (sawPositional)
            {
                // Plain "?" with no names: the values travel in collection order, which is
                // the order the placeholders appear in. Mixed with names it would be
                // neither, and that case is refused below, because a text with names always
                // fills the list.
                foreach (H2Parameter parameter in parameters)
                {
                    ordered.Add(parameter);
                }
            }

            return (text, ordered);
        }

        if (sawPositional)
        {
            throw new InvalidOperationException(
                "Named and positional parameters cannot be mixed in one command");
        }

        return (builder.ToString(), ordered);
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    private static int CopyQuoted(StringBuilder builder, string text, int i, char quote)
    {
        _ = builder.Append(quote);
        i++;
        while (i < text.Length)
        {
            char c = text[i];
            _ = builder.Append(c);
            i++;
            if (c == quote)
            {
                if (i < text.Length && text[i] == quote)
                {
                    _ = builder.Append(quote);
                    i++;
                }
                else
                {
                    break;
                }
            }
        }

        return i;
    }

    private static int CopyLineComment(StringBuilder builder, string text, int i)
    {
        while (i < text.Length && text[i] != '\n')
        {
            _ = builder.Append(text[i]);
            i++;
        }

        return i;
    }

    private static int CopyBlockComment(StringBuilder builder, string text, int i)
    {
        _ = builder.Append("/*");
        i += 2;
        while (i < text.Length)
        {
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/')
            {
                _ = builder.Append("*/");
                return i + 2;
            }

            _ = builder.Append(text[i]);
            i++;
        }

        return i;
    }
}
