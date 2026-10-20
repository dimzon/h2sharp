/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.DropSequence.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * DROP SEQUENCE.
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// A DROP SEQUENCE statement.
/// </summary>
public sealed class DropSequence : SchemaCommand
{
    private string sequenceName = string.Empty;

    private bool ifExists;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema</param>
    public DropSequence(SessionLocal session, Schema schema)
        : base(session, schema)
    {
    }

    /// <summary>Sets whether the statement is IF EXISTS.</summary>
    /// <param name="b">true for IF EXISTS</param>
    public void SetIfExists(bool b) => ifExists = b;

    /// <summary>Sets the name of the sequence.</summary>
    /// <param name="name">the name</param>
    public void SetSequenceName(string name) => sequenceName = name;

    /// <inheritdoc/>
    public override long Update()
    {
        Sequence? sequence = GetSchema().FindSequence(sequenceName);
        if (sequence is null)
        {
            if (!ifExists)
            {
                throw DbException.Get(Api.ErrorCode.SequenceNotFound1, sequenceName);
            }
        }
        else
        {
            if (sequence.GetBelongsToTable())
            {
                throw DbException.Get(Api.ErrorCode.SequenceBelongsToATable1, sequenceName);
            }

            GetSession().GetDatabase().RemoveSchemaObject(GetSession(), sequence);
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.DropSequence;
}
