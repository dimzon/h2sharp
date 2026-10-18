/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A statement that changes a table that already exists.
 *
 * The base holds the table's name and nothing else, and it is worth seeing why the name is a
 * field rather than something looked up at run time: the statement is read once and may run
 * much later, by which time the session's current schema may be a different one. The name is
 * what was written; the schema is what the statement was prepared against.
 *
 * Update() is final and it is the same for every subclass, because they all do the same three
 * things: commit whatever the statement does not need, find the table, and run the change on
 * it. What the change *is* is the subclass's update(Table).
 */
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// A statement that changes a table.
/// </summary>
public abstract class AlterTable : SchemaCommand
{
    private string tableName = string.Empty;

    private bool ifTableExists;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema</param>
    protected AlterTable(SessionLocal session, Schema schema)
        : base(session, schema)
    {
    }

    /// <summary>
    /// Gets the name of the table.
    /// </summary>
    /// <returns>the name</returns>
    public string GetTableName() => tableName;

    /// <summary>
    /// Sets the name of the table.
    /// </summary>
    /// <param name="tableName">the name</param>
    public void SetTableName(string tableName)
    {
        this.tableName = tableName;
    }

    /// <summary>
    /// Sets whether the statement is IF EXISTS.
    /// </summary>
    /// <param name="b">true for IF EXISTS</param>
    public void SetIfTableExists(bool b)
    {
        ifTableExists = b;
    }

    /// <summary>
    /// Whether the statement is IF EXISTS.
    /// </summary>
    /// <returns>true if it is</returns>
    protected bool IsIfTableExists() => ifTableExists;

    /// <inheritdoc/>
    /// <remarks>
    /// Final and the same for every subclass, as in the reference: commit what this statement
    /// does not need, find the table, and change it. The change itself is
    /// <see cref="Update(Table)"/>. The rights check waits for the user layer — refusing here
    /// would refuse every ALTER TABLE on a database that has no users yet.
    /// </remarks>
    public override long Update()
    {
        Table? table = GetSchema().FindTableOrView(GetSession(), GetTableName());
        if (table is null)
        {
            if (IsIfTableExists())
            {
                return 0;
            }

            throw DbException.Get(Api.ErrorCode.TableOrViewNotFound1, GetTableName());
        }

        return Update(table);
    }

    /// <summary>
    /// Changes the given table.
    /// </summary>
    /// <param name="table">the table</param>
    /// <returns>0</returns>
    public abstract long Update(Table table);
}
