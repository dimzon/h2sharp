/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueVarchar.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the CHARACTER VARYING data type.
/// </summary>
public sealed class ValueVarchar : ValueStringBase
{
    /// <summary>
    /// Empty string. Should not be used in places where empty string can be
    /// treated as <c>NULL</c> depending on database mode.
    /// </summary>
    public static readonly ValueVarchar Empty = new("");

    private ValueVarchar(string value)
        : base(value)
    {
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => StringUtils.QuoteStringSql(builder, value);

    /// <inheritdoc/>
    public override int GetValueType() => Value.Varchar;

    /// <summary>
    /// Get or create a VARCHAR value for the given string.
    /// </summary>
    /// <param name="s">the string</param>
    /// <returns>the value</returns>
    public static Value Get(string s) => Get(s, null);

    /// <summary>
    /// Get or create a VARCHAR value for the given string.
    /// </summary>
    /// <remarks>
    /// The empty string becomes NULL when the database is in a mode that treats empty strings as
    /// null, and that decision lives in the database mode, which is not ported. The overload
    /// without a provider is the one the tokenizer and the literals use, and it is exact.
    /// </remarks>
    /// <param name="s">the string</param>
    /// <param name="provider">the cast information provider, or <see langword="null"/></param>
    /// <returns>the value</returns>
    /// <exception cref="NotSupportedException">
    /// if a provider is given and the empty string would have to be decided against the
    /// database mode
    /// </exception>
    public static Value Get(string s, CastDataProvider? provider)
    {
        if (s.Length == 0)
        {
            if (provider is not null)
            {
                throw new NotSupportedException(
                    "Deciding whether an empty string is NULL needs the database mode, which is "
                    + "not ported yet. See MIGRATION_STATUS.md.");
            }

            return Empty;
        }

        ValueVarchar obj = new(StringUtils.Cache(s));
        if (s.Length > SysProperties.ObjectCacheMaxPerElementSize)
        {
            return obj;
        }

        return Cache(obj);
    }
}
