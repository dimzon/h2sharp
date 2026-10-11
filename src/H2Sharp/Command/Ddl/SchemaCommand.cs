/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.ddl.SchemaCommand.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A catalogue statement that names a schema.
 *
 * It is a base and nothing more: the statement holds the schema its object belongs to, and the
 * schema does the work. Every statement that creates or drops something names one, and passing
 * it down as a parameter rather than looking it up from the session is the reference's choice —
 * a statement prepared for one schema can then be run against another, which is what a
 * connection that switches schema needs.
 */
using H2Sharp.Engine;
using H2Sharp.Schemas;

namespace H2Sharp.Command.Ddl;

/// <summary>
/// A statement that creates or drops an object in a schema.
/// </summary>
public abstract class SchemaCommand : DefineCommand
{
    private readonly Schema schema;

    /// <summary>
    /// Creates the statement.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="schema">the schema</param>
    protected SchemaCommand(SessionLocal session, Schema schema)
        : base(session)
    {
        this.schema = schema;
    }

    /// <summary>
    /// Gets the schema.
    /// </summary>
    /// <returns>the schema</returns>
    protected Schema GetSchema() => schema;
}
