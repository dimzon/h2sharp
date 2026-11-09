/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * A data adapter over the provider.
 *
 * Filling and updating run through the commands, so a fill reads through the select
 * command into the table and an update writes through the insert, update and delete
 * commands. Those commands can be supplied by hand, and an H2CommandBuilder attached to
 * this adapter builds them from the select command when they are not. The row-update
 * events are declared here, because DbDataAdapter itself keeps only the hooks that create
 * and raise the arguments: without this declaration a caller could not subscribe to them.
 * FillError is different — DataAdapter declares and raises it itself, so it is inherited
 * and needs no member here.
 */
using System;
using System.Data;
using System.Data.Common;

namespace H2Sharp.Data;

/// <summary>
/// Bridges result sets and data sets over this provider.
/// </summary>
public sealed class H2DataAdapter : DbDataAdapter
{
    /// <summary>
    /// Creates an adapter with no commands.
    /// </summary>
    public H2DataAdapter()
    {
    }

    /// <summary>
    /// Creates an adapter that selects through the given command.
    /// </summary>
    /// <param name="selectCommand">the select command</param>
    public H2DataAdapter(H2Command selectCommand)
    {
        SelectCommand = selectCommand;
    }

    /// <summary>
    /// Creates an adapter that selects with the given text on the given connection.
    /// </summary>
    /// <param name="selectCommandText">the select text</param>
    /// <param name="connection">the connection, held but not opened</param>
    public H2DataAdapter(string selectCommandText, H2Connection connection)
    {
        SelectCommand = new H2Command(selectCommandText, connection);
    }

    /// <inheritdoc/>
    public new H2Command? SelectCommand
    {
        get => (H2Command?)base.SelectCommand;
        set => base.SelectCommand = value;
    }

    /// <inheritdoc/>
    public new H2Command? InsertCommand
    {
        get => (H2Command?)base.InsertCommand;
        set => base.InsertCommand = value;
    }

    /// <inheritdoc/>
    public new H2Command? UpdateCommand
    {
        get => (H2Command?)base.UpdateCommand;
        set => base.UpdateCommand = value;
    }

    /// <inheritdoc/>
    public new H2Command? DeleteCommand
    {
        get => (H2Command?)base.DeleteCommand;
        set => base.DeleteCommand = value;
    }

    /// <summary>
    /// Occurs before an update command runs for a row.
    /// </summary>
    public event H2RowUpdatingEventHandler? RowUpdating;

    /// <summary>
    /// Occurs after an update command ran for a row.
    /// </summary>
    public event H2RowUpdatedEventHandler? RowUpdated;

    /// <inheritdoc/>
    protected override RowUpdatingEventArgs CreateRowUpdatingEvent(
        DataRow dataRow, IDbCommand? command, StatementType statementType, DataTableMapping tableMapping)
        => new H2RowUpdatingEventArgs(dataRow, command, statementType, tableMapping);

    /// <inheritdoc/>
    protected override RowUpdatedEventArgs CreateRowUpdatedEvent(
        DataRow dataRow, IDbCommand? command, StatementType statementType, DataTableMapping tableMapping)
        => new H2RowUpdatedEventArgs(dataRow, command, statementType, tableMapping);

    /// <inheritdoc/>
    protected override void OnRowUpdating(RowUpdatingEventArgs value)
    {
        RowUpdating?.Invoke(this, (H2RowUpdatingEventArgs)value);
    }

    /// <inheritdoc/>
    protected override void OnRowUpdated(RowUpdatedEventArgs value)
    {
        RowUpdated?.Invoke(this, (H2RowUpdatedEventArgs)value);
    }
}
