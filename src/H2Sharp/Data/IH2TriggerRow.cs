/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A row of a table as a trigger sees it. The values are CLR objects in column order, and
 * the column names are the raw stored names — the trigger maps names to positions itself,
 * the engine folds nothing.
 */
using System.Collections.Generic;

namespace H2Sharp.Data;

/// <summary>
/// A row handed to <see cref="IH2Trigger.Fire"/>.
/// </summary>
/// <remarks>
/// Changing a value of a BEFORE trigger's new row changes the row that is written — the
/// engine compares the values against a snapshot after the fire and writes the changes
/// back, as the reference does.
/// </remarks>
public interface IH2TriggerRow
{
    /// <summary>
    /// The values of all columns of the table, in column order, as CLR objects.
    /// </summary>
    IReadOnlyList<object?> Values { get; }

    /// <summary>The names of the columns, in the same order, as stored.</summary>
    IReadOnlyList<string> ColumnNames { get; }

    /// <summary>The number of columns of the table.</summary>
    int Count { get; }

    /// <summary>Gets the value at the given column position.</summary>
    object? GetValue(int index);

    /// <summary>
    /// Sets the value at the given column position. For a BEFORE trigger's new row the
    /// change is written to the table; other rows are read-only.
    /// </summary>
    void SetValue(int index, object? value);
}
