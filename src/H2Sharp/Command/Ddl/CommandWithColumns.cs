/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.CommandWithColumns.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A catalogue statement that creates columns and then constraints on them.
 *
 * The two are separate steps and the order is the whole point. A constraint needs a table to
 * belong to, and a table needs its columns before a constraint can name one — so a parser that
 * reads "CREATE TABLE T(A INT, PRIMARY KEY(A))" reads a constraint before the table exists.
 * The constraints are held aside until Update asks for them, and CreateConstraints runs them
 * once the table is there.
 *
 * The primary key is the exception and the reason is worth seeing. It is a constraint, but it is
 * also what makes the rows of the table addressable, so it is kept separately and handed to the
 * table by GetPrimaryKey. SetPrimaryKey answers whether the key that was just read replaces the
 * one before it: a table may be written with two PRIMARY KEY clauses, and the first wins unless
 * the later one is self-referencing — a table whose key refers to itself has to keep the last
 * such key, because that is the one the rows are ordered by.
 *
 * CreateIndex is the other exception: it is a DefineCommand but not an AlterTableAddConstraint,
 * and the cast below would fail on it. It is held aside like everything else.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// A statement that creates columns and constraints.
/// </summary>
public abstract class CommandWithColumns : SchemaCommand
{
    private readonly List<DefineCommand> constraintCommands = [];

    private AlterTableAddConstraint? primaryKey;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema</param>
    protected CommandWithColumns(SessionLocal session, Schema schema)
        : base(session, schema)
    {
    }

    /// <summary>
    /// Adds a column.
    /// </summary>
    /// <param name="column">the column</param>
    public abstract void AddColumn(Column column);

    /// <summary>
    /// Adds a constraint to be created once the table exists.
    /// </summary>
    /// <param name="command">the constraint</param>
    public void AddConstraintCommand(DefineCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command is not CreateIndex)
        {
            AlterTableAddConstraint con = (AlterTableAddConstraint)command;
            if (con.GetCommandType() == CommandInterface.AlterTableAddConstraintPrimaryKey)
            {
                if (SetPrimaryKey(con))
                {
                    return;
                }
            }
        }

        GetConstraintCommands().Add(command);
    }

    /// <summary>
    /// Makes the given columns NOT NULL, which a primary key requires.
    /// </summary>
    /// <remarks>
    /// It is not optional and it is not a default: a column of a primary key cannot be NULL,
    /// and a table that allowed it would let a row exist that the key cannot name. That is why
    /// this runs after the columns exist and before the key does.
    /// </remarks>
    /// <param name="columns">the columns</param>
    protected void ChangePrimaryKeysToNotNull(List<Column> columns)
    {
        if (primaryKey is null)
        {
            return;
        }

        IndexColumn[] keyColumns = primaryKey.GetIndexColumns();
        foreach (Column column in columns)
        {
            foreach (IndexColumn keyColumn in keyColumns)
            {
                if (column.GetName().Equals(keyColumn.GetName(), StringComparison.Ordinal))
                {
                    column.SetNullable(false);
                }
            }
        }
    }

    /// <summary>
    /// Creates the constraints that were held aside.
    /// </summary>
    protected void CreateConstraints()
    {
        foreach (DefineCommand command in GetConstraintCommands())
        {
            _ = command.Update();
        }

        GetConstraintCommands().Clear();
    }

    /// <summary>
    /// Whether the constraint refers to the table being created.
    /// </summary>
    /// <param name="command">the constraint</param>
    /// <returns>true if it is self-referencing</returns>
    private static bool IsSelfReferencing(DefineCommand command)
    {
        if (command is AlterTableAddConstraint constraint && constraint.IsSelfReferencing())
        {
            return true;
        }

        return command is CreateIndex createIndex && createIndex.IsOnTable();
    }

    /// <summary>
    /// Creates the sequences the given columns need.
    /// </summary>
    /// <param name="columns">the columns</param>
    /// <param name="temporary">whether they are temporary</param>
    /// <returns>the sequences</returns>
    protected List<Sequence> GenerateSequences(List<Column> columns, bool temporary)
        => throw new TODOException("CommandWithColumns.generateSequences: sequences are not created yet");

    /// <summary>
    /// Gets the constraints held aside.
    /// </summary>
    /// <returns>the constraints</returns>
    private List<DefineCommand> GetConstraintCommands() => constraintCommands;

    /// <summary>
    /// Sets the primary key of this statement.
    /// </summary>
    /// <param name="primaryKey">the constraint</param>
    /// <returns>whether it was accepted as the key</returns>
    private bool SetPrimaryKey(AlterTableAddConstraint primaryKey)
    {
        if (this.primaryKey is null || IsSelfReferencing(primaryKey))
        {
            this.primaryKey = primaryKey;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the primary key constraint, if the statement read one.
    /// </summary>
    /// <returns>the constraint, or null</returns>
    public AlterTableAddConstraint? GetPrimaryKey() => primaryKey;
}
