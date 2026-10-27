/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterSchemaRename.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;

namespace H2Sharp.Command.Ddl;

/// <summary>ALTER SCHEMA RENAME.</summary>
public sealed class AlterSchemaRename : DefineCommand
{
    private Schema? oldSchema;
    private string newSchemaName = string.Empty;

    /// <summary>Creates the statement.</summary>
    public AlterSchemaRename(SessionLocal session) : base(session)
    {
    }

    /// <summary>Sets the schema to rename.</summary>
    public void SetOldSchema(Schema schema) => oldSchema = schema;

    /// <summary>Sets the new schema name.</summary>
    public void SetNewName(string name) => newSchemaName = name;

    /// <inheritdoc/>
    public override long Update()
    {
        // D-004: checkSchemaAdmin has no user object to check.
        Schema schema = oldSchema!;
        Database db = GetSession().GetDatabase();
        if (!schema.CanDrop())
        {
            throw DbException.Get(ErrorCode.SchemaCanNotBeDropped1, schema.GetName());
        }

        if (db.FindSchema(newSchemaName) is not null
            || newSchemaName.Equals(schema.GetName(), System.StringComparison.Ordinal))
        {
            throw DbException.Get(ErrorCode.SchemaAlreadyExists1, newSchemaName);
        }

        db.RenameDatabaseObject(GetSession(), schema, newSchemaName);
        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.AlterSchemaRename;
}
