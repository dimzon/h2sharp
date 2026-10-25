/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterDomainExpressions.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>ALTER DOMAIN SET/DROP DEFAULT and ON UPDATE.</summary>
public sealed class AlterDomainExpressions : AlterDomain
{
    private readonly int type;

    private Expression? expression;

    /// <summary>Creates the statement.</summary>
    public AlterDomainExpressions(SessionLocal session, Schema schema, int type) : base(session, schema)
        => this.type = type;

    /// <summary>Sets the expression, or null to drop it.</summary>
    public void SetExpression(Expression? expression) => this.expression = expression;

    /// <inheritdoc/>
    protected override long Update(Schema schema, Domain domain)
    {
        switch (type)
        {
            case CommandInterface.AlterDomainDefault:
                domain.SetDefaultExpression(GetSession(), expression);
                break;
            case CommandInterface.AlterDomainOnUpdate:
                domain.SetOnUpdateExpression(GetSession(), expression);
                break;
            default:
                throw DbException.GetInternalError("type=" + type);
        }

        if (expression is not null)
        {
            ForAllDependencies(GetSession(), domain, CopyColumn, CopyDomain, true);
        }

        GetSession().GetDatabase().UpdateMeta(GetSession(), domain);
        return 0;
    }

    private bool CopyColumn(Domain domain, Column targetColumn) => type switch
    {
        CommandInterface.AlterDomainDefault => CopyDefault(domain.GetDefaultExpression(), targetColumn),
        CommandInterface.AlterDomainOnUpdate => CopyOnUpdate(domain.GetOnUpdateExpression(), targetColumn),
        _ => false,
    };

    private bool CopyDomain(Domain domain, Domain targetDomain) => type switch
    {
        CommandInterface.AlterDomainDefault => CopyDefault(domain.GetDefaultExpression(), targetDomain),
        CommandInterface.AlterDomainOnUpdate => CopyOnUpdate(domain.GetOnUpdateExpression(), targetDomain),
        _ => false,
    };

    private bool CopyDefault(Expression? expression, Column target)
    {
        if (expression is not null && target.GetDefaultExpression() is null)
        {
            target.SetDefaultExpression(GetSession(), expression);
            return true;
        }

        return false;
    }

    private bool CopyDefault(Expression? expression, Domain target)
    {
        if (expression is not null && target.GetDefaultExpression() is null)
        {
            target.SetDefaultExpression(GetSession(), expression);
            return true;
        }

        return false;
    }

    private bool CopyOnUpdate(Expression? expression, Column target)
    {
        if (expression is not null && target.GetOnUpdateExpression() is null)
        {
            target.SetOnUpdateExpression(GetSession(), expression);
            return true;
        }

        return false;
    }

    private bool CopyOnUpdate(Expression? expression, Domain target)
    {
        if (expression is not null && target.GetOnUpdateExpression() is null)
        {
            target.SetOnUpdateExpression(GetSession(), expression);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => type;
}
