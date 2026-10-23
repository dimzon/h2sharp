/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.ArrayConstructorByQuery. */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>Array value constructor by query.</summary>
public sealed class ArrayConstructorByQuery : Expression
{
    private readonly Query query;
    private TypeInfo componentType = null!;
    private TypeInfo type = null!;

    /// <summary>Creates an array constructor for the subquery.</summary>
    public ArrayConstructorByQuery(Query query) => this.query = query;

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
        => Util.StringUtils.Indent(builder.Append("ARRAY ("), query.GetPlanSql(sqlFlags), 4, false).Append(')');

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        query.SetSession(session);
        List<Value> values = [];
        Result.ResultInterface result = query.Run(0);
        try
        {
            while (result.Next()) values.Add(result.CurrentRow()[0]!);
        }
        finally { result.Close(); }
        return ValueArray.Get(componentType, [.. values], session);
    }

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => type;
    /// <inheritdoc/>
    public override void MapColumns(ColumnResolver resolver, int level, int state) => query.MapColumns(resolver, level + 1, true);
    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        query.Prepare();
        if (query.GetColumnCount() != 1) throw DbException.Get(Api.ErrorCode.SubqueryIsNotSingleColumn);
        componentType = query.GetExpressions()[0].GetTypeInfo();
        type = TypeInfo.GetTypeInfo(Value.Array, -1, -1, componentType);
        return this;
    }
    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value) => query.SetEvaluatable(tableFilter, value);
    /// <inheritdoc/>
    public override void UpdateAggregate(SessionLocal session, int stage) => query.UpdateAggregate(session, stage);
    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor) => query.IsEverything(visitor);
    /// <inheritdoc/>
    public override int GetCost() => query.GetCostAsExpression();
}
