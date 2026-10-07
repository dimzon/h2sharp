/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.util.StringUtils (part 1 of 2).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: PARTIAL PORT. Only the members needed by already-ported code are here so
 * far; the rest of org.h2.util.StringUtils (1414 lines) follows in the same
 * partial. Do not treat this type as complete - see docs/ROADMAP.md.
 */
using System;
using System.Text;

namespace H2Sharp.Util;

/// <summary>
/// A few String-related utility functions.
/// </summary>
public static partial class StringUtils
{
    /// <summary>
    /// Add an asterisk ('[*]') at the given position. This format is used to
    /// show where parsing failed in a statement.
    /// </summary>
    /// <param name="s">the text</param>
    /// <param name="index">the position</param>
    /// <returns>the text with asterisk</returns>
    public static string? AddAsterisk(string? s, int index)
    {
        if (s is not null)
        {
            int len = s.Length;
            index = Math.Min(index, len);
            return new StringBuilder(len + 3).Append(s, 0, index).Append("[*]").Append(s, index, len).ToString();
        }

        return s;
    }
}