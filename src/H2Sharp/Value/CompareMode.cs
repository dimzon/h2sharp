/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.CompareMode.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Only the part of CompareMode that does not need a collation.
 *
 * The reference resolves a collation name to a java.text.Collator, and that step is not
 * ported, deliberately and with a measurement behind it: over 7560 comparisons between
 * java.text.Collator and CultureInfo.CompareInfo, 2332 disagree, including the case order
 * itself inverting at TERTIARY for en_US. Those weights are the JRE's, so icu4n would not
 * reproduce them either, and a collated ORDER BY is the only thing that would notice.
 *
 * What is here is exact and complete for the default: a compare mode is a name and a
 * strength, two are equal when those match, and with COLLATION OFF, which is the default,
 * the comparison is String.compareTo, which StringUtils.CompareJava reproduces. A mode that asks
 * for a real collator fails with that reason rather than quietly comparing ordinally, since
 * quietly comparing ordinally would produce a plausible wrong answer.
 *
 * CharsetCollator is not here either, though it would be exact: it compares bytes in a named
 * encoding and involves no platform weights. It arrives with the string value type, which is
 * its only user.
 */
using System;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Instances of this class can compare strings. Case sensitive and case insensitive
/// comparison is supported, and comparison using a collator.
/// </summary>
public class CompareMode
{
    /// <summary>
    /// This constant means there is no collator set, and the default string
    /// comparison is to be used.
    /// </summary>
    public const string Off = "OFF";

    /// <summary>
    /// This constant means the default collator should be used, even if ICU4J is
    /// in the classpath.
    /// </summary>
    public const string Default = "DEFAULT_";

    /// <summary>
    /// This constant means ICU4J should be used (this will fail if it is not in
    /// the classpath).
    /// </summary>
    public const string Icu4J = "ICU4J_";

    /// <summary>
    /// This constant means the charset specified should be used.
    /// This will fail if the specified charset does not exist.
    /// </summary>
    public const string Charset = "CHARSET_";

    /// <summary>
    /// This constant means the collation is not case sensitive and does not
    /// distinguish accents.
    /// </summary>
    public const int Primary = 1;

    /// <summary>
    /// This constant means the collation distinguishes accents but is not case
    /// sensitive.
    /// </summary>
    public const int Secondary = 2;

    /// <summary>
    /// This constant means the collation distinguishes accents and case.
    /// </summary>
    public const int Tertiary = 3;

    private readonly string? name;
    private readonly int strength;

    /// <summary>
    /// Creates a compare mode.
    /// </summary>
    /// <param name="name">the collation name, or <see langword="null"/> for none</param>
    /// <param name="strength">the collation strength</param>
    protected CompareMode(string? name, int strength)
    {
        this.name = name;
        this.strength = strength;
    }

    /// <summary>
    /// Create a new compare mode with the given collation name and strength.
    /// </summary>
    /// <remarks>
    /// The reference caches the last mode it built and hands the same object back for the
    /// same arguments. That is an allocation saving, and its observable effect is reference
    /// identity, which nothing in the port relies on, so it is not reproduced.
    /// </remarks>
    /// <param name="name">the collation name, or <see langword="null"/> for none</param>
    /// <param name="strength">the collation strength</param>
    /// <returns>the compare mode</returns>
    public static CompareMode GetInstance(string? name, int strength)
    {
        if (name is null || name == Off)
        {
            return new CompareMode(name, strength);
        }

        throw new NotSupportedException(
            $"The collation '{name}' needs a collator, which is not ported: the platform's "
            + "collation weights disagree with the reference's on 31 per cent of a measured "
            + "grid, including the order of upper and lower case. With COLLATION OFF, which "
            + "is the default, comparison is exact. See MIGRATION_STATUS.md.")
        {
        };
    }

    /// <summary>
    /// Get the collation name.
    /// </summary>
    /// <returns>the name of the collation</returns>
    public string GetName() => name ?? Off;

    /// <summary>
    /// Get the collation strength.
    /// </summary>
    /// <returns>the strength</returns>
    public int GetStrength() => strength;

    /// <summary>
    /// Compare two characters in a string.
    /// </summary>
    /// <param name="a">the first string</param>
    /// <param name="ai">the character index in the first string</param>
    /// <param name="b">the second string</param>
    /// <param name="bi">the character index in the second string</param>
    /// <param name="ignoreCase">true if a case-insensitive comparison should be made</param>
    /// <returns>true if the characters are equal</returns>
    public virtual bool EqualsChars(string a, int ai, string b, int bi, bool ignoreCase)
    {
        char ca = a[ai];
        char cb = b[bi];
        if (ca == cb)
        {
            return true;
        }

        if (ignoreCase)
        {
            if (char.ToUpperInvariant(ca) == char.ToUpperInvariant(cb)
                || char.ToLowerInvariant(ca) == char.ToLowerInvariant(cb))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Compare two strings.
    /// </summary>
    /// <param name="a">the first string</param>
    /// <param name="b">the second string</param>
    /// <param name="ignoreCase">true if a case-insensitive comparison should be made</param>
    /// <returns>
    /// -1 if the first string is 'smaller', 1 if the second string is smaller, and 0 if they
    /// are equal
    /// </returns>
    public virtual int CompareString(string a, string b, bool ignoreCase)
    {
        if (ignoreCase)
        {
            // String.compareToIgnoreCase folds with both cases, which ToUpperEnglish reproduces
            // exactly, and the reference takes this same path when the collator's strength is
            // below SECONDARY. The magnitude is the same difference of code units.
            return StringUtils.CompareJava(StringUtils.ToUpperEnglish(a), StringUtils.ToUpperEnglish(b));
        }

        return StringUtils.CompareJava(a, b);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj is not CompareMode other)
        {
            return false;
        }

        return GetName() == other.GetName() && strength == other.strength;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // The reference's formula, with Java's wrapping overflow. Unlike the type hash codes,
        // // a compare mode is put into a hash map, so the number does have to be stable; it
        // does not have to equal Java's, for the same reason, and the test says which.
        unchecked
        {
            int result = 1;
            result = (31 * result) + JavaHashCode(GetName());
            return (31 * result) + strength;
        }
    }

    private static int JavaHashCode(string? s)
    {
        if (s is null)
        {
            return 0;
        }

        unchecked
        {
            int hash = 0;
            foreach (char c in s)
            {
                hash = (31 * hash) + c;
            }

            return hash;
        }
    }
}
