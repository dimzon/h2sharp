/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.DropView, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>DROP VIEW.</summary>
public sealed class DropView : SchemaCommand
{
    private string viewName = string.Empty;

    private bool ifExists;

    private ConstraintActionType dropAction = ConstraintActionType.RESTRICT;

    /// <summary>Creates the statement.</summary>
    public DropView(SessionLocal session, Schema schema)
        : base(session, schema)
    {
    }

    /// <summary>Sets whether IF EXISTS was written.</summary>
    public void SetIfExists(bool value) => ifExists = value;

    /// <summary>Sets the drop action.</summary>
    public void SetDropAction(ConstraintActionType action) => dropAction = action;

    /// <summary>Sets the view name.</summary>
    public void SetViewName(string name) => viewName = name;

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
            if (view.GetTableType() != Table.VIEW)
            {
                throw DbException.Get(ErrorCode.ViewNotFound1, viewName);
            }

            if (dropAction == ConstraintActionType.RESTRICT)
            {
                foreach (TableView child in view.GetDependentViews())
                {
                    throw DbException.Get(ErrorCode.CannotDrop2, viewName, child.GetTableName());
                }
            }

            GetSession().GetDatabase().RemoveSchemaObject(GetSession(), view);
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.DropView;
}
