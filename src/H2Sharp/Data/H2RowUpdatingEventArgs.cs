/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * The row-updating event arguments of the adapter, typed with this provider's command.
 *
 * DbDataAdapter has no public RowUpdating event of its own: it creates the arguments and
 * calls an empty hook, and a concrete adapter is the one that declares the event. This is
 * that declaration for H2Sharp, shaped after SqlRowUpdatingEventArgs: the event argument
 * itself and the command on it are the provider's types, so a handler does not cast.
 */
using System.Data;
using System.Data.Common;

namespace H2Sharp.Data;

/// <summary>
/// Represents the method that handles the <see cref="H2DataAdapter.RowUpdating"/> event.
/// </summary>
/// <param name="sender">the adapter</param>
/// <param name="e">the event arguments</param>
public delegate void H2RowUpdatingEventHandler(object sender, H2RowUpdatingEventArgs e);

/// <summary>
/// Provides the data for the <see cref="H2DataAdapter.RowUpdating"/> event.
/// </summary>
public sealed class H2RowUpdatingEventArgs : RowUpdatingEventArgs
{
    /// <summary>
    /// Creates the event arguments for one row.
    /// </summary>
    /// <param name="dataRow">the row being updated</param>
    /// <param name="command">the command that updates it</param>
    /// <param name="statementType">the kind of statement</param>
    /// <param name="tableMapping">the mapping of the row</param>
    public H2RowUpdatingEventArgs(
        DataRow dataRow, IDbCommand? command, StatementType statementType, DataTableMapping tableMapping)
        : base(dataRow, command, statementType, tableMapping)
    {
    }

    /// <summary>
    /// Gets or sets the command that updates the row.
    /// </summary>
    public new H2Command? Command
    {
        get => (H2Command?)base.Command;
        set => base.Command = value;
    }
}
