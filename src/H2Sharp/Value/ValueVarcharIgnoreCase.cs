/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ValueVarcharIgnoreCase.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Globalization;
using System.Text;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the VARCHAR_IGNORECASE data type.
/// </summary>
public sealed class ValueVarcharIgnoreCase : ValueStringBase
{
    /// <summary>
    /// Empty string. Should not be used in places where empty string can be treated as
    /// <c>NULL</c> depending on database mode.
    /// </summary>
    private static readonly ValueVarcharIgnoreCase Empty = new("");

    /// <summary>
    /// The hash code, cached the way the reference caches it: the fold is a pass over the
    /// whole string, and a value that probes a hash map more than once should not repeat it.
    /// </summary>
    private int hash;

    private ValueVarcharIgnoreCase(string value)
        : base(value)
    {
    }

    /// <inheritdoc/>
    public override int GetValueType() => Value.VarcharIgnorecase;

    /// <inheritdoc/>
    public override int CompareTypeSafe(Value v, CompareMode mode, CastDataProvider? provider)
        => mode.CompareString(value, TextOf((ValueStringBase)v), true);

    /// <inheritdoc/>
    /// <remarks>
    /// <c>equalsIgnoreCase</c> in Java compares UTF-16 code units one at a time through
    /// <c>Character.toUpperCase</c> and <c>toLowerCase</c>, so it is a case-folded comparison and
    /// not a collation: it is not affected by the locale, and it does not use the full case
    /// mapping, so <c>ß</c> and <c>SS</c> are not equal. Ordinal ignore-case is the same
    /// comparison, which is what the reference means; <see cref="StringComparison.CurrentCulture"/>
    /// would not be, since it collates and orders.
    /// </remarks>
    public override bool Equals(object? other)
        => other is ValueVarcharIgnoreCase v
            && string.Equals(value, v.value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    /// <remarks>
    /// The reference folds the string to upper case and hashes that, so that two values which are
    /// equal have the same hash. Upper case is taken in <c>Locale.ENGLISH</c> here and not in the
    /// default locale: the reference is locale sensitive, which is a hash that changes with the
    /// machine's locale, and a hash has to be the same on every run of the same program. Nothing
    /// needs to agree with the reference's numbers here either — as in
    /// <see cref="ValueStringBase.GetHashCode"/>, a hash code is neither persisted nor compared —
    /// so what matters is only that equal values hash equally, and English upper case gives that.
    /// </remarks>
    public override int GetHashCode()
    {
        int h = hash;
        if (h == 0)
        {
            unchecked
            {
                int classHash = GetType().GetHashCode();
                int stringHash = 0;
                foreach (char c in StringUtils.ToUpperEnglish(value))
                {
                    stringHash = (31 * stringHash) + c;
                }

                h = classHash ^ stringHash;
                hash = h;
            }
        }

        return h;
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => Dialect.AppendVarcharIgnoreCase(builder, this, sqlFlags);

    /// <summary>
    /// Get or create a VARCHAR_IGNORECASE value for the given string.
    /// The value will have the same case as the passed string.
    /// </summary>
    /// <param name="s">the string</param>
    /// <returns>the value</returns>
    public static ValueVarcharIgnoreCase Get(string s)
    {
        int length = s.Length;
        if (length == 0)
        {
            return Empty;
        }

        ValueVarcharIgnoreCase obj = new(StringUtils.Cache(s));
        if (length > SysProperties.ObjectCacheMaxPerElementSize)
        {
            return obj;
        }

        if (Cache(obj) is ValueVarcharIgnoreCase cached
            && string.Equals(cached.value, s, StringComparison.Ordinal))
        {
            // The cached object could have the wrong case: it would still be equal, but storing it
            // would quietly change the case the value renders as.
            return cached;
        }

        return obj;
    }
}
