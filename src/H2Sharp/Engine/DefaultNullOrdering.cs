/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mode.DefaultNullOrdering.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Placed in Engine rather than in a package of its own, which is where the reference keeps it.
 * The reference's package is org.h2.mode, and its other member — Mode — was ported into
 * H2Sharp.Engine before this one. A namespace H2Sharp.Mode would then be a namespace and a type
 * at the same time, and every file that says Mode would stop meaning one of them; the name of
 * the package is worth less than the ability to refer to the type.
 *
 * Where a NULL goes when the statement did not say.
 *
 * The subtlety is the difference between the first two and the last two. LOW and HIGH answer
 * "NULL is small" and "NULL is big" — which is why they *swap* when the order is descending:
 * sorting by size descending still puts the smallest thing first. FIRST and LAST say NULL is
 * always before or always after, whichever direction the order runs. That is the whole reason
 * the enum keeps two numbers per member instead of one, and a port that collapsed them into one
 * would sort descending NULLs the wrong way round — the kind of mistake that only shows up in a
 * test with a NULL and a DESC together.
 */
using H2Sharp.Result;

namespace H2Sharp.Engine;

/// <summary>
/// Default ordering of NULL values. The numbers are the ordinals of the reference's enum, and
/// they matter: a database stores this as a number and reads it back with
/// <see cref="DefaultNullOrderingRules.ValueOf"/>, so the order of the four is the contract.
/// </summary>
public enum DefaultNullOrdering
{
    /// <summary>
    /// NULL values are considered as smaller than other values during sorting.
    /// </summary>
    LOW = 0,

    /// <summary>
    /// NULL values are considered as larger than other values during sorting.
    /// </summary>
    HIGH = 1,

    /// <summary>
    /// NULL values are sorted before other values, no matter if ascending or descending order
    /// is used.
    /// </summary>
    FIRST = 2,

    /// <summary>
    /// NULL values are sorted after other values, no matter if ascending or descending order is
    /// used.
    /// </summary>
    LAST = 3,
}

/// <summary>
/// The rules of <see cref="DefaultNullOrdering"/>, which an enum cannot carry.
/// </summary>
/// <remarks>
/// The behaviour is per member and not per enum constant, so it lives here rather than on the
/// enum: C# enums cannot have fields, and adding a class per member for two numbers each would
/// have made the four cases harder to read than the switch below.
/// </remarks>
public static class DefaultNullOrderingRules
{
    /// <summary>
    /// Returns default ordering of NULL values for the specified ordinal number.
    /// </summary>
    /// <param name="ordinal">ordinal number</param>
    /// <returns>default ordering of NULL values for the specified ordinal number</returns>
    public static DefaultNullOrdering ValueOf(int ordinal)
    {
        return ordinal switch
        {
            0 => DefaultNullOrdering.LOW,
            1 => DefaultNullOrdering.HIGH,
            2 => DefaultNullOrdering.FIRST,
            3 => DefaultNullOrdering.LAST,
            _ => throw new System.ArgumentOutOfRangeException(nameof(ordinal)),
        };
    }

    /// <summary>
    /// Gets the NULLS_FIRST bit for an ascending order.
    /// </summary>
    /// <param name="ordering">the ordering</param>
    /// <returns>the bit mask</returns>
    private static int DefaultAscNulls(DefaultNullOrdering ordering)
        => ordering is DefaultNullOrdering.LOW or DefaultNullOrdering.FIRST
            ? SortOrder.NullsFirst
            : SortOrder.NullsLast;

    /// <summary>
    /// Gets the NULLS_FIRST bit for a descending order.
    /// </summary>
    /// <param name="ordering">the ordering</param>
    /// <returns>the bit mask</returns>
    private static int DefaultDescNulls(DefaultNullOrdering ordering)
        => ordering is DefaultNullOrdering.HIGH or DefaultNullOrdering.FIRST
            ? SortOrder.NullsFirst
            : SortOrder.NullsLast;

    /// <summary>
    /// Returns a sort type bit mask with <c>NULLS_FIRST</c> or <c>NULLS_LAST</c> explicitly set.
    /// </summary>
    /// <param name="ordering">the ordering to take the default from</param>
    /// <param name="sortType">sort type bit mask</param>
    /// <returns>bit mask with <c>NULLS_FIRST</c> or <c>NULLS_LAST</c> explicitly set</returns>
    public static int AddExplicitNullOrdering(DefaultNullOrdering ordering, int sortType)
    {
        if ((sortType & (SortOrder.NullsFirst | SortOrder.NullsLast)) == 0)
        {
            sortType |= (sortType & SortOrder.Descending) == 0
                ? DefaultAscNulls(ordering)
                : DefaultDescNulls(ordering);
        }

        return sortType;
    }

    /// <summary>
    /// Compares two expressions where one of them is NULL.
    /// </summary>
    /// <param name="ordering">the ordering to use</param>
    /// <param name="aNull">whether the first expression is null</param>
    /// <param name="sortType">the sort bit mask to use</param>
    /// <returns>
    /// the result of the comparison (-1 meaning the first expression should appear before the
    /// second, 0 if they are equal)
    /// </returns>
    public static int CompareNull(DefaultNullOrdering ordering, bool aNull, int sortType)
    {
        if ((sortType & SortOrder.NullsFirst) != 0)
        {
            return aNull ? -1 : 1;
        }

        if ((sortType & SortOrder.NullsLast) != 0)
        {
            return aNull ? 1 : -1;
        }

        int nullAsc = DefaultAscNulls(ordering) == SortOrder.NullsFirst ? -1 : 1;
        int nullDesc = DefaultDescNulls(ordering) == SortOrder.NullsFirst ? -1 : 1;
        return (sortType & SortOrder.Descending) == 0
            ? aNull ? nullAsc : -nullAsc
            : aNull ? nullDesc : -nullDesc;
    }
}
