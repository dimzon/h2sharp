/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.CreateMaterializedView.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Collections.Generic;
using H2Sharp.Api;
using H2Sharp.Command.Query;
using H2Query = H2Sharp.Command.Query.Query;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>CREATE MATERIALIZED VIEW.</summary>
public sealed class CreateMaterializedView : SchemaCommand
{
    private readonly CreateTable createTable;
    private bool orReplace;
    private bool ifNotExists;
    private string viewName = string.Empty;
    private string? comment;
    private H2Query? select;
    private string selectSQL = string.Empty;
    private List<string>? columnNames;

    /// <summary>Creates the statement.</summary>
    public CreateMaterializedView(SessionLocal session, Schema schema) : base(session, schema)
        => createTable = new(session, schema);

    /// <summary>Sets the view name.</summary>
    public void SetViewName(string name)
    {
        viewName = name;
        createTable.SetTableName(name + "$1");
    }

    /// <summary>Sets the comment.</summary>
    public void SetComment(string? value) => comment = value;

    /// <summary>Sets the query SQL.</summary>
    public void SetSelectSql(string value) => selectSQL = value;

    /// <summary>Sets the query.</summary>
    public void SetSelect(H2Query value)
    {
        select = value;
        createTable.SetQuery(value);
    }

    /// <summary>Sets the column names written in parentheses.</summary>
    public void SetColumnNames(List<string>? names) => columnNames = names;

    /// <summary>Sets IF NOT EXISTS.</summary>
    public void SetIfNotExists(bool value)
    {
        ifNotExists = value;
        createTable.SetIfNotExists(value);
    }

    /// <summary>Sets OR REPLACE.</summary>
    public void SetOrReplace(bool value) => orReplace = value;

    /// <inheritdoc/>
    public override long Update()
    {
        Database db = GetSession().GetDatabase();
        Schema schema = GetSchema();
        Table? old = schema.FindTableOrView(GetSession(), viewName);
        MaterializedView? view = null;
        if (old is not null)
        {
            if (ifNotExists)
            {
                return 0;
            }

            if (!orReplace || old is not MaterializedView)
            {
                throw DbException.Get(ErrorCode.ViewAlreadyExists1, viewName);
            }

            view = (MaterializedView)old;
            db.RemoveSchemaObject(GetSession(), view.GetUnderlyingTable());
        }

        createTable.Update();
        Table underlying = schema.GetTableOrView(GetSession(), viewName + "$1");
        if (columnNames is not null)
        {
            Column[] columns = underlying.GetColumns();
            if (columns.Length != columnNames.Count)
            {
                throw DbException.Get(ErrorCode.ColumnCountDoesNotMatch);
            }

            for (int i = 0; i < columns.Length; i++)
            {
                columns[i].Rename(columnNames[i]);
            }
        }

        if (view is null)
        {
            view = new MaterializedView(schema, GetObjectId(), viewName, underlying, select!, selectSQL);
            if (comment is not null)
            {
                view.SetComment(comment);
            }

            db.AddSchemaObject(GetSession(), view);
        }
        else
        {
            view.Replace(underlying, select!, selectSQL);
            if (comment is not null)
            {
                view.SetComment(comment);
            }

            db.UpdateMeta(GetSession(), view);
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.CreateMaterializedView;
}
