/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.DropSynonym.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>The statement DROP SYNONYM.</summary>
public sealed class DropSynonym : SchemaCommand
{
    private string synonymName = string.Empty;

    private bool ifExists;

    /// <summary>Creates the statement.</summary>
    public DropSynonym(SessionLocal session, Schema schema) : base(session, schema)
    {
    }

    /// <summary>Sets the synonym name.</summary>
    public void SetSynonymName(string name) => synonymName = name;

    /// <summary>Sets whether the statement is IF EXISTS.</summary>
    public void SetIfExists(bool value) => ifExists = value;

    /// <inheritdoc/>
    public override long Update()
    {
        TableSynonym? synonym = GetSchema().FindSynonym(synonymName);
        if (synonym is null)
        {
            if (!ifExists)
            {
                throw DbException.Get(ErrorCode.TableOrViewNotFound1, synonymName);
            }
        }
        else
        {
            GetSession().GetDatabase().RemoveSchemaObject(GetSession(), synonym);
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.DropSynonym;
}
