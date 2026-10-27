/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.schema.Constant.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Schemas;

/// <summary>A user-defined constant created by CREATE CONSTANT.</summary>
public sealed class Constant : SchemaObject
{
    private Value? value;

    private ValueExpression? expression;

    /// <summary>Creates a constant.</summary>
    public Constant(Schema schema, int id, string name) : base(schema, id, name, Trace.Schema)
    {
    }

    /// <inheritdoc/>
    public override string? GetCreateSql()
    {
        StringBuilder builder = new("CREATE CONSTANT ");
        _ = GetSql(builder, HasSql.DefaultSqlFlags).Append(" VALUE ");
        return value!.GetSql(builder, HasSql.DefaultSqlFlags).ToString();
    }

    /// <inheritdoc/>
    public override int GetTypeId() => DbObject.Constant;

    /// <inheritdoc/>
    public override void RemoveChildrenAndResources(SessionLocal session)
    {
        GetDatabase()!.RemoveMeta(session, GetId());
        Invalidate();
    }

    /// <summary>Sets the constant's value.</summary>
    public void SetValue(Value newValue)
    {
        value = newValue;
        expression = ValueExpression.Get(newValue);
    }

    /// <summary>Gets the constant's value expression.</summary>
    public ValueExpression GetValue() => expression!;
}
