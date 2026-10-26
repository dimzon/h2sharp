/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.AlterTableSet, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Dml;

/// <summary>ALTER TABLE ... SET REFERENTIAL_INTEGRITY.</summary>
public sealed class AlterTableSet : Ddl.SchemaCommand
{
    private bool ifTableExists;

    private string tableName = string.Empty;

    private readonly int type;

    private readonly bool value;

    private bool checkExisting;

    /// <summary>Creates the statement.</summary>
    public AlterTableSet(SessionLocal session, Schema schema, int type, bool value)
        : base(session, schema)
    {
        this.type = type;
        this.value = value;
    }

    /// <summary>Sets whether IF EXISTS was written.</summary>
    public void SetIfTableExists(bool value) => ifTableExists = value;

    /// <summary>Sets the table name.</summary>
    public void SetTableName(string name) => tableName = name;

    /// <summary>Sets whether existing rows are checked.</summary>
    public void SetCheckExisting(bool value) => checkExisting = value;

    /// <inheritdoc/>
    public override bool IsTransactional() => true;

    /// <inheritdoc/>
    public override long Update()
    {
        Table? table = GetSchema().FindTableOrView(GetSession(), tableName);
        if (table is null)
        {
            if (ifTableExists) return 0;
            throw DbException.Get(ErrorCode.TableOrViewNotFound1, tableName);
        }

        switch (type)
        {
            case CommandInterface.AlterTableSetReferentialIntegrity:
                table.SetCheckForeignKeyConstraints(GetSession(), value, value && checkExisting);
                break;
            default:
                throw DbException.GetInternalError("type=" + type);
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => type;
}
