/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.DataChangeStatement.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * What INSERT, UPDATE and DELETE have in common.
 *
 * Each of the three rewrites rows of one table, and each of them runs inside a transaction on
 * that table — which is why the reference does not open one here but asks for the table's
 * transaction, the same way a read does. That is the rule written down in MVTable on this side
 * too, and it is the reason a statement that takes a transaction from its argument would write
 * on a transaction the caller cannot see.
 *
 * The base holds the session and the table, and answers the two questions every statement of
 * this kind answers the same way: is it transactional (yes, always) and what is its metadata
 * (the table's columns, no rows). Everything else is left to the three subclasses, and none of
 * them is ported yet.
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tables;

namespace H2Sharp.Command.Dml;

/// <summary>
/// A statement that changes rows: INSERT, UPDATE or DELETE.
/// </summary>
public abstract class DataChangeStatement : Command
{
    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    protected DataChangeStatement(SessionLocal session)
        : base(session, string.Empty)
    {
    }

    /// <summary>
    /// Reads the statement's own text and binds what it names: the table, the columns, the
    /// query. The reference calls this from the constructor of each subclass; here it is called
    /// by whoever prepares the statement, because the parsing that fills it is not ported.
    /// </summary>
    public virtual void Prepare()
        => throw new TODOException("DataChangeStatement.prepare: the commands are not ported");

    /// <summary>
    /// Gets the name of this statement, as it appears in its SQL.
    /// </summary>
    /// <returns>the name</returns>
    public abstract string GetStatementName();

    /// <summary>
    /// Gets the table this statement changes.
    /// </summary>
    /// <returns>the table</returns>
    public abstract Table? GetTable();

    /// <inheritdoc/>
    public override bool IsTransactional() => true;

    /// <inheritdoc/>
    public override ResultInterface QueryMeta()
        => throw new TODOException("DataChangeStatement.queryMeta: LocalResult is not ported");

    /// <inheritdoc/>
    public override bool IsQuery() => false;

    /// <inheritdoc/>
    public override bool IsCacheable() => false;

    /// <inheritdoc/>
    public override List<Expressions.ParameterInterface> GetParameters()
        => throw new TODOException("DataChangeStatement.getParameters: not ported");

    /// <summary>
    /// Runs the statement and answers the number of rows it changed.
    /// </summary>
    /// <remarks>
    /// Named Run rather than the reference's update, because C# forbids a member with the same
    /// name as its enclosing type and one of the three statements is a class called Update. The
    /// same reason renames the reference's query on the Query class.
    /// </remarks>
    /// <returns>the number of rows</returns>
    public long Run()
    {
        return Run(null, null);
    }

    /// <summary>
    /// The number of rows the last run changed.
    /// </summary>
    /// <returns>the row count</returns>
    public virtual long GetRowCount()
        => throw new TODOException("DataChangeStatement.getRowCount: not ported");

    /// <summary>
    /// Runs the statement, collecting the rows it changed if asked to.
    /// </summary>
    /// <param name="deltaChangeCollector">where the changed rows go, or null</param>
    /// <param name="deltaChangeCollectionMode">what to collect, or null for none</param>
    /// <returns>the number of rows</returns>
    public abstract long Run(ResultTarget? deltaChangeCollector, Tables.DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode);

    /// <summary>
    /// Collects the objects this statement depends on.
    /// </summary>
    /// <param name="dependencies">the set to add to</param>
    public abstract void CollectDependencies(HashSet<DbObject> dependencies);

    /// <summary>
    /// Appends the plan of this statement, as EXPLAIN prints it.
    /// </summary>
    /// <param name="builder">the builder</param>
    /// <param name="sqlFlags">the SQL flags</param>
    /// <returns>the builder</returns>
    public abstract StringBuilder GetPlanSql(StringBuilder builder, int sqlFlags);

    /// <summary>
    /// Locks the row the condition selects and rechecks the condition under the lock, so that a
    /// row which changed since the scan was read is not written over.
    /// </summary>
    /// <param name="targetTableFilter">the table being scanned</param>
    /// <param name="condition">the condition</param>
    /// <returns>the locked row, or null</returns>
    protected Row LockAndRecheckCondition(TableFilter targetTableFilter, Expression condition)
        => throw new TODOException("DataChangeStatement.lockAndRecheckCondition: TableFilter is not ported");
}
