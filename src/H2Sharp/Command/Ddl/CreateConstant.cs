/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.CreateConstant.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Values;

namespace H2Sharp.Command.Ddl;

/// <summary>CREATE CONSTANT.</summary>
public sealed class CreateConstant : SchemaCommand
{
    private string constantName = string.Empty;
    private Expression? expression;
    private bool ifNotExists;

    /// <summary>Creates the statement.</summary>
    public CreateConstant(SessionLocal session, Schema schema) : base(session, schema)
    {
    }

    /// <summary>Sets whether an existing constant is ignored.</summary>
    public void SetIfNotExists(bool value) => ifNotExists = value;

    /// <summary>Sets the constant name.</summary>
    public void SetConstantName(string name) => constantName = name;

    /// <summary>Sets the value expression.</summary>
    public void SetExpression(Expression value) => expression = value;

    /// <inheritdoc/>
    public override long Update()
    {
        Schema schema = GetSchema();
        if (schema.FindConstant(constantName) is not null)
        {
            if (ifNotExists)
            {
                return 0;
            }

            throw DbException.Get(ErrorCode.ConstantAlreadyExists1, constantName);
        }

        Constant constant = new(schema, GetObjectId(), constantName);
        Value value = expression!.Optimize(GetSession()).GetValue(GetSession());
        constant.SetValue(value);
        GetSession().GetDatabase().AddSchemaObject(GetSession(), constant);
        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.CreateConstant;
}
