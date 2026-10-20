/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterSequence.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * ALTER SEQUENCE.
 *
 * The same statement changes the sequence of an identity column, when ALTER TABLE
 * ALTER COLUMN names one: then the column is remembered and the sequence of the column
 * is the one that is changed.
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// An ALTER SEQUENCE statement.
/// </summary>
public sealed class AlterSequence : SchemaCommand
{
    private bool ifExists;

    private Column? column;

    private bool? always;

    private string sequenceName = string.Empty;

    private Sequence? sequence;

    private SequenceOptions options = new();

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema</param>
    public AlterSequence(SessionLocal session, Schema schema)
        : base(session, schema)
    {
        SetTransactional(true);
    }

    /// <summary>Sets whether the statement is IF EXISTS.</summary>
    /// <param name="b">true for IF EXISTS</param>
    public void SetIfExists(bool b) => ifExists = b;

    /// <summary>Sets the name of the sequence.</summary>
    /// <param name="name">the name</param>
    public void SetSequenceName(string name) => sequenceName = name;

    /// <summary>Sets the options of the sequence.</summary>
    /// <param name="newOptions">the options</param>
    public void SetOptions(SequenceOptions newOptions) => options = newOptions;

    /// <summary>
    /// Sets the column whose sequence is changed, as ALTER TABLE ALTER COLUMN does.
    /// </summary>
    /// <param name="newColumn">the column</param>
    /// <param name="newAlways">whether the value is always generated, or null</param>
    public void SetColumn(Column newColumn, bool? newAlways)
    {
        column = newColumn;
        always = newAlways;
        sequence = newColumn.GetSequence();
        if (sequence is null && !ifExists)
        {
            throw DbException.Get(Api.ErrorCode.SequenceNotFound1, newColumn.GetName());
        }
    }

    /// <inheritdoc/>
    public override long Update()
    {
        if (sequence is null)
        {
            sequence = GetSchema().FindSequence(sequenceName);
            if (sequence is null)
            {
                if (!ifExists)
                {
                    throw DbException.Get(Api.ErrorCode.SequenceNotFound1, sequenceName);
                }

                return 0;
            }
        }

        options.SetDataType(sequence.GetDataType());
        long? startValue = options.GetStartValue(GetSession());
        sequence.Modify(
            options.GetRestartValue(GetSession(), startValue ?? sequence.GetStartValue()),
            startValue,
            options.GetMinValue(sequence, GetSession()),
            options.GetMaxValue(sequence, GetSession()),
            options.GetIncrement(GetSession()),
            options.GetCycle(),
            options.GetCacheSize(GetSession()));
        sequence.Flush(GetSession());
        if (column is not null && always is not null)
        {
            column.SetSequence(sequence, always.Value);
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.AlterSequence;
}
