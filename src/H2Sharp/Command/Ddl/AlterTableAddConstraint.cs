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
 * Ported: PRIMARY KEY, UNIQUE, CHECK and both FOREIGN KEY kinds. Domain constraints are a
 * separate statement (AlterDomainAddConstraint).
 */
using System;
using System.Collections.Generic;
using System.Text;
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

    private IndexColumn[]? refIndexColumns;

    private Schema? refSchema;

    private string? refTableName;

    private H2Sharp.Indexes.Index? index;

    private H2Sharp.Indexes.Index? refIndex;

    private string? comment;

    private bool checkExisting;

    private bool primaryKeyHash;

    private readonly List<H2Sharp.Indexes.Index> createdIndexes = [];

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
    /// The wrapper owns what the work itself cannot: cleanup. A constraint that fails halfway
    /// has already made an index, and possibly another constraint, and the reference removes
    /// whatever was made before it rethrows. The generated name is always released — it was
    /// reserved from the schema's unique-name set while the statement ran, and a failure that
    /// kept it reserved would make the same statement fail differently the second time.
    /// </remarks>
    public override long Update(Table table)
    {
        try
        {
            return TryUpdate(table);
        }
        catch (H2SharpException e)
        {
            _ = e;
            try
            {
                // The reference also removes the unique constraint an unported referential
                // branch would have made for its referenced columns; the primary key makes no
                // such constraint, so what can need cleanup here is an index.
                foreach (H2Sharp.Indexes.Index index in createdIndexes)
                {
                    GetSession().GetDatabase().RemoveSchemaObject(GetSession(), index);
                }
            }
            catch
            {
                // The reference records the secondary failure as suppressed; C# exceptions
                // have no suppressed list, and the original failure is what the caller sees.
            }

            throw;
        }
        finally
        {
            GetSchema().FreeUniqueName(constraintName);
        }
    }

    /// <summary>
    /// Try to execute the statement.
    /// </summary>
    /// <remarks>
    /// The primary key and the foreign key run; UNIQUE and CHECK need the constraint machinery
    /// that has not been ported, so the switch answers them the way the reference answers an
    /// unknown type — internally. The primary key creates its index and then the ConstraintUnique
    /// that owns it, and the pair is what the schema registers: the index enforces uniqueness, the
    /// constraint carries the name that the duplicate-key message prints and DROP releases.
    /// </remarks>
    /// <param name="table">the table</param>
    /// <returns>the update count</returns>
    private int TryUpdate(Table table)
    {
        if (constraintName is not null && GetSchema().FindConstraint(GetSession(), constraintName) is not null)
        {
            if (ifNotExists)
            {
                return 0;
            }

            // The reference's quirks-mode branch renames the constraint instead of failing;
            // session quirks mode is not ported and is false by default, so the error is the
            // answer, as it is on a default JVM.
            throw DbException.Get(Api.ErrorCode.ConstraintAlreadyExists1, constraintName);
        }

        Engine.Database db = GetSession().GetDatabase();
        _ = db.LockMeta(GetSession());
        _ = table.Lock(GetSession(), Table.ExclusiveLock);
        Constraint constraint;
        switch (type)
        {
            case CommandInterface.AlterTableAddConstraintPrimaryKey:
                {
                    IndexColumn[] keyColumns = IndexColumn.MapColumns(indexColumns, table);
                    index = table.FindPrimaryKey();
                    foreach (Constraint c in table.GetConstraints())
                    {
                        if (c.GetConstraintType() == Constraint.Type.PRIMARY_KEY)
                        {
                            throw DbException.Get(Api.ErrorCode.SecondPrimaryKey);
                        }
                    }

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
                            GetSession(), table, Engine.Constants.PrefixPrimaryKey);
                        int indexId = db.AllocateObjectId();
                        try
                        {
                            index = table.AddIndex(GetSession(), indexName, indexId, keyColumns,
                                keyColumns.Length, indexType, create: true, indexComment: null);
                        }
                        finally
                        {
                            GetSchema().FreeUniqueName(indexName);
                        }
                    }

                    index.GetIndexType()!.SetBelongsToConstraint(true);
                    int id = GetObjectId();
                    string name = GenerateConstraintName(table);
                    constraint = new ConstraintUnique(GetSchema(), id, name, table, primaryKey: true,
                        keyColumns, index, indexOwner: true, nullsDistinct: null);
                    break;
                }

            case CommandInterface.AlterTableAddConstraintReferential:
                {
                    // The rights check of the reference waits for the user layer: refusing here
                    // would refuse every foreign key on a database that has no users yet. The
                    // canReference question has the same fate: every table here is a plain table,
                    // and a view that cannot be referenced does not exist yet.
                    Table? refTable = refSchema?.FindTableOrView(GetSession(), refTableName!);
                    if (refTable is null)
                    {
                        throw DbException.Get(Api.ErrorCode.TableOrViewNotFound1, refTableName);
                    }

                    if (table.IsTemporary() != refTable.IsTemporary())
                    {
                        throw DbException.GetUnsupportedException(
                            "referential constraint uses temporary table " + GetTableName());
                    }

                    bool isOwner = false;
                    IndexColumn[] keyColumns = IndexColumn.MapColumns(indexColumns, table);
                    IndexColumn[] refKeyColumns;
                    if (refIndexColumns is null)
                    {
                        refKeyColumns = refTable.GetPrimaryKey().GetColumns();
                    }
                    else
                    {
                        refKeyColumns = IndexColumn.MapColumns(refIndexColumns, refTable);
                    }

                    int columnCount = keyColumns.Length;
                    if (refKeyColumns.Length != columnCount)
                    {
                        throw DbException.Get(Api.ErrorCode.ColumnCountDoesNotMatch);
                    }

                    foreach (IndexColumn indexColumn in keyColumns)
                    {
                        Column column = indexColumn.GetColumn();
                        if (column.IsGeneratedAlways())
                        {
                            switch (deleteAction)
                            {
                                case ConstraintActionType.SET_DEFAULT:
                                case ConstraintActionType.SET_NULL:
                                    throw DbException.Get(
                                        Api.ErrorCode.GeneratedColumnCannotBeUpdatableByConstraint2,
                                        column.GetSqlWithTable(
                                            new StringBuilder(), Util.HasSql.TraceSqlFlags).ToString(),
                                        "ON DELETE " + ConstraintActionTypeRules.GetSqlName(deleteAction));
                                default:
                                    break;
                            }

                            switch (updateAction)
                            {
                                case ConstraintActionType.CASCADE:
                                case ConstraintActionType.SET_DEFAULT:
                                case ConstraintActionType.SET_NULL:
                                    throw DbException.Get(
                                        Api.ErrorCode.GeneratedColumnCannotBeUpdatableByConstraint2,
                                        column.GetSqlWithTable(
                                            new StringBuilder(), Util.HasSql.TraceSqlFlags).ToString(),
                                        "ON UPDATE " + ConstraintActionTypeRules.GetSqlName(updateAction));
                                default:
                                    break;
                            }
                        }
                    }

                    for (int i = 0; i < columnCount; i++)
                    {
                        Column column1 = keyColumns[i].GetColumn();
                        Column column2 = refKeyColumns[i].GetColumn();
                        if (!Values.DataType.AreStableComparable(column1.GetTypeInfo(), column2.GetTypeInfo()))
                        {
                            throw DbException.Get(Api.ErrorCode.UncomparableReferencedColumn2,
                                column1.GetCreateSql(), column2.GetCreateSql());
                        }
                    }

                    ConstraintUnique? unique = GetUniqueConstraint(refTable, refKeyColumns);
                    if (unique is null)
                    {
                        // The reference creates the missing unique constraint when the mode asks
                        // for it; the mode flag is not ported, so the answer is the default JVM
                        // one: the referenced columns must already be a key.
                        throw DbException.Get(Api.ErrorCode.ConstraintNotFound1,
                            IndexColumn.WriteColumns(
                                new StringBuilder("PRIMARY KEY | UNIQUE ("), refKeyColumns,
                                Util.HasSql.TraceSqlFlags).Append(')').ToString());
                    }

                    if (index is not null && CanUseIndex(index, table, keyColumns, null))
                    {
                        isOwner = true;
                        index.GetIndexType()!.SetBelongsToConstraint(true);
                    }
                    else
                    {
                        H2Sharp.Indexes.Index? existing = GetIndex(table, keyColumns, null);
                        if (existing is null)
                        {
                            index = CreateConstraintIndex(table, keyColumns);
                            isOwner = true;
                        }
                        else
                        {
                            index = existing;
                        }
                    }

                    int refId = db.AllocateObjectId();
                    string refName = GenerateConstraintName(table);
                    ConstraintReferential refConstraint = new(GetSchema(), refId, refName, table);
                    refConstraint.SetColumns(keyColumns);
                    refConstraint.SetIndex(index, isOwner);
                    refConstraint.SetRefTable(refTable);
                    refConstraint.SetRefColumns(refKeyColumns);
                    refConstraint.SetRefConstraint(unique);
                    if (checkExisting)
                    {
                        refConstraint.CheckExistingData(GetSession());
                    }

                    refTable.AddConstraint(refConstraint);
                    refConstraint.SetDeleteAction(deleteAction);
                    refConstraint.SetUpdateAction(updateAction);
                    constraint = refConstraint;
                    break;
                }

            case CommandInterface.AlterTableAddConstraintUnique:
                {
                    if (indexColumns is null)
                    {
                        List<IndexColumn> list = [];
                        foreach (Column column in table.GetColumns())
                        {
                            if (!column.GetVisible()) continue;
                            list.Add(new IndexColumn(column));
                        }

                        if (list.Count == 0)
                        {
                            throw DbException.Get(Api.ErrorCode.SyntaxError1,
                                "UNIQUE(VALUE) on table without columns");
                        }

                        indexColumns = [.. list];
                    }
                    else
                    {
                        indexColumns = IndexColumn.MapColumns(indexColumns, table);
                    }

                    constraint = CreateUniqueConstraint(table, index, indexColumns, false);
                    break;
                }

            case CommandInterface.AlterTableAddConstraintCheck:
                {
                    HashSet<object> dependencies = [];
                    checkExpression!.IsEverything(ExpressionVisitor.GetDependenciesVisitor(dependencies));
                    foreach (object dependency in dependencies)
                    {
                        if (dependency is DbObject dbObject && table.IsTemporary() != dbObject.IsTemporary())
                        {
                            throw DbException.GetUnsupportedException(
                                "check constraint uses temporary object " + dbObject.GetName());
                        }
                    }

                    ConstraintCheck check = new(GetSchema(), GetObjectId(), GenerateConstraintName(table), table);
                    // Rights are not ported (D-004); the table was resolved by this statement.
                    TableFilter checkFilter = new(GetSession(), table, null, rightsChecked: true, null, 0, null);
                    checkExpression!.MapColumns(checkFilter, 0, Expression.MapInitial);
                    checkExpression = checkExpression.Optimize(GetSession());
                    check.SetExpression(checkExpression);
                    check.SetTableFilter(checkFilter);
                    constraint = check;
                    if (checkExisting)
                    {
                        check.CheckExistingData(GetSession());
                    }

                    break;
                }

            default:
                throw new TODOException("AlterTableAddConstraint.update: this constraint kind is not ported");
        }

        // parent relationship is already set with addConstraint
        constraint.SetComment(comment);
        AddConstraintToTable(db, table, constraint);
        return 0;
    }

    /// <summary>
    /// The name of the constraint: the one the statement wrote, or a fresh one from the schema.
    /// </summary>
    /// <param name="table">the table</param>
    /// <returns>the name</returns>
    private string GenerateConstraintName(Table table)
    {
        if (constraintName is null)
        {
            constraintName = GetSchema().GetUniqueConstraintName(GetSession(), table);
        }

        return constraintName;
    }

    /// <summary>
    /// Registers the constraint in the database and adds it to the table.
    /// </summary>
    /// <remarks>
    /// The order is the reference's: the schema first, then the table. A table that held a
    /// constraint the schema does not know would be one that DROP TABLE could not find and
    /// release.
    /// </remarks>
    /// <param name="db">the database</param>
    /// <param name="table">the table</param>
    /// <param name="constraint">the constraint</param>
    private void AddConstraintToTable(Engine.Database db, Table table, Constraint constraint)
    {
        if (table.IsTemporary() && !table.IsGlobalTemporary())
        {
            GetSession().AddLocalTempTableConstraint(constraint);
            table.AddConstraint(constraint);
            return;
        }

        db.AddSchemaObject(GetSession(), constraint);
        table.AddConstraint(constraint);
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
    public void SetRefIndex(H2Sharp.Indexes.Index? refIndex)
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

    /// <summary>Creates a UNIQUE constraint, reusing or creating its unique index.</summary>
    private ConstraintUnique CreateUniqueConstraint(Table table, H2Sharp.Indexes.Index? existingIndex,
        IndexColumn[] columns, bool forForeignKey)
    {
        NullsDistinct needNullsDistinct = nullsDistinct ?? NullsDistinct.Distinct;
        bool indexOwner = false;
        H2Sharp.Indexes.Index uniqueIndex;
        if (existingIndex is not null && CanUseIndex(existingIndex, table, columns, needNullsDistinct))
        {
            indexOwner = true;
            existingIndex.GetIndexType()!.SetBelongsToConstraint(true);
            uniqueIndex = existingIndex;
        }
        else
        {
            H2Sharp.Indexes.Index? found = GetIndex(table, columns, needNullsDistinct);
            if (found is not null)
            {
                uniqueIndex = found;
            }
            else
            {
                uniqueIndex = CreateConstraintIndex(table, columns, nullsDistinct ?? NullsDistinct.Distinct);
                indexOwner = true;
            }
        }

        if (columns.Length == 1 && needNullsDistinct == NullsDistinct.AllDistinct)
        {
            needNullsDistinct = NullsDistinct.Distinct;
        }

        return new ConstraintUnique(GetSchema(), GetObjectId(), GenerateConstraintName(table), table,
            primaryKey: false, columns, uniqueIndex, indexOwner, needNullsDistinct);
    }

    /// <summary>
    /// The unique constraint of the table whose index the given columns can use, or null.
    /// </summary>
    /// <param name="table">the table</param>
    /// <param name="cols">the columns</param>
    /// <returns>the constraint, or null</returns>
    private static ConstraintUnique? GetUniqueConstraint(Table table, IndexColumn[] cols)
    {
        foreach (Constraint constraint in table.GetConstraints())
        {
            if (ReferenceEquals(constraint.GetTable(), table))
            {
                if (ConstraintTypeRules.IsUnique(constraint.GetConstraintType()))
                {
                    if (CanUseIndex(constraint.GetIndex()!, table, cols, Engine.NullsDistinct.Distinct))
                    {
                        return (ConstraintUnique)constraint;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The index of the table the given columns can use, or null.
    /// </summary>
    /// <param name="table">the table</param>
    /// <param name="cols">the columns</param>
    /// <param name="nullsDistinct">how NULLs compare, or null for a non-unique index</param>
    /// <returns>the index, or null</returns>
    private static H2Sharp.Indexes.Index? GetIndex(Table table, IndexColumn[] cols, Engine.NullsDistinct? nullsDistinct)
    {
        H2Sharp.Indexes.Index? result = null;
        foreach (H2Sharp.Indexes.Index idx in table.GetIndexes())
        {
            if (CanUseIndex(idx, table, cols, nullsDistinct))
            {
                if (result is null || idx.GetColumns().Length < result.GetColumns().Length)
                {
                    result = idx;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Whether the index serves the given columns.
    /// </summary>
    /// <param name="index">the index</param>
    /// <param name="table">the table</param>
    /// <param name="cols">the columns</param>
    /// <param name="nullsDistinct">how NULLs compare, or null for a non-unique index</param>
    /// <returns>true when it does</returns>
    private static bool CanUseIndex(
        H2Sharp.Indexes.Index index, Table table, IndexColumn[] cols, Engine.NullsDistinct? nullsDistinct)
    {
        if (!ReferenceEquals(index.GetTable(), table))
        {
            return false;
        }

        int allowedColumns;
        if (nullsDistinct is not null)
        {
            allowedColumns = index.GetUniqueColumnCount();
            if (allowedColumns != cols.Length)
            {
                return false;
            }

            if (index.GetIndexType()?.GetEffectiveNullsDistinct()?.CompareTo(nullsDistinct.Value) < 0)
            {
                return false;
            }
        }
        else
        {
            if (index.GetCreateSql() is null || (allowedColumns = index.GetColumns().Length) != cols.Length)
            {
                return false;
            }
        }

        foreach (IndexColumn col in cols)
        {
            // all columns of the list must be part of the index
            int i = index.GetColumnIndex(col.GetColumn());
            if (i < 0 || i >= allowedColumns)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Creates the index the foreign key walks.
    /// </summary>
    /// <remarks>
    /// Named CreateConstraintIndex rather than the reference's createIndex: a class of the
    /// statement's own namespace is already called CreateIndex, and a method of the same name
    /// would not resolve. The body is the reference's.
    /// </remarks>
    /// <param name="table">the table</param>
    /// <param name="cols">the columns</param>
    /// <returns>the index</returns>
    private H2Sharp.Indexes.Index CreateConstraintIndex(Table table, IndexColumn[] cols)
        => CreateConstraintIndex(table, cols, null);

    private H2Sharp.Indexes.Index CreateConstraintIndex(Table table, IndexColumn[] cols,
        NullsDistinct? nullsDistinct)
    {
        int indexId = GetSession().GetDatabase().AllocateObjectId();
        Indexes.IndexType indexType = nullsDistinct is null
            ? Indexes.IndexType.CreateNonUnique(table.IsPersistIndexes())
            : Indexes.IndexType.CreateUnique(table.IsPersistIndexes(), false, cols.Length, nullsDistinct.Value);
        indexType.SetBelongsToConstraint(true);
        string prefix = constraintName is null ? "CONSTRAINT" : constraintName;
        string indexName = table.GetSchema()!.GetUniqueIndexName(GetSession(), table, prefix + "_INDEX_");
        try
        {
            H2Sharp.Indexes.Index index = table.AddIndex(
                GetSession(), indexName, indexId, cols,
                uniqueColumnCount: nullsDistinct is null ? 0 : cols.Length,
                indexType, create: true, indexComment: null);
            createdIndexes.Add(index);
            return index;
        }
        finally
        {
            GetSchema().FreeUniqueName(indexName);
        }
    }
}
