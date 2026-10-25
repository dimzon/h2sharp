/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterDomainAddConstraint.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Schemas;

namespace H2Sharp.Command.Ddl;

/// <summary>ALTER DOMAIN ADD CONSTRAINT.</summary>
public sealed class AlterDomainAddConstraint : AlterDomain
{
    private string? constraintName;

    private Expression? checkExpression;

    private string? comment;

    private bool checkExisting;

    private readonly bool ifNotExists;

    /// <summary>Creates the statement.</summary>
    public AlterDomainAddConstraint(SessionLocal session, Schema schema, bool ifNotExists)
        : base(session, schema) => this.ifNotExists = ifNotExists;

    /// <summary>Sets the constraint name.</summary>
    public void SetConstraintName(string? constraintName) => this.constraintName = constraintName;

    /// <summary>Gets the constraint name.</summary>
    public string? GetConstraintName() => constraintName;

    /// <summary>Sets the check expression.</summary>
    public void SetCheckExpression(Expression expression) => checkExpression = expression;

    /// <summary>Sets the comment.</summary>
    public void SetComment(string? comment) => this.comment = comment;

    /// <summary>Sets whether existing rows are checked.</summary>
    public void SetCheckExisting(bool b) => checkExisting = b;

    /// <inheritdoc/>
    protected override long Update(Schema schema, Domain domain)
    {
        try
        {
            if (constraintName is not null && schema.FindConstraint(GetSession(), constraintName) is not null)
            {
                if (ifNotExists)
                {
                    return 0;
                }

                throw DbException.Get(ErrorCode.ConstraintAlreadyExists1, constraintName);
            }

            GetSession().GetDatabase().LockMeta(GetSession());
            int id = GetObjectId();
            string name = constraintName ?? schema.GetUniqueDomainConstraintName(GetSession(), domain);
            ConstraintDomain constraint = new(schema, id, name, domain);
            constraint.SetExpression(GetSession(), checkExpression!);
            if (checkExisting)
            {
                constraint.CheckExistingData(GetSession());
            }

            constraint.SetComment(comment);
            GetSession().GetDatabase().AddSchemaObject(GetSession(), constraint);
            domain.AddConstraint(constraint);
            return 0;
        }
        finally
        {
            schema.FreeUniqueName(constraintName);
        }
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.AlterDomainAddConstraint;
}
