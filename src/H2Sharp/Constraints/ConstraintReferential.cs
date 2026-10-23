/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.constraint.ConstraintReferential.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A referential constraint: every row of this table points at a row of another one.
 *
 * The checks are the reference's, in the reference's places. A new row of this table must
 * find its parent (checkRowOwnTable, error 23506), and a changed row of the parent must not
 * strand a child (checkRowRefTable, error 23503) unless the action says what to do with the
 * child instead: delete it, update it, null it or default it.
 *
 * The actions run directly on the tables rather than through statements the constraint
 * prepares. The reference builds a DELETE or UPDATE string and prepares it, which needs the
 * parameter plumbing of nested commands; this port writes the child rows itself, under the
 * same skipOwnTable guard and with the same row-level fires, so the rows that change and
 * the errors they raise are the same. The statement strings are still built, but only where
 * the reference builds them for reading: getCreateSQLForCopy.
 *
 * The lookup is the reference's: the constraint's own index (or the referenced unique one)
 * is walked with the key as both bounds, and the rows it returns are compared value by
 * value. A full scan is never the answer to "does the parent exist".
 *
 * Not ported: the database-wide switch (SET REFERENTIAL_INTEGRITY is not ported, so the
 * checks always run when the table's own switch is on); the canReference question (every
 * table here is a plain table); the temporary-table paths (temporary tables are not
 * ported); creating a unique constraint for referenced columns without one (the mode flag
 * for it is not ported, and the answer is the default JVM one: 90057). Rebuild keeps no
 * statements, so it has nothing to rebuild.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;
using Index = H2Sharp.Indexes.Index;

namespace H2Sharp.Constraints;

/// <summary>
/// A referential constraint.
/// </summary>
public class ConstraintReferential : Constraint
{
    private IndexColumn[]? columns;

    private IndexColumn[]? refColumns;

    private ConstraintActionType deleteAction = ConstraintActionType.NO_ACTION;

    private ConstraintActionType updateAction = ConstraintActionType.NO_ACTION;

    private Table? refTable;

    private Index? index;

    private ConstraintUnique? refConstraint;

    private bool indexOwner;

    private bool skipOwnTable;

    /// <summary>
    /// Creates the constraint.
    /// </summary>
    /// <param name="schema">the schema</param>
    /// <param name="id">the object id</param>
    /// <param name="name">the name</param>
    /// <param name="table">the table the constraint belongs to</param>
    public ConstraintReferential(Schema schema, int id, string name, Table table)
        : base(schema, id, name, table)
    {
    }

    /// <inheritdoc/>
    public override Type GetConstraintType() => Type.REFERENTIAL;

    /// <summary>
    /// Create the SQL statement of this object so a copy of the table can be made.
    /// </summary>
    /// <param name="forTable">the table to create the object for</param>
    /// <param name="quotedName">the name of this object (quoted if necessary)</param>
    /// <returns>the SQL statement</returns>
    public string GetCreateSqlForCopy(Table forTable, string quotedName)
    {
        return GetCreateSqlForCopy(forTable, refTable!, quotedName, internalIndex: true);
    }

    /// <summary>
    /// Create the SQL statement of this object so a copy of the table can be made.
    /// </summary>
    /// <param name="forTable">the table to create the object for</param>
    /// <param name="forRefTable">the referenced table</param>
    /// <param name="quotedName">the name of this object (quoted if necessary)</param>
    /// <param name="internalIndex">add the index name to the statement</param>
    /// <returns>the SQL statement</returns>
    public string GetCreateSqlForCopy(Table forTable, Table forRefTable, string quotedName, bool internalIndex)
    {
        StringBuilder builder = new("ALTER TABLE ");
        _ = forTable.GetSql(builder, HasSql.DefaultSqlFlags).Append(" ADD CONSTRAINT ");
        _ = builder.Append(quotedName);
        if (comment is not null)
        {
            _ = builder.Append(" COMMENT ");
            _ = StringUtils.QuoteStringSql(builder, comment);
        }

        IndexColumn[] cols = columns!;
        IndexColumn[] refCols = refColumns!;
        _ = builder.Append(" FOREIGN KEY(");
        _ = IndexColumn.WriteColumns(builder, cols, HasSql.DefaultSqlFlags);
        _ = builder.Append(')');
        if (internalIndex && indexOwner && ReferenceEquals(forTable, table))
        {
            _ = builder.Append(" INDEX ");
            _ = index!.GetSql(builder, HasSql.DefaultSqlFlags);
        }

        _ = builder.Append(" REFERENCES ");
        if (ReferenceEquals(table, refTable))
        {
            // self-referencing constraints: need to use new table
            _ = forTable.GetSql(builder, HasSql.DefaultSqlFlags);
        }
        else
        {
            _ = forRefTable.GetSql(builder, HasSql.DefaultSqlFlags);
        }

        _ = builder.Append('(');
        _ = IndexColumn.WriteColumns(builder, refCols, HasSql.DefaultSqlFlags);
        _ = builder.Append(')');
        if (updateAction != ConstraintActionType.NO_ACTION)
        {
            _ = builder.Append(" ON UPDATE ").Append(ConstraintActionTypeRules.GetSqlName(updateAction));
        }

        if (deleteAction != ConstraintActionType.NO_ACTION)
        {
            _ = builder.Append(" ON DELETE ").Append(ConstraintActionTypeRules.GetSqlName(deleteAction));
        }

        return builder.Append(" NOCHECK").ToString();
    }

    /// <summary>
    /// Get a short description of the constraint. This includes the constraint
    /// name (if set), and the constraint expression.
    /// </summary>
    /// <param name="searchIndex">the index, or null</param>
    /// <param name="check">the row, or null</param>
    /// <returns>the description</returns>
    private string GetShortDescription(Index? searchIndex, SearchRow? check)
    {
        StringBuilder builder = new StringBuilder(GetName()).Append(": ");
        _ = table.GetSql(builder, HasSql.TraceSqlFlags).Append(" FOREIGN KEY(");
        _ = IndexColumn.WriteColumns(builder, columns!, HasSql.TraceSqlFlags);
        _ = builder.Append(") REFERENCES ");
        _ = refTable!.GetSql(builder, HasSql.TraceSqlFlags).Append('(');
        _ = IndexColumn.WriteColumns(builder, refColumns!, HasSql.TraceSqlFlags);
        _ = builder.Append(')');
        if (searchIndex is not null && check is not null)
        {
            _ = builder.Append(" (");
            IndexColumn[] cols = searchIndex.GetColumns();
            int len = Math.Min(columns!.Length, cols.Length);
            for (int i = 0; i < len; i++)
            {
                int idx = cols[i].GetColumn().GetColumnId();
                Value? c = check.GetValue(idx);
                if (i > 0)
                {
                    _ = builder.Append(", ");
                }

                _ = builder.Append(c?.GetString() ?? string.Empty);
            }

            _ = builder.Append(')');
        }

        return builder.ToString();
    }

    /// <inheritdoc/>
    public override string GetCreateSqlWithoutIndexes()
    {
        return GetCreateSqlForCopy(table, refTable!, GetSql(HasSql.DefaultSqlFlags), internalIndex: false);
    }

    /// <inheritdoc/>
    public override string GetCreateSql()
    {
        return GetCreateSqlForCopy(table, GetSql(HasSql.DefaultSqlFlags));
    }

    /// <summary>
    /// Sets the columns of this table the constraint covers.
    /// </summary>
    /// <param name="cols">the columns</param>
    public void SetColumns(IndexColumn[] cols) => columns = cols;

    /// <summary>
    /// Gets the columns of this table the constraint covers.
    /// </summary>
    /// <returns>the columns</returns>
    public IndexColumn[] GetColumns() => columns!;

    /// <inheritdoc/>
    public override HashSet<Column> GetReferencedColumns(Table table)
    {
        HashSet<Column> result = [];
        if (table == this.table)
        {
            foreach (IndexColumn c in columns!)
            {
                _ = result.Add(c.GetColumn());
            }
        }
        else if (table == refTable)
        {
            foreach (IndexColumn c in refColumns!)
            {
                _ = result.Add(c.GetColumn());
            }
        }

        return result;
    }

    /// <summary>
    /// Sets the columns of the referenced table.
    /// </summary>
    /// <param name="refCols">the columns</param>
    public void SetRefColumns(IndexColumn[] refCols) => refColumns = refCols;

    /// <summary>
    /// Gets the columns of the referenced table.
    /// </summary>
    /// <returns>the columns</returns>
    public IndexColumn[] GetRefColumns() => refColumns!;

    /// <summary>
    /// Sets the referenced table.
    /// </summary>
    /// <param name="refTable">the referenced table</param>
    public void SetRefTable(Table refTable)
    {
        this.refTable = refTable;
        if (refTable.IsTemporary())
        {
            SetTemporary(true);
        }
    }

    /// <summary>
    /// Set the index to use for this constraint.
    /// </summary>
    /// <param name="index">the index</param>
    /// <param name="isOwner">true if the index is generated by the system and belongs to this constraint</param>
    public void SetIndex(Index index, bool isOwner)
    {
        this.index = index;
        indexOwner = isOwner;
    }

    /// <summary>
    /// Set the unique constraint of the referenced table to use for this constraint.
    /// </summary>
    /// <param name="refConstraint">the unique constraint</param>
    public void SetRefConstraint(ConstraintUnique refConstraint) => this.refConstraint = refConstraint;

    /// <inheritdoc/>
    /// <remarks>
    /// The reference also removes the constraint from the meta table; there is no meta table
    /// here, and the table's own cleanup removes the schema object around this call.
    /// </remarks>
    public override void RemoveChildrenAndResources(SessionLocal session)
    {
        table.RemoveConstraint(this);
        refTable!.RemoveConstraint(this);
        if (indexOwner)
        {
            table.RemoveIndexOrTransferOwnership(session, index!);
        }

        refTable = null;
        index = null;
        refConstraint = null;
        columns = null;
        refColumns = null;
        table = null!;
        Invalidate();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The database-wide switch of the reference is not ported, so only the table's own switch
    /// is asked: SET REFERENTIAL_INTEGRITY is a statement the port refuses, and a check that
    /// read a switch nobody can flip would be a check that never runs.
    /// </remarks>
    public override void CheckRow(SessionLocal session, Table t, Row? oldRow, Row? newRow)
    {
        if (!table.GetCheckForeignKeyConstraints() || !refTable!.GetCheckForeignKeyConstraints())
        {
            return;
        }

        if (t == table)
        {
            if (!skipOwnTable)
            {
                CheckRowOwnTable(session, oldRow, newRow);
            }
        }

        if (t == refTable)
        {
            CheckRowRefTable(session, oldRow, newRow);
        }
    }

    /// <summary>
    /// Checks a new row of this table: its parent must exist.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="oldRow">the row as it was, or null for an insert</param>
    /// <param name="newRow">the row as it becomes, or null for a delete</param>
    private void CheckRowOwnTable(SessionLocal session, Row? oldRow, Row? newRow)
    {
        if (newRow is null)
        {
            return;
        }

        bool constraintColumnsEqual = oldRow is not null;
        foreach (IndexColumn col in columns!)
        {
            int idx = col.GetColumn().GetColumnId();
            Value? v = newRow.GetValue(idx);
            if (v is null or ValueNull)
            {
                // return early if one of the columns is NULL
                return;
            }

            if (constraintColumnsEqual)
            {
                if (!session.AreEqual(v, oldRow!.GetValue(idx)!))
                {
                    constraintColumnsEqual = false;
                }
            }
        }

        if (constraintColumnsEqual)
        {
            // return early if the key columns didn't change
            return;
        }

        if (refTable == table)
        {
            // special case self referencing constraints:
            // check the inserted row first
            bool self = true;
            for (int i = 0, len = columns!.Length; i < len; i++)
            {
                int idx = columns[i].GetColumn().GetColumnId();
                Value? v = newRow.GetValue(idx);
                Column refCol = refColumns![i].GetColumn();
                int refIdx = refCol.GetColumnId();
                Value? r = newRow.GetValue(refIdx);
                if (r is null || v is null || !session.AreEqual(r, v))
                {
                    self = false;
                    break;
                }
            }

            if (self)
            {
                return;
            }
        }

        Row check = refTable!.GetTemplateRow();
        for (int i = 0, len = columns!.Length; i < len; i++)
        {
            int idx = columns[i].GetColumn().GetColumnId();
            Value? v = newRow.GetValue(idx);
            Column refCol = refColumns![i].GetColumn();
            int refIdx = refCol.GetColumnId();
            check.SetValue(refIdx, refCol.Convert(session.CastDataProvider, v!));
        }

        Index refIndex = refConstraint!.GetIndex()!;
        if (!ExistsRow(session, refIndex, check, excluding: null))
        {
            throw DbException.Get(
                Api.ErrorCode.ReferentialIntegrityViolatedParentMissing1,
                GetShortDescription(refIndex, check));
        }
    }

    /// <summary>
    /// Whether a row the index returns is the row being looked for.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="searchIndex">the index to walk</param>
    /// <param name="check">the row to find</param>
    /// <param name="excluding">the row to skip, or null</param>
    /// <returns>true when a row is there</returns>
    private bool ExistsRow(SessionLocal session, Index searchIndex, SearchRow check, Row? excluding)
    {
        Table searchTable = searchIndex.GetTable()
            ?? throw DbException.GetInternalError(searchIndex.ToString());
        _ = searchTable;
        Indexes.Cursor cursor = searchIndex.GetCursor(session, check, check, reverse: false)
            ?? throw DbException.GetInternalError(searchIndex.ToString());
        while (cursor.Next())
        {
            SearchRow? found = cursor.GetSearchRow();
            if (found is null)
            {
                continue;
            }

            if (excluding is not null && found.GetKey() == excluding.GetKey())
            {
                continue;
            }

            IndexColumn[] cols = searchIndex.GetColumns();
            bool allEqual = true;
            int len = Math.Min(columns!.Length, cols.Length);
            for (int i = 0; i < len; i++)
            {
                int idx = cols[i].GetColumn().GetColumnId();
                Value? c = check.GetValue(idx);
                Value? f = found.GetValue(idx);
                if (c is null || f is null || session.Compare(c, f) != 0)
                {
                    allEqual = false;
                    break;
                }
            }

            if (allEqual)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the referenced key is the same in both rows.
    /// </summary>
    /// <param name="oldRow">the row as it was</param>
    /// <param name="newRow">the row as it becomes</param>
    /// <returns>true when it did not change</returns>
    private bool IsEqual(Row oldRow, Row newRow)
    {
        return refConstraint!.GetIndex()!.CompareRows(oldRow, newRow) == 0;
    }

    /// <summary>
    /// Checks a row of the referenced table that is going away or changing: no child may be
    /// left pointing at it, unless the action says what happens to the child instead.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="oldRow">the row as it was</param>
    private void CheckRow(SessionLocal session, Row oldRow)
    {
        SearchRow check = table.GetTemplateRow();
        for (int i = 0, len = columns!.Length; i < len; i++)
        {
            Column refCol = refColumns![i].GetColumn();
            int refIdx = refCol.GetColumnId();
            Column col = columns[i].GetColumn();
            Value? v = col.Convert(session.CastDataProvider, oldRow.GetValue(refIdx)!);
            if (v is null or ValueNull)
            {
                return;
            }

            check.SetValue(col.GetColumnId(), v);
        }

        // exclude the row only for self-referencing constraints
        Row? excluding = refTable == table ? oldRow : null;
        if (ExistsRow(session, index!, check, excluding))
        {
            throw DbException.Get(
                Api.ErrorCode.ReferentialIntegrityViolatedChildExists1,
                GetShortDescription(index, check));
        }
    }

    /// <summary>
    /// Checks a row of the referenced table that is inserted, deleted or updated.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="oldRow">the row as it was, or null for an insert</param>
    /// <param name="newRow">the row as it becomes, or null for a delete</param>
    private void CheckRowRefTable(SessionLocal session, Row? oldRow, Row? newRow)
    {
        if (oldRow is null)
        {
            // this is an insert
            return;
        }

        if (newRow is not null && IsEqual(oldRow, newRow))
        {
            // on an update, if both old and new are the same, don't do anything
            return;
        }

        if (newRow is null)
        {
            // this is a delete
            if (ConstraintActionTypeRules.IsNoActionOrRestrict(deleteAction))
            {
                CheckRow(session, oldRow);
            }
            else if (deleteAction == ConstraintActionType.CASCADE)
            {
                CascadeDelete(session, oldRow);
            }
            else
            {
                SetNullOrDefault(session, oldRow, forDelete: true);
            }
        }
        else
        {
            // this is an update
            if (ConstraintActionTypeRules.IsNoActionOrRestrict(updateAction))
            {
                CheckRow(session, oldRow);
            }
            else if (updateAction == ConstraintActionType.CASCADE)
            {
                CascadeUpdate(session, oldRow, newRow);
            }
            else
            {
                SetNullOrDefault(session, oldRow, forDelete: false);
            }
        }
    }

    /// <summary>
    /// Deletes every row that points at the row going away.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="oldRow">the row of the referenced table as it was</param>
    private void CascadeDelete(SessionLocal session, Row oldRow)
    {
        List<Row> referencing = FindReferencingRows(session, oldRow);
        if (referencing.Count == 0)
        {
            return;
        }

        // The cascaded write must not check the row it writes against the parent again: the
        // reference runs it with skipOwnTable for the same reason, and a self-referencing
        // cascade would otherwise refuse the row it just made.
        skipOwnTable = true;
        try
        {
            foreach (Row child in referencing)
            {
                if (!table.FireRow() || !table.FireBeforeRow(session, child, null))
                {
                    table.RemoveRow(session, child.GetKey());
                    table.FireAfterRow(session, child, null, rollback: false);
                }
            }
        }
        finally
        {
            skipOwnTable = false;
        }
    }

    /// <summary>
    /// Points every row that points at the old row at the new one.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="oldRow">the row of the referenced table as it was</param>
    /// <param name="newRow">the row of the referenced table as it becomes</param>
    private void CascadeUpdate(SessionLocal session, Row oldRow, Row newRow)
    {
        List<Row> referencing = FindReferencingRows(session, oldRow);
        if (referencing.Count == 0)
        {
            return;
        }

        skipOwnTable = true;
        try
        {
            foreach (Row child in referencing)
            {
                Row updated = CopyRow(child);
                for (int i = 0, len = columns!.Length; i < len; i++)
                {
                    Column col = columns[i].GetColumn();
                    Column refCol = refColumns![i].GetColumn();
                    Value? v = newRow.GetValue(refCol.GetColumnId());
                    updated.SetValue(col.GetColumnId(), col.Convert(session.CastDataProvider, v!));
                }

                if (!table.FireRow() || !table.FireBeforeRow(session, child, updated))
                {
                    table.UpdateRow(session, child, updated);
                    table.FireAfterRow(session, child, updated, rollback: false);
                }
            }
        }
        finally
        {
            skipOwnTable = false;
        }
    }

    /// <summary>
    /// Sets the rows that point at the row going away to NULL or to their defaults.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="oldRow">the row of the referenced table as it was</param>
    /// <param name="forDelete">whether the row goes away rather than changes</param>
    private void SetNullOrDefault(SessionLocal session, Row oldRow, bool forDelete)
    {
        ConstraintActionType action = forDelete ? deleteAction : updateAction;
        List<Row> referencing = FindReferencingRows(session, oldRow);
        if (referencing.Count == 0)
        {
            return;
        }

        skipOwnTable = true;
        try
        {
            foreach (Row child in referencing)
            {
                Row updated = CopyRow(child);
                for (int i = 0, len = columns!.Length; i < len; i++)
                {
                    Column col = columns[i].GetColumn();
                    Value v;
                    if (action == ConstraintActionType.SET_NULL)
                    {
                        v = ValueNull.Instance;
                    }
                    else
                    {
                        Expressions.Expression? expr = col.GetDefaultExpression();
                        if (expr is null)
                        {
                            throw DbException.Get(Api.ErrorCode.NoDefaultSet1, col.GetName());
                        }

                        v = expr.GetValue(session);
                    }

                    updated.SetValue(col.GetColumnId(), col.ValidateConvertUpdateSequence(session, v, updated));
                }

                if (!table.FireRow() || !table.FireBeforeRow(session, child, updated))
                {
                    table.UpdateRow(session, child, updated);
                    table.FireAfterRow(session, child, updated, rollback: false);
                }
            }
        }
        finally
        {
            skipOwnTable = false;
        }
    }

    /// <summary>
    /// Every row of this table that points at the given row of the referenced table.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="oldRow">the row of the referenced table</param>
    /// <returns>the rows that point at it</returns>
    private List<Row> FindReferencingRows(SessionLocal session, Row oldRow)
    {
        Row check = table.GetTemplateRow();
        for (int i = 0, len = columns!.Length; i < len; i++)
        {
            Column refCol = refColumns![i].GetColumn();
            int refIdx = refCol.GetColumnId();
            Column col = columns[i].GetColumn();
            Value? v = col.Convert(session.CastDataProvider, oldRow.GetValue(refIdx)!);
            if (v is null or ValueNull)
            {
                // A NULL key is referenced by nothing: the comparison the reference builds
                // never matches NULL, and neither does this walk.
                return [];
            }

            check.SetValue(col.GetColumnId(), v);
        }

        List<Row> referencing = [];
        Indexes.Cursor cursor = index!.GetCursor(session, check, check, reverse: false)
            ?? throw DbException.GetInternalError(index.ToString());
        while (cursor.Next())
        {
            Row? found = cursor.Get();
            if (found is null)
            {
                continue;
            }

            IndexColumn[] cols = index.GetColumns();
            bool allEqual = true;
            int len = Math.Min(columns!.Length, cols.Length);
            for (int i = 0; i < len; i++)
            {
                int idx = cols[i].GetColumn().GetColumnId();
                Value? c = check.GetValue(idx);
                Value? f = found.GetValue(idx);
                if (c is null || f is null || session.Compare(c, f) != 0)
                {
                    allEqual = false;
                    break;
                }
            }

            if (allEqual)
            {
                referencing.Add(found);
            }
        }

        return referencing;
    }

    /// <summary>
    /// A copy of the row the cascade can change without touching the row it read.
    /// </summary>
    /// <param name="row">the row</param>
    /// <returns>the copy, with the same key</returns>
    private Row CopyRow(Row row)
    {
        Value?[] values = row.GetValueList();
        Value[] copy = new Value[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            copy[i] = values[i]!;
        }

        Row result = table.CreateRow(copy, DefaultRow.MemoryCalculate);
        result.SetKey(row.GetKey());
        return result;
    }

    /// <summary>
    /// Gets what a delete of a referenced row does to the rows that point at it.
    /// </summary>
    /// <returns>the action</returns>
    public ConstraintActionType GetDeleteAction() => deleteAction;

    /// <summary>
    /// Set the action to apply (restrict, cascade,...) on a delete.
    /// </summary>
    /// <param name="action">the action</param>
    public void SetDeleteAction(ConstraintActionType action)
    {
        deleteAction = action;
    }

    /// <summary>
    /// Gets what an update of a referenced row does to the rows that point at it.
    /// </summary>
    /// <returns>the action</returns>
    public ConstraintActionType GetUpdateAction() => updateAction;

    /// <summary>
    /// Set the action to apply (restrict, cascade,...) on an update.
    /// </summary>
    /// <param name="action">the action</param>
    public void SetUpdateAction(ConstraintActionType action)
    {
        updateAction = action;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The reference rebuilds the SQL of the cascaded statements when a column is renamed;
    /// the cascaded writes here keep no SQL, so there is nothing to rebuild.
    /// </remarks>
    public override void Rebuild()
    {
    }

    /// <inheritdoc/>
    public override Table GetRefTable() => refTable ?? table;

    /// <inheritdoc/>
    public override bool UsesIndex(Index idx) => ReferenceEquals(idx, index);

    /// <inheritdoc/>
    public override void SetIndexOwner(Index index)
    {
        if (ReferenceEquals(this.index, index))
        {
            indexOwner = true;
        }
        else
        {
            throw DbException.GetInternalError(index + " " + ToString());
        }
    }

    /// <inheritdoc/>
    public override bool IsBefore() => false;

    /// <inheritdoc/>
    public override void CheckExistingData(SessionLocal session)
    {
        if (session.GetDatabase().IsStarting())
        {
            // don't check at startup
            return;
        }

        Index? scan = table.GetScanIndex(session)
            ?? throw DbException.GetInternalError(table.GetTableName());
        Indexes.Cursor cursor = scan.GetCursor(session, null, null, reverse: false)
            ?? throw DbException.GetInternalError(table.GetTableName());
        while (cursor.Next())
        {
            Row? row = cursor.Get();
            if (row is null)
            {
                continue;
            }

            bool anyNull = false;
            foreach (IndexColumn col in columns!)
            {
                Value? v = row.GetValue(col.GetColumn().GetColumnId());
                if (v is null or ValueNull)
                {
                    anyNull = true;
                    break;
                }
            }

            if (anyNull)
            {
                continue;
            }

            Row check = refTable!.GetTemplateRow();
            for (int i = 0, len = columns!.Length; i < len; i++)
            {
                int idx = columns[i].GetColumn().GetColumnId();
                Value? v = row.GetValue(idx);
                Column refCol = refColumns![i].GetColumn();
                check.SetValue(refCol.GetColumnId(), refCol.Convert(session.CastDataProvider, v!));
            }

            Index refIndex = refConstraint!.GetIndex()!;
            if (!ExistsRow(session, refIndex, check, excluding: null))
            {
                throw DbException.Get(
                    Api.ErrorCode.ReferentialIntegrityViolatedParentMissing1,
                    GetShortDescription(null, null));
            }
        }
    }

    /// <inheritdoc/>
    public override Index? GetIndex() => index;

    /// <inheritdoc/>
    public override ConstraintUnique? GetReferencedConstraint() => refConstraint;
}
