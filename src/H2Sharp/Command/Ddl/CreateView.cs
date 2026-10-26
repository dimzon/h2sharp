/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.CreateView, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Collections.Generic;
using H2Sharp.Api;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Command.Ddl;

/// <summary>CREATE [OR REPLACE] [FORCE] VIEW.</summary>
public sealed class CreateView : SchemaCommand
{
    private H2Sharp.Command.Query.Query? query;

    private string viewName = string.Empty;

    private bool ifNotExists;

    private string? selectSQL;

    private string[]? columnNames;

    private string? comment;

    private bool orReplace;

    private bool force;

    /// <summary>Creates the statement.</summary>
    public CreateView(SessionLocal session, Schema schema)
        : base(session, schema)
    {
    }

    /// <summary>Sets the view name.</summary>
    public void SetViewName(string name) => viewName = name;

    /// <summary>Sets the parsed query.</summary>
    public void SetQuery(H2Sharp.Command.Query.Query select) => query = select;

    /// <summary>Sets whether IF NOT EXISTS was written.</summary>
    public void SetIfNotExists(bool value) => ifNotExists = value;

    /// <summary>Sets the query text for a FORCE view that could not be parsed.</summary>
    public void SetSelectSql(string sql) => selectSQL = sql;

    /// <summary>Sets the declared column names.</summary>
    public void SetColumnNames(string[]? cols) => columnNames = cols;

    /// <summary>Sets the comment.</summary>
    public void SetComment(string? value) => comment = value;

    /// <summary>Sets whether OR REPLACE was written.</summary>
    public void SetOrReplace(bool value) => orReplace = value;

    /// <summary>Sets whether FORCE was written.</summary>
    public void SetForce(bool value) => force = value;

    /// <inheritdoc/>
    public override long Update()
    {
        Database db = GetSession().GetDatabase();
        Table? old = GetSchema().FindTableOrView(GetSession(), viewName);
        TableView? view = null;
        if (old is not null)
        {
            if (ifNotExists)
            {
                return 0;
            }

            if (!orReplace || old.GetTableType() != Table.VIEW)
            {
                throw DbException.Get(ErrorCode.ViewAlreadyExists1, viewName);
            }

            view = (TableView)old;
        }

        int id = GetObjectId();
        string querySql;
        if (query is null)
        {
            querySql = selectSQL
                ?? throw DbException.GetInternalError("CREATE VIEW without query");
        }
        else
        {
            List<Parameter>? parameters = query.GetParameters();
            if (parameters is not null && parameters.Count > 0)
            {
                throw DbException.GetUnsupportedException("parameters in views");
            }

            querySql = query.GetPlanSql(HasSql.DefaultSqlFlags);
        }

        Column[]? unknownTemplates = null;
        if (columnNames is not null)
        {
            unknownTemplates = new Column[columnNames.Length];
            for (int i = 0; i < columnNames.Length; i++)
            {
                unknownTemplates[i] = new Column(columnNames[i], TypeInfo.TypeUnknown);
            }
        }

        if (view is null)
        {
            view = new TableView(GetSchema(), id, viewName, querySql, unknownTemplates, GetSession());
        }
        else
        {
            view.Replace(querySql, unknownTemplates, GetSession(), force);
            view.SetModified();
        }

        if (comment is not null)
        {
            view.SetComment(comment);
        }

        if (old is null)
        {
            db.AddSchemaObject(GetSession(), view);
        }
        else
        {
            db.UpdateMeta(GetSession(), view);
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.CreateView;
}
