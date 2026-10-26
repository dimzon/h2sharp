/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * Named parameters to indexed ones.
 *
 * The ADO.NET layer names a parameter "$name"; the engine understands only "?", "?N" and "$N".
 * The rewrite turns every "$name" into "$N" before the engine sees the text, so the parser and
 * the Prepared statement stay the reference's code and the name never reaches them.
 *
 * The scan is a token scan, not a text replacement. "$" is allowed inside an H2 identifier
 * ("a$b"), inside a string, a quoted name or a comment, and replacing it there would change the
 * statement. The tokenizer already knows where a parameter really begins, so this walks its
 * tokens and reads the name from the original text, preserving its case.
 *
 * Names are case-sensitive: "$id" and "$Id" are different parameters. A name that has no
 * parameter is an error rather than a silently unused placeholder.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Util;

namespace H2Sharp.Data;

/// <summary>
/// Rewrites named parameters to indexed ones.
/// </summary>
internal static class H2ParameterRewriter
{
    /// <summary>
    /// Rewrites the text and orders the values the placeholders take.
    /// </summary>
    /// <param name="text">the command text</param>
    /// <param name="parameters">the command parameters, by name</param>
    /// <param name="provider">where the mode comes from</param>
    /// <returns>the rewritten text and the values in placeholder order</returns>
    internal static (string Text, List<H2Parameter> Ordered) Rewrite(
        string text, H2ParameterCollection parameters, CastDataProvider provider)
    {
        if (text.IndexOf('$') < 0)
        {
            if (text.IndexOf('?') < 0)
            {
                return (text, []);
            }

            List<H2Parameter> positional = [];
            foreach (H2Parameter parameter in parameters)
            {
                positional.Add(parameter);
            }

            return (text, positional);
        }

        Tokenizer tokenizer = new(
            provider, identifiersToUpper: true, identifiersToLower: false, nonKeywords: null);
        List<Token> tokens = tokenizer.Tokenize(text, false, new BitSet());
        Mode mode = provider.GetMode();

        List<(int Start, int End, int Index)> replacements = [];
        Dictionary<string, int> indexes = new(StringComparer.Ordinal);
        List<H2Parameter> namedParameters = [];
        bool hasNamed = false;
        bool hasAnonymous = false;
        bool hasExplicitIndexed = false;
        bool hasParameter = false;

        foreach (Token token in tokens)
        {
            if (token is not Token.ParameterToken parameter)
            {
                continue;
            }

            hasParameter = true;
            int start = parameter.Start;
            char marker = text[start];

            if (marker == '?')
            {
                if (start + 1 < text.Length && text[start + 1] is >= '0' and <= '9')
                {
                    hasExplicitIndexed = true;
                }
                else
                {
                    hasAnonymous = true;
                }

                continue;
            }

            if (marker != '$')
            {
                continue;
            }

            int nameStart = start + 1;
            if (nameStart < text.Length && text[nameStart] is >= '0' and <= '9')
            {
                hasExplicitIndexed = true;
                continue;
            }

            if (nameStart >= text.Length)
            {
                throw new InvalidOperationException(
                    "A '$' parameter must have a name or an index");
            }

            int codePoint = JavaCharacter.IsHighSurrogate(text[nameStart])
                ? JavaCharacter.CodePointAt(text, nameStart)
                : text[nameStart];
            if (!JavaCharacter.IsJavaIdentifierStart(codePoint))
            {
                throw new InvalidOperationException(
                    "A '$' parameter must have a name or an index");
            }

            int nameEnd = ReadNameEnd(text, nameStart, mode);
            string name = text[nameStart..nameEnd];
            int parameterIndex = parameters.IndexOf(name);
            if (parameterIndex < 0)
            {
                throw new InvalidOperationException(
                    "No value was given for parameter '$" + name + "'");
            }

            if (!indexes.TryGetValue(name, out int index))
            {
                index = indexes.Count + 1;
                indexes.Add(name, index);
                namedParameters.Add((H2Parameter)parameters[parameterIndex]);
            }

            replacements.Add((start, nameEnd, index));
            hasNamed = true;
        }

        if (hasNamed && (hasAnonymous || hasExplicitIndexed))
        {
            throw new InvalidOperationException(
                "Named and positional parameters cannot be mixed in one command");
        }

        if (hasNamed)
        {
            return (Apply(text, replacements), namedParameters);
        }

        List<H2Parameter> ordered = [];
        if (hasParameter)
        {
            foreach (H2Parameter parameter in parameters)
            {
                ordered.Add(parameter);
            }
        }

        return (text, ordered);
    }

    private static int ReadNameEnd(string text, int start, Mode mode)
    {
        bool pound = mode.SupportPoundSymbolForColumnNames;
        int i = start;
        while (i < text.Length)
        {
            char c = text[i];
            int codePoint = JavaCharacter.IsHighSurrogate(c)
                ? JavaCharacter.CodePointAt(text, i)
                : c;
            if (!JavaCharacter.IsJavaIdentifierPart(codePoint) && !(pound && codePoint == '#'))
            {
                break;
            }

            i += JavaCharacter.CharCount(codePoint);
        }

        return i;
    }

    private static string Apply(string text, List<(int Start, int End, int Index)> replacements)
    {
        StringBuilder builder = new(text.Length);
        int position = 0;
        foreach ((int start, int end, int index) in replacements)
        {
            _ = builder.Append(text, position, start - position);
            _ = builder.Append('$').Append(index);
            position = end;
        }

        _ = builder.Append(text, position, text.Length - position);
        return builder.ToString();
    }
}
