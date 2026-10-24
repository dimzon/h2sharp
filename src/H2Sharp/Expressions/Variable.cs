/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.Variable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>
/// A user-defined variable, for example: @ID.
/// </summary>
public sealed class Variable : Expression
{
    private readonly string name;

    private Value lastValue;

    public Variable(SessionLocal session, string name)
    {
        this.name = name;
        lastValue = session.GetVariable(name);
    }

    public override int GetCost()
    {
        return 0;
    }

    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => ParserUtil.QuoteIdentifier(builder.Append('@'), name, sqlFlags);

    public override TypeInfo GetTypeInfo()
    {
        return lastValue.GetTypeInfo();
    }

    public override Value GetValue(SessionLocal session)
    {
        lastValue = session.GetVariable(name);
        return lastValue;
    }

    public override bool IsEverything(ExpressionVisitor visitor)
    {
        switch (visitor.GetVisitorType())
        {
            case ExpressionVisitor.Deterministic:
                return false;
            default:
                return true;
        }
    }

    /// <summary>
    /// Get the name of the variable.
    /// </summary>
    /// <returns>the name</returns>
    public string GetName()
    {
        return name;
    }
}
