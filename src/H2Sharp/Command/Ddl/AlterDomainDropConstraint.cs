/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterDomainDropConstraint.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;

namespace H2Sharp.Command.Ddl;

/// <summary>ALTER DOMAIN DROP CONSTRAINT.</summary>
public sealed class AlterDomainDropConstraint : AlterDomain
{
    private string constraintName = string.Empty;

    private readonly bool ifConstraintExists;

    /// <summary>Creates the statement.</summary>
    public AlterDomainDropConstraint(SessionLocal session, Schema schema, bool ifConstraintExists)
        : base(session, schema) => this.ifConstraintExists = ifConstraintExists;

    /// <summary>Sets the constraint name.</summary>
    public void SetConstraintName(string name) => constraintName = name;

    /// <inheritdoc/>
    protected override long Update(Schema schema, Domain domain)
    {
        Constraint? constraint = schema.FindConstraint(GetSession(), constraintName);
        if (constraint is null || constraint.GetConstraintType() != Constraint.Type.DOMAIN
            || !ReferenceEquals(((ConstraintDomain)constraint).GetDomain(), domain))
        {
            if (!ifConstraintExists)
            {
                throw DbException.Get(ErrorCode.ConstraintNotFound1, constraintName);
            }
        }
        else
        {
            GetSession().GetDatabase().RemoveSchemaObject(GetSession(), constraint);
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.AlterDomainDropConstraint;
}
