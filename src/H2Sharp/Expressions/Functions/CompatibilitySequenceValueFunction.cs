/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.CompatibilitySequenceValueFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;

using H2Sharp.Command;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Schemas;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>
/// NEXTVAL() and CURRVAL() compatibility functions.
/// </summary>
/// <remarks>
/// The two forms are the PostgreSQL spellings of the same thing the sequence statement spells
/// as NEXT VALUE FOR and CURRENT VALUE FOR: NEXTVAL takes the next value of the sequence,
/// CURRVAL the value the session last took from it. The name of the sequence is the first
/// argument, and the schema may be given as a second argument or written into the first one as
/// a qualified name.
/// </remarks>
public sealed class CompatibilitySequenceValueFunction : Function1_2
{
    private readonly bool current;

    /// <summary>
    /// Creates a compatibility sequence value function.
    /// </summary>
    /// <param name="left">the first argument</param>
    /// <param name="right">the second argument, or null</param>
    /// <param name="current">whether the current value is read instead of the next one</param>
    public CompatibilitySequenceValueFunction(Expression left, Expression? right, bool current)
        : base(left, right)
    {
        this.current = current;
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value v1, Value? v2)
    {
        string schemaName;
        string sequenceName;
        if (v2 is null)
        {
            Parser p = new(session);
            string sql = v1.GetString()!;
            Expression expr = p.ParseExpression(sql);
            if (expr is ExpressionColumn seq)
            {
                string? alias = seq.GetOriginalTableAliasName();
                if (alias is null)
                {
                    schemaName = session.GetCurrentSchemaName();
                    sequenceName = sql;
                }
                else
                {
                    schemaName = alias;
                    sequenceName = seq.GetColumnName(session, -1);
                }
            }
            else
            {
                throw DbException.GetSyntaxError(sql, 1);
            }
        }
        else
        {
            schemaName = v1.GetString()!;
            sequenceName = v2.GetString()!;
        }

        H2Sharp.Engine.Database database = session.GetDatabase();
        Schema? schema = database.FindSchema(schemaName);
        if (schema is null)
        {
            schemaName = StringUtils.ToUpperEnglish(schemaName);
            schema = database.GetSchema(schemaName);
        }

        Sequence? sequence = schema.FindSequence(sequenceName);
        if (sequence is null)
        {
            sequenceName = StringUtils.ToUpperEnglish(sequenceName);
            sequence = schema.GetSequence(sequenceName);
        }

        Value value = current
            ? session.GetCurrentValueFor(sequence)
            : session.GetNextValueFor(sequence, null);
        return value.ConvertToType(type!);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        left = left.Optimize(session);
        if (right != null)
        {
            right = right.Optimize(session);
        }

        type = TypeInfo.TypeBigint;
        return this;
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        switch (visitor.GetVisitorType())
        {
            case ExpressionVisitor.Independent:
            case ExpressionVisitor.Deterministic:
            case ExpressionVisitor.QueryComparable:
                return false;
            case ExpressionVisitor.ReadOnly:
                if (!current)
                {
                    return false;
                }

                break;
        }

        return base.IsEverything(visitor);
    }

    /// <inheritdoc/>
    public override string GetName() => current ? "CURRVAL" : "NEXTVAL";
}
