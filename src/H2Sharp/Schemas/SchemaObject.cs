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
 * The class exists for one reason: the name of an object is only unique *inside* a schema, so the
 * qualified name is what has to be printed and what a catalog query has to match. Everything
 * else is DbObject's.
 */
using System.Text;

namespace H2Sharp.Schemas;

/// <summary>
/// Any database object that is stored in a schema.
/// </summary>
public abstract class SchemaObject : Engine.DbObject
{
    private readonly Schema schema;

    /// <summary>
    /// Initialize some attributes of this object.
    /// </summary>
    /// <param name="newSchema">the schema</param>
    /// <param name="id">the object id</param>
    /// <param name="name">the name</param>
    /// <param name="traceModule">the trace module</param>
    protected SchemaObject(Schema newSchema, int id, string name, int traceModuleId)
        : base(newSchema.GetDatabase(), id, name, traceModuleId)
    {
        schema = newSchema;
    }

    /// <summary>
    /// Get the schema in which this object is defined.
    /// </summary>
    /// <returns>the schema</returns>
    public Schema GetSchema()
    {
        return schema;
    }

    /// <inheritdoc/>
    public override string GetSql(int sqlFlags)
    {
        return GetSql(new StringBuilder(), sqlFlags).ToString();
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        _ = Util.ParserUtil.QuoteIdentifier(builder, schema.GetName(), sqlFlags).Append('.');
        return base.GetSql(builder, sqlFlags);
    }
}
