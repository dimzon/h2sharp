/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.DropTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * DROP TABLE.
 *
 * IF EXISTS is about the statement, not about the table. It turns "there is no such table"
 * from an error into nothing, and it must not turn a drop of a *different* table into
 * silence: the decision is made per table, against what the database has.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Schemas;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// A DROP TABLE statement.
/// </summary>
public sealed class DropTable : DefineCommand
{
    private readonly List<(Schema Schema, string TableName)> tables = [];

    private bool ifExists;

    private ConstraintActionType dropAction = ConstraintActionType.RESTRICT;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    public DropTable(SessionLocal session)
        : base(session)
    {
    }

    /// <summary>Sets whether the statement is IF EXISTS.</summary>
    /// <param name="b">true for IF EXISTS</param>
    public void SetIfExists(bool b) => ifExists = b;

    /// <summary>
    /// Adds a table to drop.
    /// </summary>
    /// <param name="schema">the schema</param>
    /// <param name="tableName">the name</param>
    public void AddTable(Schema schema, string tableName)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(tableName);
        tables.Add((schema, tableName));
    }

    /// <summary>
    /// Drops the tables.
    /// </summary>
    /// <remarks>
    /// prepareDrop then executeDrop, as in the reference: resolve every name first, then
    /// remove, so a missing table fails before anything is gone. The RESTRICT dependency
    /// checks have nothing to check yet — views, materialized views and constraint objects
    /// are not ported — and neither do the locks: there is no lock manager on this path.
    /// The index maps stay in the store until it closes; map names are id-keyed, so a
    /// table created again after a drop opens fresh maps and never sees the old rows.
    /// </remarks>
    /// <returns>0</returns>
    public override long Update()
    {
        List<(Schema Schema, Tables.Table Table)> drops = [];
        foreach ((Schema schema, string name) in tables)
        {
            Tables.Table? table = schema.FindTableOrView(null, name);
            if (table is null)
            {
                if (!ifExists)
                {
                    throw Message.DbException.Get(Api.ErrorCode.TableOrViewNotFound1, name);
                }
            }
            else
            {
                drops.Add((schema, table));
            }
        }

        if (drops.Count == 0)
        {
            return 0;
        }

        foreach ((Schema schema, Tables.Table table) in drops)
        {
            schema.Remove(table);
            table.RemoveChildrenAndResources(GetSession());
        }

        return 0;
    }

    /// <summary>
    /// Sets what happens to the objects that point at the table.
    /// </summary>
    /// <param name="dropAction">the action</param>
    public void SetDropAction(ConstraintActionType dropAction) => this.dropAction = dropAction;

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.DropTable;
}
