/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * The row-updated event arguments of the adapter, typed with this provider's command.
 *
 * The counterpart of H2RowUpdatingEventArgs for the event after the command ran.
 */
using System.Data;
using System.Data.Common;

namespace H2Sharp.Data;

/// <summary>
/// Represents the method that handles the <see cref="H2DataAdapter.RowUpdated"/> event.
/// </summary>
/// <param name="sender">the adapter</param>
/// <param name="e">the event arguments</param>
public delegate void H2RowUpdatedEventHandler(object sender, H2RowUpdatedEventArgs e);

/// <summary>
/// Provides the data for the <see cref="H2DataAdapter.RowUpdated"/> event.
/// </summary>
public sealed class H2RowUpdatedEventArgs : RowUpdatedEventArgs
{
    /// <summary>
    /// Creates the event arguments for one row.
    /// </summary>
    /// <param name="dataRow">the row that was updated</param>
    /// <param name="command">the command that updated it</param>
    /// <param name="statementType">the kind of statement</param>
    /// <param name="tableMapping">the mapping of the row</param>
    public H2RowUpdatedEventArgs(
        DataRow dataRow, IDbCommand? command, StatementType statementType, DataTableMapping tableMapping)
        : base(dataRow, command, statementType, tableMapping)
    {
    }

    /// <summary>
    /// Gets the command that updated the row.
    /// </summary>
    public new H2Command? Command => (H2Command?)base.Command;
}
