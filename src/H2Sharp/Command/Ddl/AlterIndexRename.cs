/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterIndexRename.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using Index = H2Sharp.Indexes.Index;

namespace H2Sharp.Command.Ddl;

/// <summary>ALTER INDEX RENAME.</summary>
public sealed class AlterIndexRename : DefineCommand
{
    private bool ifExists;
    private Schema? oldSchema;
    private string oldIndexName = string.Empty;
    private string newIndexName = string.Empty;

    /// <summary>Creates the statement.</summary>
    public AlterIndexRename(SessionLocal session) : base(session)
    {
    }

    /// <summary>Sets whether a missing index is ignored.</summary>
    public void SetIfExists(bool value) => ifExists = value;

    /// <summary>Sets the schema of the index.</summary>
    public void SetOldSchema(Schema schema) => oldSchema = schema;

    /// <summary>Sets the old index name.</summary>
    public void SetOldName(string name) => oldIndexName = name;

    /// <summary>Sets the new index name.</summary>
    public void SetNewName(string name) => newIndexName = name;

    /// <inheritdoc/>
    public override long Update()
    {
        Schema schema = oldSchema!;
        Index? oldIndex = schema.FindIndex(GetSession(), oldIndexName);
        if (oldIndex is null)
        {
            if (!ifExists)
            {
                throw DbException.Get(ErrorCode.IndexNotFound1, newIndexName);
            }

            return 0;
        }

        if (schema.FindIndex(GetSession(), newIndexName) is not null
            || newIndexName.Equals(oldIndexName, System.StringComparison.Ordinal))
        {
            throw DbException.Get(ErrorCode.IndexAlreadyExists1, newIndexName);
        }

        // D-004: the SCHEMA_OWNER right on the index's table has no user object to check.
        GetSession().GetDatabase().RenameSchemaObject(GetSession(), oldIndex, newIndexName);
        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.AlterIndexRename;
}
