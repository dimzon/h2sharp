/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.constraint.ConstraintDomain.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;
using Index = H2Sharp.Indexes.Index;

namespace H2Sharp.Constraints;

/// <summary>A domain constraint.</summary>
public sealed class ConstraintDomain : Constraint
{
    private Domain? domain;

    private Expression? expr;

    private readonly DomainColumnResolver resolver;

    /// <summary>Creates a domain constraint.</summary>
    public ConstraintDomain(Schema schema, int id, string name, Domain domain)
        : base(schema, id, name, null!)
    {
        this.domain = domain;
        resolver = new DomainColumnResolver(domain.GetDataType());
    }

    /// <inheritdoc/>
    public override Type GetConstraintType() => Type.DOMAIN;

    /// <summary>Returns the domain of this constraint.</summary>
    public Domain GetDomain() => domain!;

    /// <summary>Sets the expression.</summary>
    public void SetExpression(SessionLocal session, Expression expression)
    {
        expression.MapColumns(resolver, 0, Expression.MapInitial);
        expression = expression.Optimize(session);
        lock (this)
        {
            resolver.SetValue(ValueNull.Instance);
            _ = expression.GetValue(session);
        }

        expr = expression;
    }

    /// <inheritdoc/>
    public override string GetCreateSqlWithoutIndexes() => GetCreateSql()!;

    /// <inheritdoc/>
    public override string GetCreateSql()
    {
        StringBuilder builder = new("ALTER DOMAIN ");
        _ = domain!.GetSql(builder, HasSql.DefaultSqlFlags).Append(" ADD CONSTRAINT ");
        _ = GetSql(builder, HasSql.DefaultSqlFlags);
        if (GetComment() is not null)
        {
            _ = builder.Append(" COMMENT ");
            _ = Util.StringUtils.QuoteStringSql(builder, GetComment());
        }

        _ = builder.Append(" CHECK ");
        _ = expr!.GetEnclosedSql(builder, HasSql.DefaultSqlFlags).Append(" NOCHECK");
        return builder.ToString();
    }

    /// <inheritdoc/>
    public override void RemoveChildrenAndResources(SessionLocal session)
    {
        domain!.RemoveConstraint(this);
        GetSchema().GetDatabase().RemoveMeta(session, GetId());
        domain = null;
        expr = null;
        Invalidate();
    }

    /// <inheritdoc/>
    public override void CheckRow(SessionLocal session, Table t, Row? oldRow, Row? newRow)
        => throw DbException.GetInternalError(ToString());

    /// <summary>Checks the specified value.</summary>
    public void Check(SessionLocal session, Value value)
    {
        Value v;
        lock (this)
        {
            resolver.SetValue(value);
            v = expr!.GetValue(session);
        }

        // Both TRUE and NULL are OK.
        if (v.IsFalse())
        {
            throw DbException.Get(ErrorCode.CheckConstraintViolated1, expr!.GetTraceSql());
        }
    }

    /// <inheritdoc/>
    public override bool UsesIndex(Index index) => false;

    /// <inheritdoc/>
    public override void SetIndexOwner(Index index) => throw DbException.GetInternalError(ToString());

    /// <inheritdoc/>
    public override HashSet<Column> GetReferencedColumns(Table table)
    {
        HashSet<Column> columns = [];
        _ = expr!.IsEverything(ExpressionVisitor.GetColumnsVisitor(columns, table));
        return columns;
    }

    /// <inheritdoc/>
    public override Expression? GetExpression() => expr;

    /// <inheritdoc/>
    public override bool IsBefore() => true;

    /// <inheritdoc/>
    public override void CheckExistingData(SessionLocal session)
    {
        if (session.GetDatabase().IsStarting())
        {
            return;
        }

        CheckDomain(session, domain!);
    }

    private void CheckDomain(SessionLocal session, Domain targetDomain)
    {
        foreach (Schema schema in session.GetDatabase().GetSchemas())
        {
            foreach (Domain dependent in schema.GetAllDomains())
            {
                if (ReferenceEquals(dependent.GetDomain(), targetDomain))
                {
                    CheckDomain(session, dependent);
                }
            }

            foreach (Table table in schema.GetAllTablesAndViews(session))
            {
                CheckTable(session, table);
            }
        }
    }

    private void CheckTable(SessionLocal session, Table table)
    {
        Column[] columns = table.GetColumns();
        for (int i = 0; i < columns.Length; i++)
        {
            if (!ReferenceEquals(columns[i].GetDomain(), domain))
            {
                continue;
            }

            Index? scan = table.GetScanIndex(session);
            if (scan is null)
            {
                continue;
            }

            H2Sharp.Indexes.Cursor cursor = scan.GetCursor(session, null, null, false)!;
            while (cursor.Next())
            {
                Row? row = cursor.Get();
                if (row is not null)
                {
                    Check(session, row.GetValue(i)!);
                }
            }
        }
    }

    /// <inheritdoc/>
    public override void Rebuild()
    {
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor) => expr!.IsEverything(visitor);
}
