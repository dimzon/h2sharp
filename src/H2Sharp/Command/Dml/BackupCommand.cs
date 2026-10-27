/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.BackupCommand.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Result;

namespace H2Sharp.Command.Dml;

/// <summary>BACKUP TO.</summary>
public sealed class BackupCommand : Prepared
{
    private Expression? fileNameExpr;

    /// <summary>Creates the statement.</summary>
    public BackupCommand(SessionLocal session) : base(session)
    {
    }

    /// <summary>Sets the backup file name expression.</summary>
    public void SetFileName(Expression fileName) => fileNameExpr = fileName;

    /// <inheritdoc/>
    public override long Update()
    {
        _ = fileNameExpr!.GetValue(GetSession()).GetString();
        // D-004: checkAdmin has no user object to check.
        if (!GetSession().GetDatabase().IsPersistent())
        {
            throw DbException.Get(ErrorCode.DatabaseIsNotPersistent);
        }

        throw new TODOException(
            "BackupCommand.backupTo: the persistent file store is not opened by the port");
    }

    /// <inheritdoc/>
    public override bool IsTransactional() => true;

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Backup;

    /// <inheritdoc/>
    public override ResultInterface? QueryMeta() => null;
}
