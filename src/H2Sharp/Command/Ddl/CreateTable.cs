/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.CreateTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * CREATE TABLE.
 *
 * The statement is built field by field by the parser and runs once. Nothing is decided while it
 * is being read: a constraint may be read before the table exists, which is why the base class
 * holds constraints aside, and the temporary flags may be read in any order.
 *
 * The order inside Update is the part that cannot be checked by reading the fields, and it is
 * the thing a port gets wrong: the table is created with its columns, the key makes those
 * columns NOT NULL, the constraints are added, and only then is IF NOT EXISTS answered against
 * what the database already had. Checking first and creating afterwards leaves a table behind
 * when the statement decides the table was there after all.
 *
 * Not ported: the body. It needs the meta table and the object registry of the database.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// A CREATE TABLE statement.
/// </summary>
public sealed class CreateTable : CommandWithColumns
{
    private string tableName = string.Empty;

    private readonly List<Column> columns = [];

    private H2Sharp.Command.Query.Query? select;

    private bool temporary;

    private bool globalTemporary;

    private bool onCommitDrop;

    private bool onCommitTruncate;

    private bool persistIndexes = true;

    private bool persistData = true;

    private bool withNoData;

    private bool ifNotExists;

    private string? comment;

    private string? tableEngine;

    private List<string>? tableEngineParams;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema</param>
    public CreateTable(SessionLocal session, Schema schema)
        : base(session, schema)
    {
    }

    /// <summary>
    /// Sets the query whose rows the table is created from, for CREATE TABLE AS SELECT.
    /// </summary>
    /// <param name="query">the query</param>
    public void SetQuery(H2Sharp.Command.Query.Query query) => select = query;

    /// <summary>Sets whether the table is temporary.</summary>
    /// <param name="temporary">true for a temporary table</param>
    public void SetTemporary(bool temporary) => this.temporary = temporary;

    /// <summary>Sets the name of the table to create.</summary>
    /// <param name="tableName">the name</param>
    public void SetTableName(string tableName) => this.tableName = tableName;

    /// <inheritdoc/>
    public override void AddColumn(Column column)
    {
        ArgumentNullException.ThrowIfNull(column);
        columns.Add(column);
    }

    /// <summary>
    /// Gets the columns of the table.
    /// </summary>
    /// <returns>the columns, in the order they were written</returns>
    public List<Column> GetColumns() => columns;

    /// <summary>Sets whether the statement is IF NOT EXISTS.</summary>
    /// <param name="ifNotExists">true for IF NOT EXISTS</param>
    public void SetIfNotExists(bool ifNotExists) => this.ifNotExists = ifNotExists;

    /// <inheritdoc/>
    /// <remarks>
    /// The order here is the reference's and it is not free: create the table, make the key's
    /// columns NOT NULL, add the constraints, and only then answer IF NOT EXISTS against what
    /// the database had. Moving the check to the front would leave a table behind on the very
    /// path where the statement decided not to create one.
    /// </remarks>
    public override long Update()
    {
        if (temporary || globalTemporary || onCommitDrop || onCommitTruncate)
        {
            throw new TODOException("CreateTable.update: temporary tables are not ported");
        }

        if (select is not null || withNoData)
        {
            throw new TODOException("CreateTable.update: CREATE TABLE AS SELECT is not ported");
        }

        if (tableEngine is not null || tableEngineParams is not null)
        {
            throw new TODOException("CreateTable.update: table engines are not ported");
        }

        if (comment is not null)
        {
            throw new TODOException("CreateTable.update: table comments are not ported");
        }

        SessionLocal session = GetSession();
        Engine.Database db = session.GetDatabase();
        Schema schema = GetSchema();
        if (schema.FindTableOrView(session, tableName) is not null)
        {
            if (ifNotExists)
            {
                return 0;
            }

            throw Message.DbException.Get(Api.ErrorCode.TableOrViewAlreadyExists1, tableName);
        }

        ChangePrimaryKeysToNotNull(columns);
        MvStore.Db.MVTable table = new(
            db,
            schema,
            db.AllocateObjectId(),
            tableName,
            [.. columns],
            db.IsPersistent() && persistData);
        foreach (Column column in columns)
        {
            column.SetTable(table, column.GetColumnId());
        }

        schema.Add(table);
        CreateConstraints();
        return 0;
    }

    /// <summary>Sets whether the table's indexes are persisted.</summary>
    /// <param name="persistIndexes">true to persist them</param>
    public void SetPersistIndexes(bool persistIndexes) => this.persistIndexes = persistIndexes;

    /// <summary>Sets whether the table is a global temporary table.</summary>
    /// <param name="globalTemporary">true for a global temporary table</param>
    public void SetGlobalTemporary(bool globalTemporary) => this.globalTemporary = globalTemporary;

    /// <summary>Sets that the table is dropped at commit.</summary>
    public void SetOnCommitDrop() => onCommitDrop = true;

    /// <summary>Sets that the table is truncated at commit.</summary>
    public void SetOnCommitTruncate() => onCommitTruncate = true;

    /// <summary>Sets the comment on the table.</summary>
    /// <param name="comment">the comment</param>
    public void SetComment(string comment) => this.comment = comment;

    /// <summary>Sets whether the table's data is persisted.</summary>
    /// <param name="persistData">true to persist it</param>
    public void SetPersistData(bool persistData) => this.persistData = persistData;

    /// <summary>Sets whether the table is created without data.</summary>
    /// <param name="withNoData">true for WITH NO DATA</param>
    public void SetWithNoData(bool withNoData) => this.withNoData = withNoData;

    /// <summary>Sets the table engine.</summary>
    /// <param name="tableEngine">the engine</param>
    public void SetTableEngine(string tableEngine) => this.tableEngine = tableEngine;

    /// <summary>Sets the table engine parameters.</summary>
    /// <param name="tableEngineParams">the parameters</param>
    public void SetTableEngineParams(List<string> engineParams) => tableEngineParams = engineParams;

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.CreateTable;
}
