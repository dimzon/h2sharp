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
 * for a real collator is created (the reference answers the same instance for equal
 * arguments) but fails with that reason the moment it is used, rather than quietly comparing
 * ordinally, since quietly comparing ordinally would produce a plausible wrong answer.
 *
 * CharsetCollator is carried as its own class, and GetInstance wires the
 * CHARSET_ branch through CompareModeDefault: a charset collator compares
 * encoded bytes and involves no platform weights, so that branch is exact.
 * The locale and ICU4J branches stay unported for the measured reason above.
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

    /// <summary>
    /// This constant means the collation treats every difference as significant. The
    /// reference spells it Collator.IDENTICAL, the Java collator's own strength zero.
    /// </summary>
    public const int Identical = 0;

    private readonly string? name;
    private readonly int strength;

    /// <summary>
    /// The last mode built, handed back when the arguments match. A benign race: two threads
    /// may build the same mode twice, and both answers are equal, as in the reference.
    /// </summary>
    private static volatile CompareMode? lastUsed;

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
    /// If possible the last built mode is returned: same arguments, same object. That is an
    /// allocation saving, and its observable effect is reference identity, which nothing in
    /// the port compares on.
    /// </remarks>
    /// <param name="name">the collation name, or <see langword="null"/> for none</param>
    /// <param name="strength">the collation strength</param>
    /// <returns>the compare mode</returns>
    public static CompareMode GetInstance(string? name, int strength)
    {
        CompareMode? last = lastUsed;
        if (last is not null && last.name == name && last.strength == strength)
        {
            return last;
        }

        if (name is null || name == Off)
        {
            last = new CompareMode(name, strength);
        }
        else if (name.StartsWith(Charset, StringComparison.Ordinal))
        {
            last = new CompareModeDefault(name, strength);
        }
        else
        {
            // The collator itself is still not ported (D-002): creating a mode must not
            // fail, because the reference answers the same instance for equal arguments, but
            // using it to compare must, rather than quietly comparing ordinally.
            last = new UnsupportedCollation(name, strength);
        }

        lastUsed = last;
        return last;
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
            // The reference hands this straight to String.compareToIgnoreCase, which folds case per
            // code unit and answers with the difference of the lower-cased pair. Folding to upper case
            // and subtracting gives the same sign and a different number, which is observable, so this
            // is the JDK's own two-step walk rather than a string comparison dressed up as one.
            return StringUtils.CompareJavaIgnoreCase(a, b);
        }

        return StringUtils.CompareJava(a, b);
    }

    /// <summary>
    /// A named collation that exists but cannot compare: the platform's collation weights
    /// disagree with the reference's (D-002), so using it fails with the measured reason
    /// instead of producing a plausible wrong answer.
    /// </summary>
    private sealed class UnsupportedCollation : CompareMode
    {
        internal UnsupportedCollation(string name, int strength)
            : base(name, strength)
        {
        }

        /// <inheritdoc/>
        public override int CompareString(string a, string b, bool ignoreCase)
            => throw NotSupported();

        /// <inheritdoc/>
        public override bool EqualsChars(string a, int ai, string b, int bi, bool ignoreCase)
            => throw NotSupported();

        private static NotSupportedException NotSupported()
            => new("The collation needs a collator, which is not ported: the platform's "
                + "collation weights disagree with the reference's on 31 per cent of a measured "
                + "grid, including the order of upper and lower case. With COLLATION OFF, which "
                + "is the default, comparison is exact. See MIGRATION_STATUS.md.");
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
            result = (31 * result) + Utils.JavaHashCode(GetName());
            return (31 * result) + strength;
        }
    }
}
