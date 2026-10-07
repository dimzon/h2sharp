/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.util.ParserUtil (the methods; the constants live in the generated
 * ParserUtil.cs).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: hand written, one method per Java method, no logic changes. Kept in a partial
 * so the generated half stays purely mechanical.
 */
using System;
using System.Text;

namespace H2Sharp.Util;

/// <summary>
/// Token constants and keyword lookup.
/// </summary>
public static partial class ParserUtil
{
    /// <summary>
    /// The longest keyword in the table, used as a cheap length filter before the lookup.
    /// "CURRENT_TIMESTAMP" is 17 characters.
    /// </summary>
    private const int MaxKeywordLength = 17;

    /// <summary>
    /// Add double quotes around an identifier if required and append it to the specified
    /// string builder.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="s">the identifier; null is rendered as an empty quoted identifier</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the specified builder</returns>
    public static StringBuilder QuoteIdentifier(StringBuilder builder, string? s, int sqlFlags)
    {
        if (s is null)
        {
            return builder.Append("\"\"");
        }

        if ((sqlFlags & HasSql.QuoteOnlyWhenRequired) != 0 && IsSimpleIdentifier(s, false, false))
        {
            return builder.Append(s);
        }

        return StringUtils.QuoteIdentifier(builder, s);
    }

    /// <summary>
    /// Checks if this string is a SQL keyword.
    /// </summary>
    /// <param name="s">the token to check</param>
    /// <param name="ignoreCase">true if case should be ignored, false if only upper case
    /// tokens are detected as keywords</param>
    /// <returns>true if it is a keyword</returns>
    public static bool IsKeyword(string s, bool ignoreCase)
        => GetTokenType(s, ignoreCase, false) != Identifier;

    /// <summary>
    /// Is this a simple identifier (in the JDBC specification sense).
    /// </summary>
    /// <param name="s">identifier to check</param>
    /// <param name="databaseToUpper">whether unquoted identifiers are converted to upper
    /// case</param>
    /// <param name="databaseToLower">whether unquoted identifiers are converted to lower
    /// case</param>
    /// <returns>whether the identifier may be used without quotes</returns>
    /// <exception cref="ArgumentException">if both flags are set, which is what Java's
    /// <c>IllegalArgumentException</c> maps to</exception>
    public static bool IsSimpleIdentifier(string s, bool databaseToUpper, bool databaseToLower)
    {
        if (databaseToUpper && databaseToLower)
        {
            throw new ArgumentException("databaseToUpper && databaseToLower");
        }

        int length = s.Length;
        if (length == 0 || !CheckLetter(databaseToUpper, databaseToLower, s[0]))
        {
            return false;
        }

        for (int i = 1; i < length; i++)
        {
            char c = s[i];
            if (c != '_' && (c < '0' || c > '9') && !CheckLetter(databaseToUpper, databaseToLower, c))
            {
                return false;
            }
        }

        return GetTokenType(s, !databaseToUpper, true) == Identifier;
    }

    private static bool CheckLetter(bool databaseToUpper, bool databaseToLower, char c)
    {
        if (databaseToUpper)
        {
            if (c < 'A' || c > 'Z')
            {
                return false;
            }
        }
        else if (databaseToLower)
        {
            if (c < 'a' || c > 'z')
            {
                return false;
            }
        }
        else if ((c < 'A' || c > 'Z') && (c < 'a' || c > 'z'))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Get the token type.
    /// </summary>
    /// <param name="s">the string with token</param>
    /// <param name="ignoreCase">true if case should be ignored, false if only upper case
    /// tokens are detected as keywords</param>
    /// <param name="additionalKeywords">whether context-sensitive keywords are returned as
    /// <see cref="Keyword"/></param>
    /// <returns>the token type</returns>
    public static int GetTokenType(string s, bool ignoreCase, bool additionalKeywords)
    {
        int length = s.Length;

        // The length filter is not an optimisation: a keyword outside this range cannot be
        // in the table, so it saves the uppercase mapping and the hash on every ordinary
        // identifier.
        if (length <= 1 || length > MaxKeywordLength)
        {
            return Identifier;
        }

        if (ignoreCase)
        {
            s = StringUtils.ToUpperEnglish(s);
        }

        if (!Keywords.TryGetValue(s, out int type))
        {
            return Identifier;
        }

        return type == Keyword && !additionalKeywords ? Identifier : type;
    }
}
