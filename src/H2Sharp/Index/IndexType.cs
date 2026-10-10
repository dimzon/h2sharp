/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.index.IndexType.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Two things in here are decisions rather than transliteration, and both are visible in the
 * SQL.
 *
 * The unique flag is a field in the reference and a question here: isUnique() is
 * "primaryKey || nullsDistinct != null", so a unique index is one that was created unique, and
 * a non-unique index has no nulls-distinct treatment at all. That is why NullsDistinct is null
 * for non-unique indexes and NOT_DISTINCT for primary keys: getEffectiveNullsDistinct() adds the
 * latter, so the two questions ("was it declared unique" and "how are nulls treated") stay
 * separable instead of collapsing into one flag.
 *
 * createUnique folds a single-column ALL_DISTINCT down to DISTINCT: with one column the two only
 * differ when every column is null, which with one column means the whole key is null, and H2
 * decided that is the plain DISTINCT case. It is the one place where a factory quietly rewrites
 * its argument, and it would be invisible if the SQL did not print it — so the SQL is what the
 * differential test asks about.
 */
using System;
using System.Text;
using H2Sharp.Engine;

namespace H2Sharp.Index;

/// <summary>
/// Represents information about the properties of an index.
/// </summary>
public class IndexType
{
    private bool primaryKey;
    private bool persistent;
    private bool hash;
    private bool scan;
    private bool spatial;
    private bool belongsToConstraint;
    private NullsDistinct? nullsDistinct;

    /// <summary>
    /// Create a primary key index.
    /// </summary>
    /// <param name="persistent">if the index is persistent</param>
    /// <param name="hash">if a hash index should be used</param>
    /// <returns>the index type</returns>
    public static IndexType CreatePrimaryKey(bool persistent, bool hash)
    {
        IndexType type = new()
        {
            primaryKey = true,
            persistent = persistent,
            hash = hash,
        };
        return type;
    }

    /// <summary>
    /// Create a unique index.
    /// </summary>
    /// <param name="persistent">if the index is persistent</param>
    /// <param name="hash">if a hash index should be used</param>
    /// <param name="uniqueColumnCount">count of unique columns (not stored)</param>
    /// <param name="nullsDistinct">are nulls distinct</param>
    /// <returns>the index type</returns>
    public static IndexType CreateUnique(bool persistent, bool hash, int uniqueColumnCount, NullsDistinct nullsDistinct)
    {
        return new IndexType
        {
            persistent = persistent,
            hash = hash,
            nullsDistinct = uniqueColumnCount == 1 && nullsDistinct == NullsDistinct.AllDistinct
                ? NullsDistinct.Distinct
                : nullsDistinct,
        };
    }

    /// <summary>
    /// Create a non-unique index.
    /// </summary>
    /// <param name="persistent">if the index is persistent</param>
    /// <returns>the index type</returns>
    public static IndexType CreateNonUnique(bool persistent)
    {
        return CreateNonUnique(persistent, false, false);
    }

    /// <summary>
    /// Create a non-unique index.
    /// </summary>
    /// <param name="persistent">if the index is persistent</param>
    /// <param name="hash">if a hash index should be used</param>
    /// <param name="spatial">if a spatial index should be used</param>
    /// <returns>the index type</returns>
    public static IndexType CreateNonUnique(bool persistent, bool hash, bool spatial)
    {
        return new IndexType
        {
            persistent = persistent,
            hash = hash,
            spatial = spatial,
        };
    }

    /// <summary>
    /// Create a scan pseudo-index.
    /// </summary>
    /// <param name="persistent">if the index is persistent</param>
    /// <returns>the index type</returns>
    public static IndexType CreateScan(bool persistent)
    {
        return new IndexType
        {
            persistent = persistent,
            scan = true,
        };
    }

    /// <summary>
    /// Sets if this index belongs to a constraint.
    /// </summary>
    /// <param name="belongsToConstraint">if the index belongs to a constraint</param>
    public void SetBelongsToConstraint(bool belongsToConstraint)
    {
        this.belongsToConstraint = belongsToConstraint;
    }

    /// <summary>
    /// If the index is created because of a constraint. Such indexes are to be
    /// dropped once the constraint is dropped.
    /// </summary>
    /// <returns>if the index belongs to a constraint</returns>
    public bool GetBelongsToConstraint()
    {
        return belongsToConstraint;
    }

    /// <summary>
    /// Is this a hash index?
    /// </summary>
    /// <returns>true if it is a hash index</returns>
    public bool IsHash()
    {
        return hash;
    }

    /// <summary>
    /// Is this a spatial index?
    /// </summary>
    /// <returns>true if it is a spatial index</returns>
    public bool IsSpatial()
    {
        return spatial;
    }

    /// <summary>
    /// Is this index persistent?
    /// </summary>
    /// <returns>true if it is persistent</returns>
    public bool IsPersistent()
    {
        return persistent;
    }

    /// <summary>
    /// Does this index belong to a primary key constraint?
    /// </summary>
    /// <returns>true if it references a primary key constraint</returns>
    public bool IsPrimaryKey()
    {
        return primaryKey;
    }

    /// <summary>
    /// Is this a unique index?
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsUnique()
    {
        return primaryKey || nullsDistinct.HasValue;
    }

    /// <summary>
    /// Get the SQL snippet to create such an index.
    /// </summary>
    /// <param name="addNullsDistinct">true to add nulls distinct clause</param>
    /// <returns>the SQL snippet</returns>
    public string GetSql(bool addNullsDistinct)
    {
        StringBuilder builder = new();
        if (primaryKey)
        {
            _ = builder.Append("PRIMARY KEY");
            if (hash)
            {
                _ = builder.Append(" HASH");
            }
        }
        else
        {
            if (nullsDistinct.HasValue)
            {
                _ = builder.Append("UNIQUE ");
                if (addNullsDistinct)
                {
                    NullsDistinctSql.GetSql(builder, nullsDistinct.Value, 0).Append(' ');
                }
            }

            if (hash)
            {
                _ = builder.Append("HASH ");
            }

            if (spatial)
            {
                _ = builder.Append("SPATIAL ");
            }

            _ = builder.Append("INDEX");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Is this a table scan pseudo-index?
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsScan()
    {
        return scan;
    }

    /// <summary>
    /// Returns nulls distinct treatment for unique indexes (excluding primary key indexes).
    /// For primary key and other types of indexes returns null.
    /// </summary>
    /// <returns>are nulls distinct, or null for non-unique and primary key indexes</returns>
    public NullsDistinct? GetNullsDistinct()
    {
        return nullsDistinct;
    }

    /// <summary>
    /// Returns nulls distinct treatment for unique indexes,
    /// <see cref="NullsDistinct.NotDistinct"/> for primary key indexes,
    /// and null for other types of indexes.
    /// </summary>
    /// <returns>are nulls distinct, or null for non-unique indexes</returns>
    public NullsDistinct? GetEffectiveNullsDistinct()
    {
        return nullsDistinct ?? (primaryKey ? NullsDistinct.NotDistinct : null);
    }
}
