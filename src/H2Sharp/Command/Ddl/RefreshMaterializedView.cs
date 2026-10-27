/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.RefreshMaterializedView.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>REFRESH MATERIALIZED VIEW.</summary>
public sealed class RefreshMaterializedView : SchemaCommand
{
    private MaterializedView? view;

    /// <summary>Creates the statement.</summary>
    public RefreshMaterializedView(SessionLocal session, Schema schema) : base(session, schema)
    {
    }

    /// <summary>Sets the view to refresh.</summary>
    public void SetView(MaterializedView value) => view = value;

    /// <inheritdoc/>
    public override long Update()
    {
        MaterializedView target = view!;
        TruncateTable truncate = new(GetSession());
        truncate.SetTable(target.GetUnderlyingTable());
        truncate.Update();
        CreateTable createTable = new(GetSession(), GetSchema());
        createTable.SetQuery(target.GetSelect());
        createTable.InsertAsData(target.GetUnderlyingTable());
        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.RefreshMaterializedView;
}
