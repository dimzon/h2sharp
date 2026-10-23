/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.Subquery. Copyright 2026 H2Sharp contributors. */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions;

/// <summary>A query returning a single value.</summary>
public sealed class Subquery : Expression
{
    private readonly Query query;
    private Expression expression = null!;
    private Value nullValue = null!;
    private HashSet<ColumnResolver>? outerResolvers = [];
    public Subquery(Query query) => this.query = query;
    public override Value GetValue(SessionLocal session)
    {
        query.SetSession(session);
        using ResultInterface result = query.Run(2);
        if (!result.Next()) return nullValue;
        Value value = ReadRow(result);
        if (result.HasNext()) throw DbException.Get(Api.ErrorCode.ScalarSubqueryContainsMoreThanOneRow);
        return value;
    }
    public List<Value> GetAllRows(SessionLocal session)
    {
        List<Value> values = [];
        query.SetSession(session);
        using ResultInterface result = query.Run(int.MaxValue);
        while (result.Next()) values.Add(ReadRow(result));
        return values;
    }
    private Value ReadRow(ResultInterface result)
    {
        Value?[] values = result.CurrentRow();
        int visible = result.GetVisibleColumnCount();
        // ResultInterface annotates row elements nullable; engine rows carry ValueNull,
        // not CLR null. The CLR array type is identical, so retain Java's reuse rule.
        return visible == 1 ? values[0]! : ValueRow.Get(GetTypeInfo(),
            (Value[])(object)(visible == values.Length ? values : values[..visible]));
    }
    public override TypeInfo GetTypeInfo() => expression.GetTypeInfo();
    public override void MapColumns(ColumnResolver resolver, int level, int state)
    { outerResolvers?.Add(resolver); query.MapColumns(resolver, level + 1, true); }
    public override Expression Optimize(SessionLocal session)
    {
        query.Prepare();
        if (query.IsConstantQuery()) { SetType(); return ValueExpression.Get(GetValue(session)); }
        // DbSettings.optimizeSimpleSingleRowSubqueries's reference default is true.
        if (outerResolvers is not null)
        {
            Expression? e = query.GetIfSingleRow();
            if (e is not null && e.IsEverything(ExpressionVisitor.GetDecrementQueryLevelVisitor(outerResolvers, 0)))
            {
                e.IsEverything(ExpressionVisitor.GetDecrementQueryLevelVisitor(outerResolvers, 1));
                return e.Optimize(session);
            }
        }
        outerResolvers = null;
        SetType();
        return this;
    }
    private void SetType()
    {
        Expression[] expressions = query.GetExpressions();
        int count = query.GetColumnCount();
        if (count == 1) { expression = expressions[0]; nullValue = ValueNull.Instance; }
        else
        {
            Expression[] list = expressions[..count];
            Value[] nulls = new Value[count];
            TypeInfo[] types = new TypeInfo[count];
            for (int i = 0; i < count; i++) { nulls[i] = ValueNull.Instance; types[i] = list[i].GetTypeInfo(); }
            ExpressionList expressionList = new(list, false);
            expressionList.InitializeType();
            expression = expressionList;
            nullValue = ValueRow.Get(TypeInfo.GetTypeInfo(Value.Row, -1, -1, new ExtTypeInfoRow(types)), nulls);
        }
    }
    public override void SetEvaluatable(TableFilter filter, bool value) => query.SetEvaluatable(filter, value);
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags) => query.GetPlanSql(builder.Append('('), sqlFlags).Append(')');
    public override void UpdateAggregate(SessionLocal session, int stage) => query.UpdateAggregate(session, stage);
    public override bool IsEverything(ExpressionVisitor visitor) => query.IsEverything(visitor);
    public Query GetQuery() => query;
    public override int GetCost() => query.GetCostAsExpression();
    public override TypeInfo? GetTypeIfStaticallyKnown(SessionLocal session)
    {
        if (!query.IsConstantQuery()) return null;
        query.Prepare(); SetType(); return expression.GetTypeInfo();
    }
    public override bool IsConstant() => query.IsConstantQuery();
}
