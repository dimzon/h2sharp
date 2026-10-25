/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.constraint.DomainColumnResolver.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Constraints;

/// <summary>The column resolver of a domain constraint: the only column is VALUE.</summary>
public sealed class DomainColumnResolver : ColumnResolver
{
    private readonly Column column;

    private Value? value;

    private string? name;

    /// <summary>Creates the resolver for the domain's type.</summary>
    public DomainColumnResolver(TypeInfo typeInfo)
    {
        column = new Column("VALUE", typeInfo);
    }

    /// <summary>Sets the value the VALUE column answers.</summary>
    public void SetValue(Value value) => this.value = value;

    /// <inheritdoc/>
    public Column[] GetColumns() => [column];

    /// <inheritdoc/>
    public Column? FindColumn(string name) => null;

    /// <inheritdoc/>
    public Value? GetValue(Column column) => value;

    /// <summary>Sets the column name to print instead of VALUE.</summary>
    public void SetColumnName(string newName) => name = newName;

    /// <summary>Clears the column name override.</summary>
    public void ResetColumnName() => name = null;

    /// <summary>Gets the column name override.</summary>
    public string? GetColumnName() => name;

    /// <summary>Gets the type of the VALUE column.</summary>
    public TypeInfo GetValueType() => column.GetTypeInfo();
}
