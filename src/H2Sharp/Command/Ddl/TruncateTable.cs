/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.TruncateTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * TRUNCATE TABLE.
 */
using H2Sharp.Engine;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// A TRUNCATE TABLE statement.
/// </summary>
public sealed class TruncateTable : DefineCommand
{
    private Table? table;

    private bool restart;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    public TruncateTable(SessionLocal session)
        : base(session)
    {
    }

    /// <summary>Sets the table to truncate.</summary>
    /// <param name="newTable">the table</param>
    public void SetTable(Table newTable) => table = newTable;

    /// <summary>Sets whether the identity columns start over.</summary>
    /// <param name="b">true to restart the identity</param>
    public void SetRestart(bool b) => restart = b;

    /// <inheritdoc/>
    public override long Update()
    {
        Table target = table ?? throw new TODOException("TruncateTable.update: no table");
        SessionLocal session = GetSession();
        target.Truncate(session);
        long result = 0;
        if (restart)
        {
            foreach (Column column in target.GetColumns())
            {
                Sequence? sequence = column.GetSequence();
                if (sequence is not null)
                {
                    sequence.Modify(sequence.GetStartValue(), null, null, null, null, null, null);
                }
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.TruncateTable;
}
