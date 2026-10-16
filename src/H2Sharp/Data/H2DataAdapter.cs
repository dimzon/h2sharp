/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * A data adapter over the provider.
 *
 * DbDataAdapter's abstract surface is only the row-update events: filling and updating
 * run through the commands, so a fill reads through the select command into the table
 * and an update writes through the insert, update and delete commands. What is not here
 * is batch updating and conflict detection, which need the schema machinery behind
 * GetInsertCommand and friends — those live on the command builder and refuse there.
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
}
