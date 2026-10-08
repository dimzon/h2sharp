/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.value.ValueEnum.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// ENUM value: a label, an ordinal, and the list the label came from.
/// </summary>
public sealed class ValueEnum : ValueEnumBase
{
    private readonly ExtTypeInfoEnum enumerators;

    internal ValueEnum(ExtTypeInfoEnum enumerators, string label, int ordinal)
        : base(label, ordinal)
    {
        this.enumerators = enumerators;
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => enumerators.GetEnumType();

    /// <summary>
    /// The list this value's label came from.
    /// </summary>
    /// <returns>the enumerators</returns>
    public ExtTypeInfoEnum GetEnumerators() => enumerators;

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        // Without the flag the value is written as a cast of its label to its own type, which
        // is how a reader can tell an enum from a string that happens to look like one.
        if ((sqlFlags & HasSql.NoCasts) == 0)
        {
            _ = StringUtils.QuoteStringSql(_ = builder.Append("CAST("), label).Append(" AS ");
            return enumerators.GetEnumType().GetSql(builder, sqlFlags).Append(')');
        }

        return StringUtils.QuoteStringSql(builder, label);
    }
}
