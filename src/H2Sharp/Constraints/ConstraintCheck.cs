/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.constraint.ConstraintCheck, H2 2.5.
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

/// <summary>A CHECK constraint.</summary>
public sealed class ConstraintCheck : Constraint
{
    private TableFilter? filter;

    private Expression? expr;

    public ConstraintCheck(Schema schema, int id, string name, Table table)
        : base(schema, id, name, table)
    {
    }

    public override Type GetConstraintType() => Type.CHECK;

    public void SetTableFilter(TableFilter filter) => this.filter = filter;

    public void SetExpression(Expression expr) => this.expr = expr;

    public override string GetCreateSqlForCopy(Table forTable, string quotedName)
    {
        StringBuilder builder = new("ALTER TABLE ");
        _ = forTable.GetSql(builder, HasSql.DefaultSqlFlags).Append(" ADD CONSTRAINT ");
        _ = builder.Append(quotedName);
        if (comment is not null)
        {
            _ = builder.Append(" COMMENT ");
            _ = StringUtils.QuoteStringSql(builder, comment);
        }

        _ = builder.Append(" CHECK");
        _ = expr!.GetEnclosedSql(builder, HasSql.DefaultSqlFlags).Append(" NOCHECK");
        return builder.ToString();
    }

    private string GetShortDescription()
    {
        StringBuilder builder = new StringBuilder().Append(GetName()).Append(": ");
        _ = expr!.GetTraceSql();
        return builder.ToString();
    }

    public override string GetCreateSqlWithoutIndexes() => GetCreateSql();

    public override string GetCreateSql()
        => GetCreateSqlForCopy(table, GetSql(HasSql.DefaultSqlFlags));

    public override void RemoveChildrenAndResources(SessionLocal session)
    {
        table.RemoveConstraint(this);
        GetSchema().GetDatabase().RemoveMeta(session, GetId());
        filter = null;
        expr = null;
        table = null!;
        Invalidate();
    }

    public override void CheckRow(SessionLocal session, Table t, Row? oldRow, Row? newRow)
    {
        if (newRow is null) return;
        bool violated;
        try
        {
            Value value;
            lock (this)
            {
                filter!.Set(newRow);
                value = expr!.GetValue(session);
            }
            // Both TRUE and NULL are accepted.
            violated = value.IsFalse();
        }
        catch (H2SharpException e)
        {
            throw DbException.Get(ErrorCode.CheckConstraintInvalid, e, GetShortDescription());
        }

        if (violated)
        {
            throw DbException.Get(ErrorCode.CheckConstraintViolated1, GetShortDescription());
        }
    }

    public override bool UsesIndex(Index index) => false;

    public override void SetIndexOwner(Index index)
        => throw DbException.GetInternalError(ToString());

    public override HashSet<Column> GetReferencedColumns(Table table)
    {
        HashSet<Column> columns = [];
        _ = expr!.IsEverything(ExpressionVisitor.GetColumnsVisitor(columns, table));
        return columns;
    }

    public override Expression? GetExpression() => expr;

    public override bool IsBefore() => true;

    public override void CheckExistingData(SessionLocal session)
    {
        if (session.GetDatabase().IsStarting()) return;
        StringBuilder builder = new("SELECT NULL FROM ");
        _ = filter!.GetTable()!.GetSql(builder, HasSql.DefaultSqlFlags).Append(" WHERE NOT ");
        _ = expr!.GetSql(builder, HasSql.DefaultSqlFlags, Expression.AutoParentheses);
        ResultInterface result = ((H2Sharp.Command.CommandContainer)session.PrepareCommand(builder.ToString())).Query(1);
        try
        {
            if (result.Next())
            {
                throw DbException.Get(ErrorCode.CheckConstraintViolated1, GetName());
            }
        }
        finally { result.Close(); }
    }

    public override void Rebuild()
    {
    }

    public override bool IsEverything(ExpressionVisitor visitor) => expr!.IsEverything(visitor);
}
