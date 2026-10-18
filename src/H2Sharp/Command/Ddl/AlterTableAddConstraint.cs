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
 * A referential constraint that names the statement's own table is self-referencing, and that
 * is a question about names, not lookup: the table may not exist yet, so the answer is whether
 * the referenced schema is this schema and the two names are the same identifier.
 *
 * Not ported: everything but the primary key needs the constraint machinery.
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
    /// it would own is, which is what enforces uniqueness and answers MIN and MAX. The
    /// constraint-name checks and the constraint list of the reference's tryUpdate are part of
    /// that layer: there is no constraint object to collide with or to iterate.
    /// </remarks>
    public override long Update(Table table)
    {
        if (type != CommandInterface.AlterTableAddConstraintPrimaryKey)
        {
            throw new TODOException("AlterTableAddConstraint.update: constraints are not ported");
        }

        SessionLocal session = GetSession();
        _ = session.GetDatabase().LockMeta(session);
        _ = table.Lock(session, Table.ExclusiveLock);
        IndexColumn[] keyColumns = MapColumns(table, indexColumns);
        index = table.FindPrimaryKey();
        if (index is not null)
        {
            // if there is an index, it must match with the one declared
            // we don't test ascending / descending
            IndexColumn[] pkCols = index.GetColumns();
            if (pkCols.Length != keyColumns.Length)
            {
                throw DbException.Get(Api.ErrorCode.SecondPrimaryKey);
            }

            for (int i = 0; i < pkCols.Length; i++)
            {
                if (!ReferenceEquals(pkCols[i].GetColumn(), keyColumns[i].GetColumn()))
                {
                    throw DbException.Get(Api.ErrorCode.SecondPrimaryKey);
                }
            }
        }
        else
        {
            Indexes.IndexType indexType = Indexes.IndexType.CreatePrimaryKey(
                table.IsPersistIndexes(), primaryKeyHash);
            string indexName = table.GetSchema()!.GetUniqueIndexName(
                session, table, Engine.Constants.PrefixPrimaryKey);
            int indexId = session.GetDatabase().AllocateObjectId();
            try
            {
                index = table.AddIndex(session, indexName, indexId, keyColumns, keyColumns.Length,
                    indexType, create: true, indexComment: null);
            }
            finally
            {
                GetSchema().FreeUniqueName(indexName);
            }
        }

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
    /// <remarks>
    /// Only a referential constraint can be self-referencing, and it is one when the schema it
    /// points at is this statement's schema and the two table names are the same identifier.
    /// The check is about the statement's text, not about a table: the table may not exist yet.
    /// </remarks>
    /// <returns>true if it does</returns>
    public bool IsSelfReferencing()
    {
        return type == CommandInterface.AlterTableAddConstraintReferential && refSchema == GetSchema()
            && refTableName is not null
            && GetSession().GetDatabase().EqualsIdentifiers(GetTableName(), refTableName);
    }

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
