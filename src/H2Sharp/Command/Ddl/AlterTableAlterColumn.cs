/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterTableAlterColumn.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * ALTER TABLE ... ALTER COLUMN.
 *
 * Only the changes of the identity are here: the column keeps its sequence, or loses it.
 * The other changes of a column — its type, its default, its nullability — need the rows
 * of the table to be written again, and that is not ported; they refuse by name.
 */
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// An ALTER TABLE ... ALTER COLUMN statement.
/// </summary>
public sealed class AlterTableAlterColumn : SchemaCommand
{
    private string tableName = string.Empty;

    private bool ifTableExists;

    private Column? oldColumn;

    private int type;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema</param>
    public AlterTableAlterColumn(SessionLocal session, Schema schema)
        : base(session, schema)
    {
    }

    /// <summary>Sets the name of the table.</summary>
    /// <param name="name">the name</param>
    public void SetTableName(string name) => tableName = name;

    /// <summary>Sets whether the table name was written with IF EXISTS.</summary>
    /// <param name="b">true for IF EXISTS</param>
    public void SetIfTableExists(bool b) => ifTableExists = b;

    /// <summary>Sets the column that is changed.</summary>
    /// <param name="column">the column</param>
    public void SetOldColumn(Column column) => oldColumn = column;

    /// <summary>Sets the kind of the change.</summary>
    /// <param name="newType">the kind, one of the CommandInterface constants</param>
    public void SetType(int newType) => type = newType;

    /// <inheritdoc/>
    public override long Update()
    {
        switch (type)
        {
            case CommandInterface.AlterTableAlterColumnDropIdentity:
                {
                    Column? column = oldColumn;
                    if (column is null)
                    {
                        return 0;
                    }

                    Schemas.Sequence? sequence = column.GetSequence();
                    if (sequence is null)
                    {
                        return 0;
                    }

                    column.SetSequence(null, false);
                    Table? table = column.GetTable();
                    if (table is not null)
                    {
                        table.RemoveSequence(sequence);
                    }

                    GetSession().GetDatabase().RemoveSchemaObject(GetSession(), sequence);
                    return 0;
                }

            default:
                throw new TODOException(
                    "AlterTableAlterColumn.update: only the identity of a column is ported");
        }
    }

    /// <inheritdoc/>
    public override int GetCommandType() => type;
}
