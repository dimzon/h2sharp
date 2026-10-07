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
/// rendering itself was not ported when this was written.
/// </para>
/// <para>
/// The rendering was later ported on TypeInfo, and with it the ENUM and NUMERIC subclasses,
/// so this class no longer blocks anything. An earlier note here claimed that the base
/// rendering needed the DataType registry first. That was wrong: the reference renders from
/// a switch over literal names and takes the rest of the name from Value.GetTypeName, so
/// DataType is not a prerequisite for it. See MIGRATION_STATUS.md.
/// </para>
/// </remarks>
public abstract class ExtTypeInfo
{
    /// <summary>
    /// Append the SQL representation of this type to a string builder.
    /// </summary>
    /// <remarks>
    /// Virtual rather than abstract for now, and the base implementation fails loudly
    /// instead of returning something plausible. The reference has this abstract. Keeping it
    /// virtual means a subclass that has not been ported yet fails with a reason that names
    /// the missing piece, rather than the type silently rendering as something else. Every
    /// ported subclass overrides it, so the failure is unreachable except through a subclass
    /// that does not exist yet.
    /// </remarks>
    /// <param name="builder">string builder to append to</param>
    /// <param name="sqlFlags">formatting flags</param>
    /// <returns>the specified builder</returns>
    /// <exception cref="NotSupportedException">
    /// always, for an extended type whose subclass is not ported
    /// </exception>
    public virtual StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        throw new NotSupportedException(
            $"The extended type {GetType().Name} is not ported yet, so it cannot render "
            + "itself as SQL. See MIGRATION_STATUS.md for the order.");
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
