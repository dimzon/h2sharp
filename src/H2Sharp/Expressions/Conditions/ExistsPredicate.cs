/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.condition.ExistsPredicate.
 * Copyright 2026 H2Sharp contributors.
 */
using System.Text;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Conditions;

/// <summary>Exists predicate as in EXISTS(SELECT ...).</summary>
public class ExistsPredicate : PredicateWithSubquery
{
    public ExistsPredicate(Query query) : base(query) { }

    public override Value GetValue(SessionLocal session)
    {
        query.SetSession(session);
        return ValueBoolean.Get(query.Exists());
    }

    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => base.GetUnenclosedSql(builder.Append("EXISTS"), sqlFlags);
}
