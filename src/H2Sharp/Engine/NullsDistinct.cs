/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.engine.NullsDistinct.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Util;

namespace H2Sharp.Engine;

/// <summary>
/// Determines how rows with NULL values in indexed columns are handled
/// in unique indexes, unique constraints, or by unique predicate.
/// </summary>
public enum NullsDistinct : byte
{
    /// <summary>
    /// NULL values of columns are distinct.
    /// </summary>
    Distinct,

    /// <summary>
    /// NULL values of columns are distinct only if all columns have null values.
    /// </summary>
    AllDistinct,

    /// <summary>
    /// NULL values of columns are never distinct.
    /// </summary>
    NotDistinct,
}

/// <summary>
/// The SQL rendering of <see cref="NullsDistinct"/>, which in the reference is the enum itself
/// implementing HasSQL.
/// </summary>
public static class NullsDistinctSql
{
    /// <summary>
    /// Constructs the SQL for this nulls-distinct treatment.
    /// </summary>
    /// <remarks>
    /// A method on the enum in the reference, so the words are uppercase there and are
    /// uppercase here; the port names the enum members in PascalCase per the project's own
    /// rule, and this is where the two spellings meet.
    /// </remarks>
    /// <param name="builder">the builder</param>
    /// <param name="nullsDistinct">the treatment</param>
    /// <param name="sqlFlags">the SQL flags</param>
    /// <returns>the builder</returns>
    public static StringBuilder GetSql(StringBuilder builder, NullsDistinct nullsDistinct, int sqlFlags)
    {
        _ = builder.Append("NULLS ");
        switch (nullsDistinct)
        {
            case NullsDistinct.Distinct:
                _ = builder.Append("DISTINCT");
                break;
            case NullsDistinct.AllDistinct:
                _ = builder.Append("ALL DISTINCT");
                break;
            case NullsDistinct.NotDistinct:
                _ = builder.Append("NOT DISTINCT");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(nullsDistinct));
        }

        return builder;
    }
}
