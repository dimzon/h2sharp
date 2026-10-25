/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.AlterDomain.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;

namespace H2Sharp.Command.Ddl;

/// <summary>The base class for ALTER DOMAIN commands.</summary>
public abstract class AlterDomain : SchemaCommand
{
    private string domainName = string.Empty;

    private bool ifDomainExists;

    /// <summary>Creates the statement.</summary>
    protected AlterDomain(SessionLocal session, Schema schema) : base(session, schema)
    {
    }

    /// <summary>Sets the domain name.</summary>
    public void SetDomainName(string name) => domainName = name;

    /// <summary>Sets whether the statement is IF EXISTS.</summary>
    public void SetIfDomainExists(bool b) => ifDomainExists = b;

    /// <inheritdoc/>
    public sealed override long Update()
    {
        Domain? domain = GetSchema().FindDomain(domainName);
        if (domain is null)
        {
            if (ifDomainExists)
            {
                return 0;
            }

            throw DbException.Get(ErrorCode.DomainNotFound1, domainName);
        }

        return Update(GetSchema(), domain);
    }

    /// <summary>Runs the statement for an existing domain.</summary>
    protected abstract long Update(Schema schema, Domain domain);

    /// <summary>Processes all columns and domains that use the specified domain.</summary>
    public static void ForAllDependencies(SessionLocal session, Domain domain,
        Func<Domain, Column, bool>? columnProcessor, Func<Domain, Domain, bool>? domainProcessor,
        bool recompileExpressions)
    {
        Engine.Database db = session.GetDatabase();
        foreach (Schema schema in db.GetSchemas())
        {
            foreach (Domain targetDomain in schema.GetAllDomains())
            {
                if (ReferenceEquals(targetDomain.GetDomain(), domain))
                {
                    if (domainProcessor is null || domainProcessor(domain, targetDomain))
                    {
                        if (recompileExpressions)
                        {
                            domain.PrepareExpressions(session);
                        }

                        db.UpdateMeta(session, targetDomain);
                    }
                }
            }

            foreach (Table table in schema.GetAllTablesAndViews(session))
            {
                if (ForTable(session, domain, columnProcessor, recompileExpressions, table))
                {
                    db.UpdateMeta(session, table);
                }
            }
        }
    }

    private static bool ForTable(SessionLocal session, Domain domain, Func<Domain, Column, bool>? columnProcessor,
        bool recompileExpressions, Table table)
    {
        bool modified = false;
        foreach (Column targetColumn in table.GetColumns())
        {
            if (ReferenceEquals(targetColumn.GetDomain(), domain))
            {
                bool m = columnProcessor is null || columnProcessor(domain, targetColumn);
                if (m)
                {
                    if (recompileExpressions)
                    {
                        targetColumn.PrepareExpressions(session);
                    }

                    modified = true;
                }
            }
        }

        return modified;
    }
}
