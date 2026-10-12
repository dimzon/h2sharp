/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.Insert.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * INSERT INTO.
 *
 * The statement holds two ways of getting its rows and one rule about the count it answers:
 * rows given as VALUES arrive one list at a time through AddRow, rows given as a query are read
 * from that query, and either way GetRowCount is the number of rows that were *inserted* — not
 * the number that were offered. A statement that failed halfway has offered more rows than it
 * has written, and a caller that counted the offers would be told a lie about its own table.
 *
 * Not ported: the members below are the reference's shape, and each one refuses. The insert
 * itself needs the table's transaction, the identity handling and the duplicate-key path, none
 * of which exists here yet.
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Command.Dml;

/// <summary>
/// An INSERT statement.
/// </summary>
public sealed class Insert : CommandWithValues
{
    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    public Insert(SessionLocal session)
        : base(session)
    {
    }

    /// <summary>
    /// Sets the command that is this statement's parent.
    /// </summary>
    /// <param name="command">the parent command</param>
    public new void SetCommand(Command command)
        => throw new TODOException("Insert.setCommand: not ported");


    /// <summary>
    /// Sets the table the rows go into.
    /// </summary>
    /// <param name="table">the table</param>
    public void SetTable(Table table)
        => throw new TODOException("Insert.setTable: not ported");

    /// <summary>
    /// Sets the columns the values are for.
    /// </summary>
    /// <param name="columns">the columns</param>
    public void SetColumns(Column[] columns)
        => throw new TODOException("Insert.setColumns: not ported");

    /// <summary>
    /// Sets whether rows that would duplicate a key are skipped rather than refused.
    /// </summary>
    /// <param name="ignore">true to ignore duplicates</param>
    public void SetIgnore(bool ignore)
        => throw new TODOException("Insert.setIgnore: not ported");

    /// <summary>
    /// Sets the query the rows are read from, for INSERT INTO ... SELECT.
    /// </summary>
    /// <param name="query">the query</param>
    public void SetQuery(Query.Query query)
        => throw new TODOException("Insert.setQuery: Query is not ported");

    /// <summary>
    /// Sets whether the values override what the system would generate.
    /// </summary>
    /// <param name="overridingSystem">true when the statement says OVERRIDING SYSTEM VALUE</param>
    public void SetOverridingSystem(bool overridingSystem)
        => throw new TODOException("Insert.setOverridingSystem: not ported");

    /// <summary>
    /// Sets what a column becomes when the row duplicates a key, for ON DUPLICATE KEY UPDATE.
    /// </summary>
    /// <param name="column">the column</param>
    /// <param name="expression">what to set it to</param>
    public void AddAssignmentForDuplicate(Column column, Expression expression)
        => throw new TODOException("Insert.addAssignmentForDuplicate: not ported");

    /// <inheritdoc/>
    public override long Run(ResultTarget? deltaChangeCollector, Tables.DataChangeDeltaTable.ResultOption? deltaChangeCollectionMode)
        => throw new TODOException("Insert.update: not ported");

    /// <summary>
    /// Adds one row's values to the statement.
    /// </summary>
    /// <param name="values">the values</param>
    public void AddRow(params Value?[] values)
        => throw new TODOException("Insert.addRow: not ported");

    /// <inheritdoc/>
    public override long GetRowCount()
        => throw new TODOException("Insert.getRowCount: not ported");

    /// <inheritdoc/>
    public void LimitsWereApplied()
        => throw new TODOException("Insert.limitsWereApplied: not ported");

    /// <inheritdoc/>
    public override StringBuilder GetPlanSql(StringBuilder builder, int sqlFlags)
        => throw new TODOException("Insert.getPlanSQL: not ported");

    /// <summary>
    /// Gets the kind of this statement.
    /// </summary>
    /// <returns>the kind</returns>
    public override int GetCommandType() => CommandInterface.Insert;

    /// <inheritdoc/>
    public override string GetStatementName() => "INSERT";

    /// <summary>
    /// Sets whether the rows come from a select rather than from values.
    /// </summary>
    /// <param name="value">true for INSERT INTO ... SELECT</param>
    public void SetInsertFromSelect(bool value)
        => throw new TODOException("Insert.setInsertFromSelect: not ported");

    /// <summary>
    /// Gets what a column becomes when the row duplicates a key.
    /// </summary>
    /// <param name="columnIndex">the column index</param>
    /// <returns>the value, or null when there is none</returns>
    public Value? GetOnDuplicateKeyValue(int columnIndex)
        => throw new TODOException("Insert.getOnDuplicateKeyValue: not ported");

    /// <inheritdoc/>
    public override void CollectDependencies(HashSet<DbObject> dependencies)
        => throw new TODOException("Insert.collectDependencies: not ported");






    /// <inheritdoc/>
    public override bool IsRetryable() => true;
    /// <inheritdoc/>
    /// <remarks>
    /// The table is bound here and not at run time. A statement that looked its table up when
    /// it ran would write to a different table than the one that was named when it was read --
    /// and would fail after it had already changed something.
    /// </remarks>
    protected override void DoPrepare()
    {
        table = GetTableByName(GetParsedTableName());
    }
}
