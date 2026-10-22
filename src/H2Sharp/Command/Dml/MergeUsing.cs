/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.MergeUsing.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * MERGE INTO target USING source ON condition WHEN ...
 *
 * The statement walks the source and, for every source row, asks whether the target has a row
 * the ON condition joins to. A matched row is updated or deleted, a missed one is inserted,
 * and the WHEN clauses are tried in the order they were written until one of them applies.
 *
 * The one thing in here that looks like bookkeeping and is not: the source row that found no
 * target is remembered, and when the walk reaches a different source row the remembered one is
 * merged against an empty target first. The reference does that because the walk is a chain of
 * filters — reading the next target row moves the source with it — so a missed source row would
 * otherwise be forgotten the moment the walk moved on.
 *
 * What is left out, and why: triggers (the port has none), rights (it has no users), row
 * locking (no row-level locks, so the condition is not rechecked under a lock), the LocalResult
 * pair batch of UPDATE (the port writes one row at a time, as its UPDATE already does), and
 * dependency collection (a table is not a DbObject here yet). Each one is named where it would
 * run.
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Util;

namespace H2Sharp.Command.Dml;

/// <summary>
/// The statement <c>MERGE INTO table USING ... ON ... WHEN ...</c>.
/// </summary>
/// <remarks>
/// It does not replace <c>MERGE INTO ... KEY ... VALUES</c>, which is the other form and is
/// another class in the reference.
/// </remarks>
public sealed class MergeUsing : DataChangeStatement
{
    /// <summary>
    /// The target table filter.
    /// </summary>
    private TableFilter targetTableFilter;

    /// <summary>
    /// The source table filter.
    /// </summary>
    private TableFilter sourceTableFilter = null!;

    /// <summary>
    /// The ON condition expression.
    /// </summary>
    private Expression onCondition = null!;

    private readonly List<When> when = [];

    /// <summary>
    /// The row ids of the target rows already processed.
    /// </summary>
    /// <remarks>
    /// Row identities are remembered to prevent a second update of the same row: an ON
    /// condition that matches one target row twice would otherwise update it twice, and the
    /// reference refuses that instead.
    /// </remarks>
    private readonly HashSet<long> targetRowidsRemembered = [];

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="targetTableFilter">the target table filter</param>
    public MergeUsing(SessionLocal session, TableFilter targetTableFilter)
        : base(session) => this.targetTableFilter = targetTableFilter;

    /// <inheritdoc/>
    public override long Run(
        ResultTarget? deltaChangeCollector,
        Tables.DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode)
    {
        long countUpdatedRows = 0;
        targetRowidsRemembered.Clear();

        // The reference checks rights here and fires the statement-level triggers. The port has
        // no users and no triggers; the calls that would make are named in the file header.
        SetCurrentRowNumber(0);
        sourceTableFilter.StartQuery(GetSession());
        sourceTableFilter.Reset();
        Table table = targetTableFilter.GetTable()!;
        _ = table.Lock(GetSession(), Table.WriteLock);
        SetCurrentRowNumber(0);
        long count = 0;
        Row? previousSource = null;
        Row? missedSource = null;
        bool hasRowId = table.GetRowIdColumn() is not null;
        while (sourceTableFilter.Next())
        {
            Row? source = sourceTableFilter.Get();
            if (missedSource is not null)
            {
                if (!ReferenceEquals(source, missedSource))
                {
                    Row? backupTarget = targetTableFilter.Get();
                    sourceTableFilter.Set(missedSource);
                    targetTableFilter.Set(table.GetNullRow());
                    countUpdatedRows += Merge(true, deltaChangeCollector, deltaChangeCollectionMode);
                    sourceTableFilter.Set(source);
                    targetTableFilter.Set(backupTarget);
                    count++;
                }

                missedSource = null;
            }

            SetCurrentRowNumber(count + 1);
            bool nullRow = targetTableFilter.IsNullRow();
            if (!nullRow)
            {
                Row? targetRow = LockAndRecheckCondition(targetTableFilter, onCondition);
                if (targetRow is not null)
                {
                    if (hasRowId)
                    {
                        long targetRowId = targetRow.GetKey();
                        if (!targetRowidsRemembered.Add(targetRowId))
                        {
                            throw DbException.Get(
                                ErrorCode.DuplicateKey1,
                                "Merge using ON column expression, duplicate _ROWID_ target record"
                                + " already processed:_ROWID_=" + targetRowId + ":in:"
                                + targetTableFilter.GetTable());
                        }
                    }
                }
                else
                {
                    if (!ReferenceEquals(previousSource, source))
                    {
                        missedSource = source;
                    }

                    continue;
                }
            }

            countUpdatedRows += Merge(nullRow, deltaChangeCollector, deltaChangeCollectionMode);
            count++;
            previousSource = source;
        }

        if (missedSource is not null)
        {
            sourceTableFilter.Set(missedSource);
            targetTableFilter.Set(table.GetNullRow());
            countUpdatedRows += Merge(true, deltaChangeCollector, deltaChangeCollectionMode);
        }

        targetRowidsRemembered.Clear();
        return countUpdatedRows;
    }

    /// <summary>
    /// Runs the first WHEN clause that applies to the current pair of rows.
    /// </summary>
    /// <param name="nullRow">whether the target is the empty row</param>
    /// <param name="deltaChangeCollector">where the changed rows go, or null</param>
    /// <param name="deltaChangeCollectionMode">what to collect, or null for none</param>
    /// <returns>1 when a clause ran, 0 when none did</returns>
    private int Merge(
        bool nullRow,
        ResultTarget? deltaChangeCollector,
        Tables.DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode)
    {
        foreach (When w in when)
        {
            if ((w is WhenNotMatched) == nullRow)
            {
                Expression? condition = w.AndCondition;
                if (condition is null || condition.GetBooleanValue(GetSession()))
                {
                    w.Merge(GetSession(), deltaChangeCollector, deltaChangeCollectionMode);
                    return 1;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Sets the source table filter.
    /// </summary>
    /// <param name="sourceTableFilter">the filter</param>
    public void SetSourceTableFilter(TableFilter sourceTableFilter)
        => this.sourceTableFilter = sourceTableFilter;

    /// <summary>
    /// Gets the source table filter.
    /// </summary>
    /// <returns>the filter</returns>
    public TableFilter GetSourceTableFilter() => sourceTableFilter;

    /// <summary>
    /// Sets the ON condition.
    /// </summary>
    /// <param name="condition">the condition</param>
    public void SetOnCondition(Expression condition) => onCondition = condition;

    /// <summary>
    /// Gets the ON condition.
    /// </summary>
    /// <returns>the condition</returns>
    public Expression GetOnCondition() => onCondition;

    /// <summary>
    /// Gets the WHEN clauses.
    /// </summary>
    /// <returns>the clauses, in the order they were written</returns>
    public List<When> GetWhen() => when;

    /// <summary>
    /// Adds a WHEN clause.
    /// </summary>
    /// <param name="w">the clause</param>
    public void AddWhen(When w) => when.Add(w);

    /// <summary>
    /// Sets the target table filter.
    /// </summary>
    /// <param name="targetTableFilter">the filter</param>
    public void SetTargetTableFilter(TableFilter targetTableFilter)
        => this.targetTableFilter = targetTableFilter;

    /// <summary>
    /// Gets the target table filter.
    /// </summary>
    /// <returns>the filter</returns>
    public TableFilter GetTargetTableFilter() => targetTableFilter;

    /// <inheritdoc/>
    public override Table? GetTable() => targetTableFilter.GetTable();

    /// <inheritdoc/>
    protected override void DoPrepare()
    {
        onCondition.AddFilterConditions(sourceTableFilter);
        onCondition.AddFilterConditions(targetTableFilter);

        onCondition.MapColumns(sourceTableFilter, 0, Expression.MapInitial);
        onCondition.MapColumns(targetTableFilter, 0, Expression.MapInitial);

        onCondition = onCondition.Optimize(GetSession());

        // Create conditions only for the target table
        onCondition.CreateIndexConditions(GetSession(), targetTableFilter);

        TableFilter[] filters = [sourceTableFilter, targetTableFilter];
        sourceTableFilter.AddJoin(targetTableFilter, true, onCondition);
        PlanItem item = sourceTableFilter.GetBestPlanItem(
            GetSession(), filters, 0, new H2Sharp.Command.Query.AllColumnsForPlan(filters), isSelectCommand: false);
        sourceTableFilter.SetPlanItem(item);
        sourceTableFilter.Prepare();

        bool hasFinalNotMatched = false, hasFinalMatched = false;
        for (int i = when.Count - 1; i >= 0; i--)
        {
            When w = when[i];
            if (!w.Prepare(GetSession()))
            {
                when.RemoveAt(i);
            }
            else if (w is WhenNotMatched)
            {
                if (hasFinalNotMatched)
                {
                    when.RemoveAt(i);
                }
                else if (w.AndCondition is null)
                {
                    hasFinalNotMatched = true;
                }
            }
            else
            {
                if (hasFinalMatched)
                {
                    when.RemoveAt(i);
                }
                else if (w.AndCondition is null)
                {
                    hasFinalMatched = true;
                }
            }
        }
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Merge;

    /// <inheritdoc/>
    public override string GetStatementName() => "MERGE";

    /// <inheritdoc/>
    public override long GetRowCount() => 0;

    /// <inheritdoc/>
    /// <remarks>
    /// Not ported, for the reason UPDATE gives: the reference collects the table objects, and a
    /// table is not a <see cref="DbObject"/> in the port yet, so there is nothing to collect.
    /// </remarks>
    public override void CollectDependencies(HashSet<DbObject> dependencies)
        => throw new TODOException("MergeUsing.collectDependencies: a table is not a DbObject yet");

    /// <inheritdoc/>
    /// <remarks>
    /// Not ported: the reference prints the filters of the plan, and the port's table filter has
    /// no plan SQL yet.
    /// </remarks>
    public override StringBuilder GetPlanSql(StringBuilder builder, int sqlFlags)
        => throw new TODOException("MergeUsing.getPlanSQL: TableFilter.getPlanSQL is not ported");

    /// <summary>
    /// Abstract WHEN clause of the MERGE statement.
    /// </summary>
    /// <remarks>
    /// The reference declares these as inner classes of the statement, so they read its target
    /// and source filters for free. C# nested classes do not capture the outer instance, so it
    /// arrives as a constructor argument — the same arrangement the port's other nested classes
    /// use.
    /// </remarks>
    public abstract class When
    {
        /// <summary>
        /// The statement this clause belongs to.
        /// </summary>
        protected readonly MergeUsing Owner;

        /// <summary>
        /// The AND condition of the clause, or null.
        /// </summary>
        internal Expression? AndCondition;

        /// <summary>
        /// Creates the clause.
        /// </summary>
        /// <param name="owner">the statement the clause belongs to</param>
        protected When(MergeUsing owner) => Owner = owner;

        /// <summary>
        /// Sets the AND condition.
        /// </summary>
        /// <param name="andCondition">the condition</param>
        public void SetAndCondition(Expression? andCondition) => AndCondition = andCondition;

        /// <summary>
        /// Merges the rows.
        /// </summary>
        /// <param name="session">the session</param>
        /// <param name="deltaChangeCollector">where the changed rows go, or null</param>
        /// <param name="deltaChangeCollectionMode">what to collect, or null for none</param>
        internal abstract void Merge(
            SessionLocal session,
            ResultTarget? deltaChangeCollector,
            Tables.DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode);

        /// <summary>
        /// Prepares the clause.
        /// </summary>
        /// <param name="session">the session</param>
        /// <returns>false when the clause may be removed</returns>
        internal virtual bool Prepare(SessionLocal session)
        {
            if (AndCondition is not null)
            {
                AndCondition.MapColumns(Owner.targetTableFilter, 0, Expression.MapInitial);
                AndCondition.MapColumns(Owner.sourceTableFilter, 0, Expression.MapInitial);
                AndCondition = AndCondition.Optimize(session);
                if (AndCondition.IsConstant())
                {
                    if (AndCondition.GetBooleanValue(session))
                    {
                        AndCondition = null;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Collects the objects this clause depends on.
        /// </summary>
        /// <param name="visitor">the visitor</param>
        internal virtual void CollectDependencies(ExpressionVisitor visitor)
        {
            if (AndCondition is not null)
            {
                _ = AndCondition.IsEverything(visitor);
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            StringBuilder builder = new();
            _ = builder.Append("WHEN ");
            if (this is WhenNotMatched)
            {
                _ = builder.Append("NOT ");
            }

            _ = builder.Append("MATCHED");
            if (AndCondition is not null)
            {
                _ = AndCondition.GetUnenclosedSql(builder.Append(" AND "), HasSql.DefaultSqlFlags);
            }

            return builder.Append(" THEN ").ToString();
        }
    }

    /// <summary>
    /// WHEN MATCHED THEN DELETE.
    /// </summary>
    public sealed class WhenMatchedThenDelete : When
    {
        /// <summary>
        /// Creates the clause.
        /// </summary>
        /// <param name="owner">the statement the clause belongs to</param>
        public WhenMatchedThenDelete(MergeUsing owner)
            : base(owner)
        {
        }

        internal override void Merge(
            SessionLocal session,
            ResultTarget? deltaChangeCollector,
            Tables.DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode)
        {
            Table table = Owner.targetTableFilter.GetTable()!;
            Row row = Owner.targetTableFilter.Get()!;
            if (deltaChangeCollectionMode == Tables.DataChangeDeltaTable.ResultOption.OLD)
            {
                deltaChangeCollector?.AddRow(row.GetValueList());
            }

            // The reference fires the row triggers around the delete; the port has none.
            table.RemoveRow(session, row.GetKey());
        }

        /// <inheritdoc/>
        public override string ToString() => base.ToString() + "DELETE";
    }

    /// <summary>
    /// WHEN MATCHED THEN UPDATE.
    /// </summary>
    public sealed class WhenMatchedThenUpdate : When
    {
        private SetClauseList? setClauseList;

        /// <summary>
        /// Creates the clause.
        /// </summary>
        /// <param name="owner">the statement the clause belongs to</param>
        public WhenMatchedThenUpdate(MergeUsing owner)
            : base(owner)
        {
        }

        /// <summary>
        /// Sets the assignments.
        /// </summary>
        /// <param name="setClauseList">the assignments</param>
        public void SetSetClauseList(SetClauseList setClauseList) => this.setClauseList = setClauseList;

        internal override void Merge(
            SessionLocal session,
            ResultTarget? deltaChangeCollector,
            Tables.DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode)
        {
            Table table = Owner.targetTableFilter.GetTable()!;
            Row oldRow = Owner.targetTableFilter.Get()!;

            // The reference builds a LocalResult of old/new pairs and lets UPDATE write them in
            // one batch. The port's UPDATE writes one row at a time, so the pair is built and
            // written here, one row at a time, with the same result.
            Row newRow = (setClauseList
                ?? throw DbException.GetInternalError("MERGE ... UPDATE without assignments"))
                .PrepareUpdate(session, oldRow);
            if (deltaChangeCollectionMode == Tables.DataChangeDeltaTable.ResultOption.OLD)
            {
                deltaChangeCollector?.AddRow(oldRow.GetValueList());
            }
            else if (deltaChangeCollectionMode == Tables.DataChangeDeltaTable.ResultOption.NEW)
            {
                deltaChangeCollector?.AddRow([.. newRow.GetValueList()]);
            }

            table.UpdateRow(session, oldRow, newRow);
            if (deltaChangeCollectionMode == Tables.DataChangeDeltaTable.ResultOption.FINAL)
            {
                deltaChangeCollector?.AddRow(newRow.GetValueList());
            }
        }

        internal override bool Prepare(SessionLocal session)
        {
            bool result = base.Prepare(session);
            (setClauseList ?? throw DbException.GetInternalError("MERGE ... UPDATE without assignments"))
                .MapColumns(session, Owner.targetTableFilter, Owner.sourceTableFilter);
            return result;
        }

        internal override void CollectDependencies(ExpressionVisitor visitor)
        {
            base.CollectDependencies(visitor);
            _ = setClauseList!.IsEverything(visitor);
        }

        /// <inheritdoc/>
        public override string ToString()
            => setClauseList is null
                ? base.ToString() + "UPDATE"
                : setClauseList.GetSql(new StringBuilder(base.ToString() + "UPDATE"), HasSql.DefaultSqlFlags)
                    .ToString();
    }

    /// <summary>
    /// WHEN NOT MATCHED THEN INSERT.
    /// </summary>
    public sealed class WhenNotMatched : When
    {
        private Column[]? columns;

        private readonly bool? overridingSystem;

        private readonly Expression[] values;

        /// <summary>
        /// Creates the clause.
        /// </summary>
        /// <param name="owner">the statement the clause belongs to</param>
        /// <param name="columns">the columns, or null for all of them</param>
        /// <param name="overridingSystem">whether the values override what the system
        /// generates, or null when the statement said nothing</param>
        /// <param name="values">the values</param>
        public WhenNotMatched(
            MergeUsing owner, Column[]? columns, bool? overridingSystem, Expression[] values)
            : base(owner)
        {
            this.columns = columns;
            this.overridingSystem = overridingSystem;
            this.values = values;
        }

        internal override void Merge(
            SessionLocal session,
            ResultTarget? deltaChangeCollector,
            Tables.DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode)
        {
            Table table = Owner.targetTableFilter.GetTable()!;
            Row newRow = table.GetTemplateRow();
            Column[] targetColumns = columns!;
            for (int i = 0; i < targetColumns.Length; i++)
            {
                Column c = targetColumns[i];
                Expression e = values[i];
                if (!ReferenceEquals(e, ValueExpression.Default))
                {
                    try
                    {
                        newRow.SetValue(c.GetColumnId(), e.GetValue(session));
                    }
                    catch (H2SharpException ex)
                    {
                        throw DbException.AddSql(ex, "INSERT -- "
                            + Expression.WriteExpressions(
                                new StringBuilder(), values, HasSql.DefaultSqlFlags).ToString());
                    }
                }
            }

            table.ConvertInsertRow(session, newRow, overridingSystem);
            if (deltaChangeCollectionMode == Tables.DataChangeDeltaTable.ResultOption.NEW)
            {
                deltaChangeCollector?.AddRow([.. newRow.GetValueList()]);
            }

            _ = table.AddRow(session, newRow);
            Tables.DataChangeDeltaTable.CollectForInsert(
                session, table, deltaChangeCollector, deltaChangeCollectionMode, newRow);
        }

        internal override bool Prepare(SessionLocal session)
        {
            bool result = base.Prepare(session);
            Table targetTable = Owner.targetTableFilter.GetTable()!;

            // The reference uses the table's visible columns; the port has no hidden ones, so
            // its columns are the visible ones.
            columns ??= targetTable.GetColumns();
            if (values.Length != columns.Length)
            {
                throw DbException.Get(ErrorCode.ColumnCountDoesNotMatch);
            }

            for (int i = 0; i < values.Length; i++)
            {
                Expression e = values[i];
                e.MapColumns(Owner.targetTableFilter, 0, Expression.MapInitial);
                e.MapColumns(Owner.sourceTableFilter, 0, Expression.MapInitial);
                e = e.Optimize(session);
                if (e is Parameter parameter)
                {
                    parameter.SetColumn(columns[i]);
                }

                values[i] = e;
            }

            return result;
        }

        internal override void CollectDependencies(ExpressionVisitor visitor)
        {
            base.CollectDependencies(visitor);
            foreach (Expression e in values)
            {
                _ = e.IsEverything(visitor);
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            StringBuilder builder = new(base.ToString() + "INSERT (");
            _ = Column.WriteColumns(builder, columns!, HasSql.DefaultSqlFlags).Append(")\nVALUES (");
            return Expression.WriteExpressions(builder, values, HasSql.DefaultSqlFlags).Append(')').ToString();
        }
    }
}
