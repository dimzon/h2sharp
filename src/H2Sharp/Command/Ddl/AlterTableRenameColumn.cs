/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.command.ddl.AlterTableRenameColumn, H2 2.5. */
using H2Sharp.Engine;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>ALTER TABLE ALTER COLUMN RENAME.</summary>
public sealed class AlterTableRenameColumn : AlterTable
{
    private bool ifExists;
    private string oldName = string.Empty;
    private string newName = string.Empty;
    public AlterTableRenameColumn(SessionLocal session, Schema schema) : base(session, schema) { }
    public void SetIfExists(bool b) => ifExists = b;
    public void SetOldColumnName(string oldName) => this.oldName = oldName;
    public void SetNewColumnName(string newName) => this.newName = newName;
    public override long Update(Table table)
    {
        Column? column = table.GetColumn(oldName, ifExists);
        if (column is null) return 0;
        table.CheckSupportAlter();
        table.RenameColumn(column, newName);
        table.SetModified();
        Database db = GetSession().GetDatabase();
        db.UpdateMeta(GetSession(), table);
        // Referential actions resolve live columns and keep no cached SQL
        // (ConstraintReferential.Rebuild); the renamed Column is shared by these children.
        foreach (DbObject child in table.GetChildren())
            if (child.GetCreateSql() is not null) db.UpdateMeta(GetSession(), child);
        return 0;
    }
    public override int GetCommandType() => CommandInterface.AlterTableAlterColumnRename;
}
