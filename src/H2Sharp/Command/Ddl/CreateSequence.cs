/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.CreateSequence.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * CREATE SEQUENCE.
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// A CREATE SEQUENCE statement.
/// </summary>
public sealed class CreateSequence : SchemaCommand
{
    private string sequenceName = string.Empty;

    private bool ifNotExists;

    private SequenceOptions options = new();

    private bool belongsToTable;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema</param>
    public CreateSequence(SessionLocal session, Schema schema)
        : base(session, schema)
    {
        SetTransactional(true);
    }

    /// <summary>Sets the name of the sequence.</summary>
    /// <param name="name">the name</param>
    public void SetSequenceName(string name) => sequenceName = name;

    /// <summary>Sets whether the statement is IF NOT EXISTS.</summary>
    /// <param name="b">true for IF NOT EXISTS</param>
    public void SetIfNotExists(bool b) => ifNotExists = b;

    /// <summary>Sets the options of the sequence.</summary>
    /// <param name="newOptions">the options</param>
    public void SetOptions(SequenceOptions newOptions) => options = newOptions;

    /// <summary>Sets whether the sequence belongs to a table.</summary>
    /// <param name="b">true if it belongs to a table</param>
    public void SetBelongsToTable(bool b) => belongsToTable = b;

    /// <inheritdoc/>
    public override long Update()
    {
        Database db = GetSession().GetDatabase();
        Schema schema = GetSchema();
        if (schema.FindSequence(sequenceName) is not null)
        {
            if (ifNotExists)
            {
                return 0;
            }

            throw DbException.Get(Api.ErrorCode.SequenceAlreadyExists1, sequenceName);
        }

        int id = db.AllocateObjectId();
        Sequence sequence = new(GetSession(), schema, id, sequenceName, options, belongsToTable);
        db.AddSchemaObject(GetSession(), sequence);
        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.CreateSequence;
}
