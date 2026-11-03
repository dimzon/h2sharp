/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.command.ddl.DropDomain.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;

namespace H2Sharp.Command.Ddl;

/// <summary>A DROP DOMAIN statement.</summary>
public sealed class DropDomain : AlterDomain
{
    private ConstraintActionType dropAction;

    /// <summary>Creates the statement.</summary>
    public DropDomain(SessionLocal session, Schema schema) : base(session, schema)
        => dropAction = GetSession().GetDatabase().IsDropRestrict()
            ? ConstraintActionType.RESTRICT
            : ConstraintActionType.CASCADE;

    /// <summary>Sets the drop action.</summary>
    /// <param name="dropAction">the action</param>
    public void SetDropAction(ConstraintActionType dropAction) => this.dropAction = dropAction;

    /// <inheritdoc/>
    protected override long Update(Schema schema, Domain domain)
    {
        ForAllDependencies(GetSession(), domain, CopyColumn, CopyDomain, true);
        GetSession().GetDatabase().RemoveSchemaObject(GetSession(), domain);
        return 0;
    }

    private bool CopyColumn(Domain domain, Column targetColumn)
    {
        Table targetTable = targetColumn.GetTable()!;
        if (dropAction == ConstraintActionType.RESTRICT)
        {
            throw DbException.Get(ErrorCode.CannotDrop2, domainName, targetTable.GetCreateSql());
        }

        string columnName = targetColumn.GetName();
        if (domain.GetConstraints() is { Count: > 0 } constraints)
        {
            foreach (ConstraintDomain constraint in constraints)
            {
                Expression checkCondition = constraint.GetCheckConstraint(GetSession(), columnName);
                AlterTableAddConstraint check = new(GetSession(), targetTable.GetSchema()!,
                    CommandInterface.AlterTableAddConstraintCheck, false);
                check.SetTableName(targetTable.GetTableName());
                check.SetCheckExpression(checkCondition);
                _ = check.Update();
            }
        }

        CopyExpressions(GetSession(), domain, targetColumn);
        return true;
    }

    private bool CopyDomain(Domain domain, Domain targetDomain)
    {
        if (dropAction == ConstraintActionType.RESTRICT)
        {
            throw DbException.Get(ErrorCode.CannotDrop2, domainName, targetDomain.GetSql(Util.HasSql.TraceSqlFlags));
        }

        if (domain.GetConstraints() is { Count: > 0 } constraints)
        {
            foreach (ConstraintDomain constraint in constraints)
            {
                Expression checkCondition = constraint.GetCheckConstraint(GetSession(), null);
                AlterDomainAddConstraint check = new(GetSession(), targetDomain.GetSchema(), false);
                check.SetDomainName(targetDomain.GetName()!);
                check.SetCheckExpression(checkCondition);
                _ = check.Update();
            }
        }

        CopyExpressions(GetSession(), domain, targetDomain);
        return true;
    }

    private static void CopyExpressions(SessionLocal session, Domain domain, Column targetColumn)
    {
        targetColumn.SetDomain(domain.GetDomain());
        Expression? e = domain.GetDefaultExpression();
        if (e is not null && targetColumn.GetDefaultExpression() is null)
        {
            targetColumn.SetDefaultExpression(session, e);
        }

        e = domain.GetOnUpdateExpression();
        if (e is not null && targetColumn.GetOnUpdateExpression() is null)
        {
            targetColumn.SetOnUpdateExpression(session, e);
        }
    }

    private static void CopyExpressions(SessionLocal session, Domain domain, Domain targetDomain)
    {
        targetDomain.SetDomain(domain.GetDomain());
        Expression? e = domain.GetDefaultExpression();
        if (e is not null && targetDomain.GetDefaultExpression() is null)
        {
            targetDomain.SetDefaultExpression(session, e);
        }

        e = domain.GetOnUpdateExpression();
        if (e is not null && targetDomain.GetOnUpdateExpression() is null)
        {
            targetDomain.SetOnUpdateExpression(session, e);
        }
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.DropDomain;
}
