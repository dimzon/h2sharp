/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mode.Regclass.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Modes;

/// <summary>
/// A ::regclass expression.
/// </summary>
public sealed class Regclass : Operation1
{
    /// <summary>
    /// Creates a REGCLASS expression.
    /// </summary>
    /// <param name="arg">the argument</param>
    public Regclass(Expression arg) : base(arg)
    {
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        Value value = arg.GetValue(session);
        if (value is ValueNull)
        {
            return ValueNull.Instance;
        }

        int valueType = value.GetValueType();
        if (valueType >= Value.Tinyint && valueType <= Value.Integer)
        {
            return value.ConvertToIntType(null);
        }

        if (valueType == Value.Bigint)
        {
            return ValueInteger.Get((int)value.GetLong());
        }

        string name = value.GetString()!;
        // Database.getAllSchemas is exposed as GetSchemas by the existing database port.
        foreach (Schema schema in session.GetDatabase().GetSchemas())
        {
            Table? table = schema.FindTableOrView(session, name);
            if (table is not null)
            {
                return ValueInteger.Get(table.GetId());
            }

            Index? index = schema.FindIndex(session, name);
            if (index is not null && index.GetCreateSql() is not null)
            {
                return ValueInteger.Get(index.GetId());
            }
        }

        throw DbException.Get(ErrorCode.TableOrViewNotFound1, name);
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeInteger;

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        arg = arg.Optimize(session);
        if (arg.IsConstant())
        {
            return ValueExpression.Get(GetValue(session));
        }

        return this;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => arg.GetSql(builder, sqlFlags, AutoParentheses).Append("::REGCLASS");

    /// <inheritdoc/>
    public override int GetCost() => arg.GetCost() + 100;
}
