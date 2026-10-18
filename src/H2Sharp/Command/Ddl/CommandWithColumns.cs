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
 * The primary key is the exception. It is a constraint, but it is also the key the table's rows
 * are addressed by, so it is kept separately and handed to the table by GetPrimaryKey. A second
 * PRIMARY KEY over the same columns does not simply win or lose: an unnamed one is replaced by
 * the later spelling, and a named one keeps its place, so the later, unnamed key is ignored.
 * Different columns are not a replacement at all but error 90017, and SetPrimaryKey decides all
 * three cases before anything has been created.
 *
 * CreateConstraints runs the held-aside commands in two passes. A self-referencing constraint
 * may point at a unique constraint defined after it, so the ones that refer to the statement's
 * own table run last; both passes hand the statement's transactional flag down, because the
 * constraints commit as part of the statement that created them, not on their own.
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
    /// <remarks>
    /// Two passes, and the order is the point: a self-referencing constraint may name a unique
    /// constraint that is defined after it, so the self-referencing ones run in the second pass,
    /// once everything they could point at exists. Each command is told whether the statement
    /// is transactional before it runs, because a constraint does not decide that for itself.
    /// </remarks>
    protected void CreateConstraints()
    {
        // Self-referencing constraints may refer to unique constraints that are defined after
        // them.
        foreach (DefineCommand command in GetConstraintCommands())
        {
            if (!IsSelfReferencing(command))
            {
                command.SetTransactional(transactional);
                _ = command.Update();
            }
        }

        foreach (DefineCommand command in GetConstraintCommands())
        {
            if (IsSelfReferencing(command))
            {
                command.SetTransactional(transactional);
                _ = command.Update();
            }
        }
    }

    /// <summary>
    /// Whether the constraint refers to the table being created.
    /// </summary>
    /// <param name="command">the constraint</param>
    /// <returns>true if it is self-referencing</returns>
    private static bool IsSelfReferencing(DefineCommand command)
        => command is AlterTableAddConstraint constraint && constraint.IsSelfReferencing();

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
    /// Sets the primary key, checking the one it would replace.
    /// </summary>
    /// <remarks>
    /// When an unnamed primary key over the same columns is already set, it is removed from the
    /// list of constraints and the new one takes its place; the return value tells the caller
    /// whether the new key is ignored instead, which is the case for a key that repeats a named
    /// one. Different columns are error 90017 — a table has one primary key, and two spellings
    /// that disagree are not a replacement.
    /// </remarks>
    /// <param name="primaryKey">the constraint</param>
    /// <returns>whether another primary key with the same columns was already set and the
    /// specified primary key should be ignored</returns>
    private bool SetPrimaryKey(AlterTableAddConstraint primaryKey)
    {
        if (this.primaryKey is not null)
        {
            IndexColumn[] oldColumns = this.primaryKey.GetIndexColumns();
            IndexColumn[] newColumns = primaryKey.GetIndexColumns();
            int len = newColumns.Length;
            if (len != oldColumns.Length)
            {
                throw Message.DbException.Get(Api.ErrorCode.SecondPrimaryKey);
            }

            for (int i = 0; i < len; i++)
            {
                if (!newColumns[i].GetName().Equals(oldColumns[i].GetName(), StringComparison.Ordinal))
                {
                    throw Message.DbException.Get(Api.ErrorCode.SecondPrimaryKey);
                }
            }

            if (this.primaryKey.GetConstraintName() is not null)
            {
                return true;
            }

            // Remove unnamed primary key
            _ = constraintCommands.Remove(this.primaryKey);
        }

        this.primaryKey = primaryKey;
        return false;
    }

    /// <summary>
    /// Gets the primary key constraint, if the statement read one.
    /// </summary>
    /// <returns>the constraint, or null</returns>
    public AlterTableAddConstraint? GetPrimaryKey() => primaryKey;
}
