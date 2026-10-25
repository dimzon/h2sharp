/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.DropDomain (the DROP DOMAIN branch of Parser).
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;

namespace H2Sharp.Command.Ddl;

/// <summary>A DROP DOMAIN statement.</summary>
public sealed class DropDomain : SchemaCommand
{
    private string domainName = string.Empty;

    private bool ifExists;

    private bool cascade;

    /// <summary>Creates the statement.</summary>
    public DropDomain(SessionLocal session, Schema schema) : base(session, schema)
    {
    }

    /// <summary>Sets the domain name.</summary>
    public void SetDomainName(string name) => domainName = name;

    /// <summary>Sets whether the statement is IF EXISTS.</summary>
    public void SetIfExists(bool b) => ifExists = b;

    /// <summary>Sets whether dependent objects are dropped.</summary>
    public void SetCascade(bool b) => cascade = b;

    /// <inheritdoc/>
    public override long Update()
    {
        Domain? domain = GetSchema().FindDomain(domainName);
        if (domain is null)
        {
            if (ifExists)
            {
                return 0;
            }

            throw DbException.Get(ErrorCode.DomainNotFound1, domainName);
        }

        if (!cascade && domain.GetConstraints() is { Count: > 0 })
        {
            throw DbException.Get(ErrorCode.CannotDrop2, domainName, "DOMAIN");
        }

        GetSession().GetDatabase().RemoveSchemaObject(GetSession(), domain);
        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.DropDomain;
}
