/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.CompareModeDefault.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Only the branch of this class that can be exact is here. The reference takes
 * any java.text.Collator — a locale collator for DEFAULT_ names, an ICU4J one
 * for ICU4J_ names, a CharsetCollator for CHARSET_ names — and there is no .NET
 * counterpart for the first two: the platform's collation weights disagree with
 * the reference's on 31 per cent of a measured grid (see CompareMode), so a
 * locale-backed port would produce plausible wrong answers. Names that do not
 * start with CHARSET_ therefore fail with that measured reason, the same way
 * CompareMode.GetInstance fails for them.
 *
 * What is here is the whole CHARSET_ branch: strength handed straight through
 * (H2's own off-by-one included — its SECONDARY arrives as the JDK's TERTIARY,
 * which is why the ignore-case delegation below triggers at H2 SECONDARY too),
 * the SECONDARY copy for case-insensitive comparison at higher strengths, and
 * the SmallLRUCache of collation keys keyed exactly like the reference.
 */
using System;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// The default implementation of CompareMode, over a charset collator.
/// </summary>
public class CompareModeDefault : CompareMode
{
    private readonly CharsetCollator collator;
    private readonly SmallLRUCache<string, CharsetCollator.CharsetCollationKey>? collationKeys;

    private volatile CompareModeDefault? caseInsensitive;

    /// <summary>
    /// Creates a compare mode for a CHARSET_ collation name.
    /// </summary>
    /// <param name="name">the collation name, starting with CHARSET_</param>
    /// <param name="strength">the collation strength</param>
    internal CompareModeDefault(string name, int strength)
        : base(name, strength)
    {
        // CompareMode.getCollator, CHARSET_ branch only. The locale and ICU4J
        // branches need java.text.Collator weights and stay unported.
        if (!name.StartsWith(Charset, StringComparison.Ordinal))
        {
            throw DbException.GetInternalError(name);
        }

        collator = new CharsetCollator(name.Substring(Charset.Length));
        collator.Strength = strength;
        int cacheSize = SysProperties.CollatorCacheSize;
        if (cacheSize != 0)
        {
            collationKeys = SmallLRUCache<string, CharsetCollator.CharsetCollationKey>.NewInstance(cacheSize);
        }
    }

    /// <inheritdoc/>
    public override int CompareString(string a, string b, bool ignoreCase)
    {
        if (ignoreCase && GetStrength() > CharsetCollator.Secondary)
        {
            CompareModeDefault? i = caseInsensitive;
            if (i is null)
            {
                i = new CompareModeDefault(GetName(), CharsetCollator.Secondary);
                caseInsensitive = i;
            }

            return i.CompareString(a, b, false);
        }

        int comp;
        if (collationKeys is not null)
        {
            CharsetCollator.CharsetCollationKey aKey = GetKey(a);
            CharsetCollator.CharsetCollationKey bKey = GetKey(b);
            comp = aKey.CompareTo(bKey);
        }
        else
        {
            comp = collator.Compare(a, b);
        }

        return comp;
    }

    /// <inheritdoc/>
    public override bool EqualsChars(string a, int ai, string b, int bi, bool ignoreCase)
    {
        return CompareString(a.Substring(ai, 1), b.Substring(bi, 1), ignoreCase) == 0;
    }

    private CharsetCollator.CharsetCollationKey GetKey(string a)
    {
        SmallLRUCache<string, CharsetCollator.CharsetCollationKey> keys = collationKeys!;
        lock (keys)
        {
            if (!keys.TryGetValue(a, out CharsetCollator.CharsetCollationKey? key))
            {
                key = collator.GetCollationKey(a);
                keys.Put(a, key);
            }

            return key;
        }
    }
}
