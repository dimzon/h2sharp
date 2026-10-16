/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterTableAddConstraint.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * ALTER TABLE ... ADD CONSTRAINT.
 *
 * One statement, five constraints. That is why the kind is a field set in the constructor
 * rather than a class per constraint: PRIMARY KEY, UNIQUE, CHECK and the two FOREIGN KEYs all
 * read their columns the same way and differ in what they do with them, and a caller that has to
 * pick a class before it knows which constraint was written would have to read the statement
 * twice.
 *
 * The reference is self-referencing for the primary key, which is the one case a table
 * referring to itself raises: the constraint is read before the table exists, so "the table
 * this refers to" is the statement's own name rather than something looked up.
 *
 * Not ported: the body needs the index and the constraint machinery.
 */
using System;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// An ALTER TABLE ADD CONSTRAINT statement.
/// </summary>
public class AlterTableAddConstraint : AlterTable
{
    private readonly int type;

    private readonly bool ifNotExists;

    private ConstraintActionType deleteAction = ConstraintActionType.NO_ACTION;

    private ConstraintActionType updateAction = ConstraintActionType.NO_ACTION;

    private string? constraintName;

    private bool selfReferencing;

    private NullsDistinct? nullsDistinct;

    private Expression? checkExpression;

    private IndexColumn[] indexColumns = [];

    private IndexColumn[] refIndexColumns = [];

    private Schema? refSchema;

    private string? refTableName;

    private H2Sharp.Indexes.Index? index;

    private H2Sharp.Indexes.Index? refIndex;

    private string? comment;

    private bool checkExisting;

    private bool primaryKeyHash;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema</param>
    /// <param name="type">the kind of constraint</param>
    /// <param name="ifNotExists">whether the statement is IF NOT EXISTS</param>
    public AlterTableAddConstraint(SessionLocal session, Schema schema, int type, bool ifNotExists)
        : base(session, schema)
    {
        this.type = type;
        this.ifNotExists = ifNotExists;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only the primary key runs: it is an index, and the index layer is here. Every other
    /// constraint waits for the constraint layer with it. The catalog object (ConstraintUnique)
    /// is not created yet — nothing reads it back without INFORMATION_SCHEMA — but the index
    /// it would own is, which is what enforces uniqueness and answers MIN and MAX.
    /// </remarks>
    public override long Update(Table table)
    {
        if (type != CommandInterface.AlterTableAddConstraintPrimaryKey)
        {
            throw new TODOException("AlterTableAddConstraint.update: constraints are not ported");
        }

        SessionLocal session = GetSession();
        IndexColumn[] keyColumns = MapColumns(table, indexColumns);
        foreach (H2Sharp.Indexes.Index existing in table.GetIndexes())
        {
            if (existing == table.GetPrimaryKey() || !existing.IsPrimaryKey())
            {
                continue;
            }

            IndexColumn[] existingColumns = existing.GetColumns();
            if (existingColumns.Length == keyColumns.Length)
            {
                bool same = true;
                for (int i = 0; i < existingColumns.Length && same; i++)
                {
                    same = ReferenceEquals(existingColumns[i].GetColumn(), keyColumns[i].GetColumn());
                }

                if (same)
                {
                    return 0;
                }
            }

            throw DbException.Get(Api.ErrorCode.SecondPrimaryKey);
        }

        H2Sharp.Indexes.Index index = table.AddIndex(
            session,
            "PRIMARY_KEY_" + table.GetTableName(),
            session.GetDatabase().AllocateObjectId(),
            keyColumns,
            keyColumns.Length,
            Indexes.IndexType.CreatePrimaryKey(session.GetDatabase().IsPersistent(), primaryKeyHash),
            create: true,
            indexComment: null);
        index.GetIndexType()!.SetBelongsToConstraint(true);
        return 0;
    }

    private static IndexColumn[] MapColumns(Table table, IndexColumn[] columns)
    {
        Column[] tableColumns = table.GetColumns();
        IndexColumn[] mapped = new IndexColumn[columns.Length];
        for (int i = 0; i < columns.Length; i++)
        {
            string name = columns[i].GetName();
            Column? match = null;
            foreach (Column candidate in tableColumns)
            {
                if (candidate.GetName().Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    match = candidate;
                    break;
                }
            }

            if (match is null)
            {
                throw DbException.Get(Api.ErrorCode.ColumnNotFound1, name);
            }

            mapped[i] = new IndexColumn(match);
        }

        return mapped;
    }

    /// <summary>
    /// Sets what happens when a row this one points at is deleted.
    /// </summary>
    /// <param name="action">the action</param>
    public void SetDeleteAction(ConstraintActionType action)
    {
        deleteAction = action;
    }

    /// <summary>
    /// Sets what happens when a row this one points at is updated.
    /// </summary>
    /// <param name="action">the action</param>
    public void SetUpdateAction(ConstraintActionType action)
    {
        updateAction = action;
    }

    /// <summary>
    /// Sets the name of the constraint.
    /// </summary>
    /// <param name="constraintName">the name</param>
    public void SetConstraintName(string constraintName)
    {
        this.constraintName = constraintName;
    }

    /// <summary>
    /// Gets the name of the constraint.
    /// </summary>
    /// <returns>the name</returns>
    public string? GetConstraintName() => constraintName;

    /// <summary>
    /// Whether this constraint refers to the table being changed.
    /// </summary>
    /// <returns>true if it does</returns>
    public bool IsSelfReferencing() => selfReferencing;

    /// <summary>
    /// Sets that this constraint refers to the table being changed.
    /// </summary>
    public void SetSelfReferencing() => selfReferencing = true;

    /// <inheritdoc/>
    public override int GetCommandType() => type;

    /// <summary>
    /// Whether the statement is IF NOT EXISTS.
    /// </summary>
    /// <returns>true if it is</returns>
    public bool IsIfNotExists() => ifNotExists;

    /// <summary>
    /// Sets whether NULL values are distinct in a UNIQUE constraint.
    /// </summary>
    /// <param name="nullsDistinct">the rule</param>
    public void SetNullsDistinct(NullsDistinct nullsDistinct)
    {
        this.nullsDistinct = nullsDistinct;
    }

    /// <summary>
    /// Sets the condition of a CHECK constraint.
    /// </summary>
    /// <param name="expression">the condition</param>
    public void SetCheckExpression(Expression expression)
    {
        checkExpression = expression;
    }

    /// <summary>
    /// Sets the columns this constraint covers.
    /// </summary>
    /// <param name="indexColumns">the columns</param>
    public void SetIndexColumns(IndexColumn[] indexColumns)
    {
        this.indexColumns = indexColumns;
    }

    /// <summary>
    /// Gets the columns this constraint covers.
    /// </summary>
    /// <returns>the columns</returns>
    public IndexColumn[] GetIndexColumns() => indexColumns;

    /// <summary>
    /// Sets the table and the columns a foreign key points at.
    /// </summary>
    /// <param name="refSchema">the schema of the other table</param>
    /// <param name="ref">the name of the other table</param>
    public void SetRefTableName(Schema refSchema, string refTableName)
    {
        this.refSchema = refSchema;
        this.refTableName = refTableName;
    }

    /// <summary>
    /// Sets the columns of the other table that the foreign key points at.
    /// </summary>
    /// <param name="indexColumns">the columns</param>
    public void SetRefIndexColumns(IndexColumn[] indexColumns)
    {
        refIndexColumns = indexColumns;
    }

    /// <summary>
    /// Sets the index of this constraint.
    /// </summary>
    /// <param name="index">the index</param>
    public void SetIndex(H2Sharp.Indexes.Index index)
    {
        this.index = index;
    }

    /// <summary>
    /// Sets the index of the other table that the foreign key points at.
    /// </summary>
    /// <param name="refIndex">the index</param>
    public void SetRefIndex(H2Sharp.Indexes.Index refIndex)
    {
        this.refIndex = refIndex;
    }

    /// <summary>
    /// Sets the comment on the constraint.
    /// </summary>
    /// <param name="comment">the comment</param>
    public void SetComment(string comment)
    {
        this.comment = comment;
    }

    /// <summary>
    /// Sets whether an existing constraint of the same name is an error.
    /// </summary>
    /// <param name="b">true to fail on an existing one</param>
    public void SetCheckExisting(bool b)
    {
        checkExisting = b;
    }

    /// <summary>
    /// Sets whether the primary key is a hash index.
    /// </summary>
    /// <param name="b">true for a hash index</param>
    public void SetPrimaryKeyHash(bool b)
    {
        primaryKeyHash = b;
    }
}
