/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterDomainRename.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;

namespace H2Sharp.Command.Ddl;

/// <summary>ALTER DOMAIN RENAME.</summary>
public sealed class AlterDomainRename : AlterDomain
{
    private string newDomainName = string.Empty;

    /// <summary>Creates the statement.</summary>
    public AlterDomainRename(SessionLocal session, Schema schema) : base(session, schema)
    {
    }

    /// <summary>Sets the new domain name.</summary>
    public void SetNewDomainName(string name) => newDomainName = name;

    /// <inheritdoc/>
    protected override long Update(Schema schema, Domain domain)
    {
        Domain? d = schema.FindDomain(newDomainName);
        if (d is not null)
        {
            if (!ReferenceEquals(domain, d))
            {
                throw DbException.Get(ErrorCode.DomainAlreadyExists1, newDomainName);
            }

            if (newDomainName.Equals(domain.GetName(), System.StringComparison.Ordinal))
            {
                return 0;
            }
        }

        GetSession().GetDatabase().RenameSchemaObject(GetSession(), domain, newDomainName);
        ForAllDependencies(GetSession(), domain, null, null, false);
        return 0;
    }

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.AlterDomainRename;
}
