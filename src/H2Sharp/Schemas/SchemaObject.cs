/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.schema.SchemaObject.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Any database object that is stored in a schema.
 *
 * It is a thin class and it exists for one reason: an object has to know both which schema it
 * belongs to and which database that schema is in, and its parent already knows the database
 * because the id is allocated by it. Passing the database up from the schema is what lets the two
 * agree — a second way to learn which database an object is in would eventually disagree with
 * the first.
 *
 * It also prints its own name qualified by the schema. That is the only difference in printing,
 * and it is the reason the method is overridden rather than left to the parent: a name that
 * printed as "S.T" for an object created inside schema S would be a name that only works while
 * S is the current one.
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Message;

namespace H2Sharp.Schemas;

/// <summary>
/// An object that is stored in a schema.
/// </summary>
public abstract class SchemaObject : DbObject
{
    private readonly Schema schema;

    /// <summary>
    /// Creates the object.
    /// </summary>
    /// <param name="newSchema">the schema</param>
    /// <param name="id">the object id</param>
    /// <param name="name">the name</param>
    /// <param name="traceModuleId">the trace module id</param>
    protected SchemaObject(Schema newSchema, int id, string name, int traceModuleId)
        : base(newSchema.GetDatabase(), id, name, traceModuleId)
    {
        schema = newSchema;
    }

    /// <summary>
    /// Gets the schema this object is defined in.
    /// </summary>
    /// <returns>the schema</returns>
    public Schema GetSchema() => schema;

    /// <inheritdoc/>
    public override string GetSql(int sqlFlags) => GetSql(new StringBuilder(), sqlFlags).ToString();

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        _ = schema.GetSql(builder, sqlFlags).Append('.');
        return base.GetSql(builder, sqlFlags);
    }
}
