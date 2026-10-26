/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterTableRename, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>ALTER TABLE ... RENAME TO.</summary>
public sealed class AlterTableRename : AlterTable
{
    private string newTableName = string.Empty;

    /// <summary>Creates the statement.</summary>
    public AlterTableRename(SessionLocal session, Schema schema)
        : base(session, schema)
    {
    }

    /// <summary>Sets the new table name.</summary>
    public void SetNewTableName(string name) => newTableName = name;

    /// <inheritdoc/>
    public override long Update(Table table)
    {
        Database db = GetSession().GetDatabase();
        Table? existing = GetSchema().FindTableOrView(GetSession(), newTableName);
        if (existing is not null || newTableName.Equals(table.GetTableName(), System.StringComparison.Ordinal))
        {
            throw DbException.Get(ErrorCode.TableOrViewAlreadyExists1, newTableName);
        }

        if (table.IsTemporary())
        {
            throw DbException.GetUnsupportedException("temp table");
        }

        // MetaTable.checkRename in the reference.
        if (table.GetTableType() == Table.SYSTEM_TABLE)
        {
            throw DbException.GetUnsupportedException("META");
        }

        GetSchema().Rename(table, newTableName);
        db.UpdateMeta(GetSession(), table);
        foreach (DbObject child in table.GetChildren())
        {
            if (child.GetCreateSql() is not null)
            {
                db.UpdateMeta(GetSession(), child);
            }
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.AlterTableRename;
}
