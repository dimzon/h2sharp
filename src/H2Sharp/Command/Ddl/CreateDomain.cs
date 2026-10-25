/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.CreateDomain.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Collections.Generic;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Values;

namespace H2Sharp.Command.Ddl;

/// <summary>A CREATE DOMAIN statement.</summary>
public sealed class CreateDomain : SchemaCommand
{
    private string typeName = string.Empty;

    private bool ifNotExists;

    private TypeInfo? dataType;

    private Domain? parentDomain;

    private Expression? defaultExpression;

    private Expression? onUpdateExpression;

    private string? comment;

    private List<AlterDomainAddConstraint>? constraintCommands;

    /// <summary>Creates the statement.</summary>
    public CreateDomain(SessionLocal session, Schema schema) : base(session, schema)
    {
    }

    /// <summary>Sets the domain name.</summary>
    public void SetTypeName(string name) => typeName = name;

    /// <summary>Sets whether the statement is IF NOT EXISTS.</summary>
    public void SetIfNotExists(bool b) => ifNotExists = b;

    /// <summary>Sets the data type.</summary>
    public void SetDataType(TypeInfo type) => dataType = type;

    /// <summary>Sets the parent domain.</summary>
    public void SetParentDomain(Domain domain) => parentDomain = domain;

    /// <summary>Sets the default expression.</summary>
    public void SetDefaultExpression(Expression expression) => defaultExpression = expression;

    /// <summary>Sets the on-update expression.</summary>
    public void SetOnUpdateExpression(Expression expression) => onUpdateExpression = expression;

    /// <summary>Sets the comment.</summary>
    public void SetComment(string comment) => this.comment = comment;

    /// <summary>Adds a constraint command.</summary>
    public void AddConstraintCommand(AlterDomainAddConstraint command)
    {
        constraintCommands ??= [];
        constraintCommands.Add(command);
    }

    /// <inheritdoc/>
    public override long Update()
    {
        Schema schema = GetSchema();
        if (schema.FindDomain(typeName) is not null)
        {
            if (ifNotExists)
            {
                return 0;
            }

            throw DbException.Get(ErrorCode.DomainAlreadyExists1, typeName);
        }

        int id = GetObjectId();
        Domain domain = new(schema, id, typeName);
        domain.SetDataType(dataType ?? parentDomain!.GetDataType());
        domain.SetDomain(parentDomain);
        domain.SetDefaultExpression(GetSession(), defaultExpression);
        domain.SetOnUpdateExpression(GetSession(), onUpdateExpression);
        domain.SetComment(comment);
        GetSession().GetDatabase().AddSchemaObject(GetSession(), domain);
        if (constraintCommands is not null)
        {
            foreach (AlterDomainAddConstraint command in constraintCommands)
            {
                command.Update();
            }
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.CreateDomain;
}
