/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.DropConstant.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;

namespace H2Sharp.Command.Ddl;

/// <summary>DROP CONSTANT.</summary>
public sealed class DropConstant : SchemaCommand
{
    private string constantName = string.Empty;
    private bool ifExists;

    /// <summary>Creates the statement.</summary>
    public DropConstant(SessionLocal session, Schema schema) : base(session, schema)
    {
    }

    /// <summary>Sets whether a missing constant is ignored.</summary>
    public void SetIfExists(bool value) => ifExists = value;

    /// <summary>Sets the constant name.</summary>
    public void SetConstantName(string name) => constantName = name;

    /// <inheritdoc/>
    public override long Update()
    {
        Constant? constant = GetSchema().FindConstant(constantName);
        if (constant is null)
        {
            if (!ifExists)
            {
                throw DbException.Get(ErrorCode.ConstantNotFound1, constantName);
            }
        }
        else
        {
            GetSession().GetDatabase().RemoveSchemaObject(GetSession(), constant);
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.DropConstant;
}
