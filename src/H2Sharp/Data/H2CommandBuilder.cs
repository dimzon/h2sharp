/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * A command builder over the provider.
 *
 * It exists because the factory must hand one out, and it refuses where generation would
 * need the schema machinery: building an INSERT from a select command means quoting every
 * name the way the mode spells it, matching parameters to key columns, and detecting the
 * conflicts the update would overwrite. That is conflict-option semantics on top of the
 * catalogue, and inventing a subset of it here would generate statements the database
 * reads differently from what the builder meant.
 */
using System;
using System.Data;
using System.Data.Common;

namespace H2Sharp.Data;

/// <summary>
/// Builds single-table commands from a select command.
/// </summary>
public sealed class H2CommandBuilder : DbCommandBuilder
{
    /// <summary>
    /// Creates a builder with no adapter.
    /// </summary>
    public H2CommandBuilder()
    {
    }

    /// <summary>
    /// Creates a builder for the given adapter.
    /// </summary>
    /// <param name="adapter">the adapter</param>
    public H2CommandBuilder(H2DataAdapter adapter)
    {
        DataAdapter = adapter;
    }

    /// <inheritdoc/>
    protected override void ApplyParameterInfo(
        DbParameter parameter, DataRow row, StatementType statementType, bool whereClause)
        => throw new NotSupportedException("Command generation needs the schema machinery");

    /// <inheritdoc/>
    protected override string GetParameterName(string parameterName)
        => throw new NotSupportedException("Command generation needs the schema machinery");

    /// <inheritdoc/>
    protected override string GetParameterName(int parameterOrdinal)
        => throw new NotSupportedException("Command generation needs the schema machinery");

    /// <inheritdoc/>
    protected override string GetParameterPlaceholder(int parameterOrdinal)
        => throw new NotSupportedException("Command generation needs the schema machinery");

    /// <inheritdoc/>
    protected override void SetRowUpdatingHandler(DbDataAdapter adapter)
    {
    }
}
