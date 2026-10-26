/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterView, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>ALTER VIEW ... RECOMPILE.</summary>
public sealed class AlterView : DefineCommand
{
    private bool ifExists;

    private TableView? view;

    /// <summary>Creates the statement.</summary>
    public AlterView(SessionLocal session)
        : base(session)
    {
    }

    /// <summary>Sets whether IF EXISTS was written.</summary>
    public void SetIfExists(bool value) => ifExists = value;

    /// <summary>Sets the view.</summary>
    public void SetView(TableView? value) => view = value;

    /// <inheritdoc/>
    public override long Update()
    {
        if (view is null && ifExists)
        {
            return 0;
        }

        if (view is null)
        {
            throw Message.DbException.Get(Api.ErrorCode.ViewNotFound1, string.Empty);
        }

        H2SharpException? error = view.Recompile(GetSession(), force: false, clearIndexCache: true);
        if (error is not null)
        {
            throw error;
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.AlterView;
}
