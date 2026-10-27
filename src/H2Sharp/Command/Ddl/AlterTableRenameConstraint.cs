/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterTableRenameConstraint.
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

/// <summary>ALTER TABLE RENAME CONSTRAINT.</summary>
public sealed class AlterTableRenameConstraint : AlterTable
{
    private string constraintName = string.Empty;

    private string newConstraintName = string.Empty;

    /// <summary>Creates the statement.</summary>
    public AlterTableRenameConstraint(SessionLocal session, Schema schema) : base(session, schema)
    {
    }

    /// <summary>Sets the constraint name.</summary>
    public void SetConstraintName(string name) => constraintName = name;

    /// <summary>Sets the new constraint name.</summary>
    public void SetNewConstraintName(string name) => newConstraintName = name;

    /// <inheritdoc/>
    public override long Update(Table table)
    {
        Constraint? constraint = GetSchema().FindConstraint(GetSession(), constraintName);
        if (constraint is null || constraint.GetConstraintType() == Constraint.Type.DOMAIN
            || !ReferenceEquals(constraint.GetTable(), table))
        {
            throw DbException.Get(ErrorCode.ConstraintNotFound1, constraintName);
        }

        if (GetSchema().FindConstraint(GetSession(), newConstraintName) is not null
            || newConstraintName.Equals(constraintName, System.StringComparison.Ordinal))
        {
            throw DbException.Get(ErrorCode.ConstraintAlreadyExists1, newConstraintName);
        }

        // D-004: the SCHEMA_OWNER right on the referencing table has no user object to check.
        GetSession().GetDatabase().RenameSchemaObject(GetSession(), constraint, newConstraintName);
        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.AlterTableRenameConstraint;
}
