/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.CreateSchema.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A CREATE SCHEMA statement.
 *
 * The reference gives the new schema an owner, the user the AUTHORIZATION clause names, and
 * refuses the statement when that user is not there. This port has no users and no roles
 * yet, so the owner is nobody and the name the clause names is not checked.
 */
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// A CREATE SCHEMA statement.
/// </summary>
public sealed class CreateSchema : DefineCommand
{
    private string schemaName = string.Empty;

    private bool ifNotExists;

    private string? authorization;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    public CreateSchema(SessionLocal session)
        : base(session)
    {
    }

    /// <summary>Sets the name of the schema.</summary>
    /// <param name="name">the name</param>
    public void SetSchemaName(string name) => schemaName = name;

    /// <summary>Sets whether the statement is IF NOT EXISTS.</summary>
    /// <param name="b">true for IF NOT EXISTS</param>
    public void SetIfNotExists(bool b) => ifNotExists = b;

    /// <summary>Sets the user the schema belongs to.</summary>
    /// <param name="userName">the name of the user</param>
    public void SetAuthorization(string userName) => authorization = userName;

    /// <inheritdoc/>
    public override long Update()
    {
        Database db = GetSession().GetDatabase();
        if (db.FindSchema(schemaName) is not null)
        {
            if (ifNotExists)
            {
                return 0;
            }

            throw DbException.Get(Api.ErrorCode.SchemaAlreadyExists1, schemaName);
        }

        int id = GetObjectId();
        Schema schema = new(db, id, schemaName, system: false);
        db.AddSchema(schema);
        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.CreateSchema;
}
