/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.DropMaterializedView.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>DROP MATERIALIZED VIEW.</summary>
public sealed class DropMaterializedView : SchemaCommand
{
    private string viewName = string.Empty;
    private bool ifExists;

    /// <summary>Creates the statement.</summary>
    public DropMaterializedView(SessionLocal session, Schema schema) : base(session, schema)
    {
    }

    /// <summary>Sets IF EXISTS.</summary>
    public void SetIfExists(bool value) => ifExists = value;

    /// <summary>Sets the view name.</summary>
    public void SetViewName(string value) => viewName = value;

    /// <inheritdoc/>
    public override long Update()
    {
        Table? view = GetSchema().FindTableOrView(GetSession(), viewName);
        if (view is null)
        {
            if (!ifExists)
            {
                throw DbException.Get(ErrorCode.ViewNotFound1, viewName);
            }
        }
        else
        {
            if (view is not MaterializedView)
            {
                throw DbException.Get(ErrorCode.ViewNotFound1, viewName);
            }

            GetSession().GetDatabase().RemoveSchemaObject(GetSession(), view);
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.DropMaterializedView;
}
