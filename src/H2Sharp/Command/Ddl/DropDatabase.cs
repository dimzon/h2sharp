/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.DropDatabase.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A DROP ALL OBJECTS statement.
 *
 * The reference also drops the users and the roles, and can delete the files of the
 * database when it closes. This port has no users and no roles, and no files to delete
 * while the database is open.
 */
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// A DROP ALL OBJECTS statement.
/// </summary>
public sealed class DropDatabase : DefineCommand
{
    private bool dropAllObjects;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    public DropDatabase(SessionLocal session)
        : base(session)
    {
    }

    /// <summary>Sets whether the statement drops every object of the database.</summary>
    /// <param name="b">true to drop the objects</param>
    public void SetDropAllObjects(bool b) => dropAllObjects = b;

    /// <inheritdoc/>
    public override long Update()
    {
        if (dropAllObjects)
        {
            DropAllObjects();
        }

        return 0;
    }

    /// <summary>
    /// Drops every object of the database: the tables, the sequences, and the schemas but the
    /// main one.
    /// </summary>
    private void DropAllObjects()
    {
        SessionLocal current = GetSession();
        Database db = current.GetDatabase();
        foreach (Schema schema in db.GetSchemas())
        {
            foreach (Table table in schema.GetAllTablesAndViews(current))
            {
                if (table.GetTableName() is null)
                {
                    // The object is already dead.
                    continue;
                }

                if (table.GetTableType() == Table.SYSTEM_TABLE)
                {
                    // The system tables are not objects of the database: they are the
                    // catalogue itself, and it stays.
                    continue;
                }

                db.RemoveSchemaObject(current, table);
            }

            foreach (Sequence sequence in schema.GetAllSequences())
            {
                db.RemoveSchemaObject(current, sequence);
            }
        }

        Schema? main = db.GetMainSchema();
        foreach (Schema schema in db.GetSchemas())
        {
            // The main schema and the schema of the catalogue stay: the catalogue is the
            // catalogue, and dropping it would leave the database without a way to be read.
            if (schema.GetName() == main?.GetName()
                || schema.GetName() == Database.InformationSchemaName)
            {
                continue;
            }

            db.RemoveSchema(schema);
        }
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.DropAllObjects;
}
