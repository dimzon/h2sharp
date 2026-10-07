/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.value.ExtTypeInfo.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Extended parameters of a data type: the element type of an array, the fields of a row,
/// the enum definition, and so on.
/// </summary>
/// <remarks>
/// <para>
/// The reference declares this as an abstract class implementing HasSQL, whose only member
/// is a toString that renders the type as SQL. There is no abstract getDeclaredTypeName
/// here, unlike in the first draft: that method belongs to the SQL rendering path, and the
/// rendering itself is not ported yet.
/// </para>
/// <para>
/// GetSql is the blocker for the concrete subclasses. It needs TypeInfo.GetSql, and that in
/// turn needs the DataType registry for the canonical type names and the precision and scale
/// syntax. So DataType is the next prerequisite, not the ExtTypeInfoEnum and
/// ExtTypeInfoRow classes that were assumed to be. See MIGRATION_STATUS.md.
/// </para>
/// </remarks>
public abstract class ExtTypeInfo
{
    /// <summary>
    /// Append the SQL representation of this type to a string builder.
    /// </summary>
    /// <remarks>
    /// Virtual rather than abstract for now, and the base implementation fails loudly
    /// instead of returning something plausible. The reference has this abstract; making it
    /// so here would force TypeInfo to render SQL before the DataType registry exists, and
    /// the alternative to inheriting the reference's shape was dropping the array element
    /// handling from areSameTypes. Failing with a reason is the better of the two, and it
    /// disappears when DataType is ported and the members become abstract again.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the specified builder</returns>
    /// <exception cref="NotSupportedException">always, until the DataType registry is ported</exception>
    public virtual StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        throw new NotSupportedException(
            "SQL rendering of a data type is not ported: it needs the DataType registry, "
            + "which holds the canonical type names and the precision and scale syntax. "
            + "See MIGRATION_STATUS.md for the order.");
    }

    /// <summary>
    /// The SQL representation of this type with the trace formatting flags.
    /// </summary>
    /// <returns>the SQL text</returns>
    public string GetTraceSql() => GetSql(HasSql.TraceSqlFlags).ToString();

    /// <summary>
    /// The SQL representation of this type.
    /// </summary>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the SQL text</returns>
    public string GetSql(int sqlFlags) => GetSql(new StringBuilder(), sqlFlags).ToString();

    /// <inheritdoc/>
    public override string ToString() => GetSql(HasSql.QuoteOnlyWhenRequired);
}
