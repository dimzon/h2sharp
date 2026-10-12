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
 * It is not one table. The statement accumulates the tables it was asked to drop — a DROP TABLE
 * with CASCADE, or a schema's worth of them — and the count it answers is the number of tables
 * gone, not the number of names written. That is the reference's shape and it matters: a DROP
 * TABLE T1, T2 that drops three tables because a third was cascaded answers 3, and a port that
 * counted the names would answer 2 and leave a caller believing nothing else went with them.
 *
 * IF EXISTS is about the statement, not about the table. It turns "there is no such table" from
 * an error into nothing, and it must not turn a drop of a *different* table into silence: the
 * decision is made per table, against what the database has.
 *
 * Not ported: the body. It needs the meta table and the schema registry.
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
    /// <returns>the number of tables dropped, counting the ones that went with them</returns>
    public override long Update()
    {
        throw new TODOException(
            "DropTable.update: the meta table is not ported (tables=" + tables.Count
            + ", ifExists=" + ifExists
            + ", dropAction=" + dropAction + ")");
    }

    /// <summary>
    /// Sets what happens to the objects that point at the table.
    /// </summary>
    /// <param name="dropAction">the action</param>
    public void SetDropAction(ConstraintActionType dropAction) => this.dropAction = dropAction;

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.DropTable;
}
