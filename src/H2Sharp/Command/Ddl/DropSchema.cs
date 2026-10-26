/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.DropSchema, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Collections.Generic;
using H2Sharp.Api;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;

namespace H2Sharp.Command.Ddl;

/// <summary>DROP SCHEMA.</summary>
public sealed class DropSchema : DefineCommand
{
    private string schemaName = string.Empty;

    private bool ifExists;

    private ConstraintActionType dropAction = ConstraintActionType.RESTRICT;

    /// <summary>Creates the statement.</summary>
    public DropSchema(SessionLocal session)
        : base(session)
    {
    }

    /// <summary>Sets the schema name.</summary>
    public void SetSchemaName(string name) => schemaName = name;

    /// <summary>Sets whether IF EXISTS was written.</summary>
    public void SetIfExists(bool value) => ifExists = value;

    /// <summary>Sets the drop action.</summary>
    public void SetDropAction(ConstraintActionType action) => dropAction = action;

    /// <inheritdoc/>
    public override long Update()
    {
        Database db = GetSession().GetDatabase();
        Schema? schema = db.FindSchema(schemaName);
        if (schema is null)
        {
            if (!ifExists)
            {
                throw DbException.Get(ErrorCode.SchemaNotFound1, schemaName);
            }

            return 0;
        }

        if (!schema.CanDrop())
        {
            throw DbException.Get(ErrorCode.SchemaCanNotBeDropped1, schemaName);
        }

        if (dropAction == ConstraintActionType.RESTRICT)
        {
            List<string> names = [];
            foreach (Tables.Table table in schema.GetAllTablesAndViews(GetSession()))
            {
                names.Add(table.GetTableName());
            }

            foreach (Domain domain in schema.GetAllDomains())
            {
                names.Add(domain.GetName()!);
            }

            foreach (Sequence sequence in schema.GetAllSequences())
            {
                names.Add(sequence.GetName()!);
            }

            if (names.Count > 0)
            {
                throw DbException.Get(ErrorCode.CannotDrop2, schemaName, string.Join(", ", names));
            }
        }

        db.RemoveSchema(schema);
        schema.RemoveChildrenAndResources(GetSession());
        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.DropSchema;
}
