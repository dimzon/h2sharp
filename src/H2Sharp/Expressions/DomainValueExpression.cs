/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.DomainValueExpression.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Text;
using H2Sharp.Api;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>The VALUE of a domain constraint.</summary>
public sealed class DomainValueExpression : Expression
{
    private DomainColumnResolver? columnResolver;

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session) => columnResolver!.GetValue(null!)!;

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => columnResolver!.GetValueType();

    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    {
        if (resolver is DomainColumnResolver domainResolver)
        {
            columnResolver = domainResolver;
        }
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        if (columnResolver is null)
        {
            throw DbException.Get(ErrorCode.ColumnNotFound1, "VALUE");
        }

        return this;
    }

    /// <inheritdoc/>
    public override bool IsValueSet() => columnResolver!.GetValue(null!) is not null;

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        if (columnResolver is not null)
        {
            string? name = columnResolver.GetColumnName();
            if (name is not null)
            {
                return Util.ParserUtil.QuoteIdentifier(builder, name, sqlFlags);
            }
        }

        return builder.Append("VALUE");
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor) => true;

    /// <inheritdoc/>
    public override int GetCost() => 1;
}
