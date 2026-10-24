/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueChar.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Api;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the CHARACTER data type.
/// </summary>
public sealed class ValueChar : ValueStringBase
{
    private ValueChar(string s)
        : base(s)
    {
    }

    /// <inheritdoc/>
    public override int GetValueType() => Value.Char;

    /// <inheritdoc/>
    /// <remarks>
    /// The reference compares the two values after converting both of them to CHAR, and that
    /// conversion drops trailing spaces: it is the padding of a fixed length type, and SQL says
    /// that <c>'a'</c> and <c>'a '</c> are the same CHAR. So the comparison is not a plain string
    /// comparison and neither side is taken as written. The conversion is also what makes this
    /// correct when the other side is a longer string, since a CHAR compares against that as CHAR.
    /// </remarks>
    public override int CompareTypeSafe(Value v, CompareMode mode, CastDataProvider? provider)
        => mode.CompareString(TrimPadding(value), TrimPadding(v.GetString() ?? string.Empty), false);

    /// <inheritdoc/>
    /// <remarks>
    /// A quoted string is a VARCHAR, so a CHAR says its length out loud in a CAST, and the length
    /// is at least 1: a CHAR of length 0 is spelled CHAR(1). NO_CASTS drops the wrapper for the
    /// caller that already knows the type.
    /// </remarks>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => Dialect.AppendChar(builder, this, sqlFlags);

    /// <summary>
    /// Get or create a CHAR value for the given string.
    /// </summary>
    /// <remarks>
    /// There is no empty value of its own, unlike the varying type: an empty CHAR is a value with
    /// a length, and the two are not the same thing.
    /// </remarks>
    /// <param name="s">the string</param>
    /// <returns>the value</returns>
    public static ValueChar Get(string s)
    {
        ValueChar obj = new(StringUtils.Cache(s));
        if (s.Length > SysProperties.ObjectCacheMaxPerElementSize)
        {
            return obj;
        }

        return (ValueChar)Value.Cache(obj)!;
    }

    /// <summary>
    /// Drops the padding of a CHAR value, which is what the reference's conversion to CHAR does
    /// before the value is compared or converted.
    /// </summary>
    /// <param name="s">the text</param>
    /// <returns>the text without its trailing spaces</returns>
    private static string TrimPadding(string s)
    {
        int end = s.Length;
        while (end > 0 && s[end - 1] == ' ')
        {
            end--;
        }

        return end == s.Length ? s : s[..end];
    }
}
