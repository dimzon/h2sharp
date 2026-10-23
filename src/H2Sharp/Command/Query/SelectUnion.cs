/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.command.query.SelectUnion.
 * Copyright 2026 H2Sharp contributors. */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Command.Query;

/// <summary>A set operation over two queries.</summary>
public sealed class SelectUnion : Query
{
    public enum UnionType { UNION, UNION_ALL, EXCEPT, INTERSECT }
    private readonly UnionType unionType;
    private readonly Query left;
    private readonly Query right;
    // These are inherited Query fields in Java, retained locally by this Query port.
    private Expression[] expressions = [];
    private Expression[] expressionArray = [];

    public SelectUnion(SessionLocal session, UnionType unionType, Query left, Query right) : base(session, string.Empty)
    { this.unionType = unionType; this.left = left; this.right = right; }

    public override bool IsUnion() => true;
    public override bool IsConstantQuery() => base.IsConstantQuery() && left.IsConstantQuery() && right.IsConstantQuery();
    // Query.GetUnionType currently has an int interface in the port.
    public override int GetUnionType() => (int)unionType;
    public Query GetLeft() => left;
    public Query GetRight() => right;
    public override Expression[] GetExpressions() => expressions;
    public override int GetColumnCount() => left.GetColumnCount();

    private Value?[] Convert(Value?[] values, int columnCount)
    {
        Value?[] converted = columnCount == values.Length ? values : new Value[columnCount];
        for (int i = 0; i < columnCount; i++)
            converted[i] = values[i]!.ConvertToType(expressions[i].GetTypeInfo(), GetSession());
        return converted;
    }
    public LocalResult GetEmptyResult() => CreateLocalResult(left.GetColumnCount());
    private LocalResult CreateLocalResult(int columnCount) => new(GetSession(), expressionArray, columnCount, columnCount);

    public override ResultInterface Run(long limit)
    {
        // H2 queryWithoutCache's materialized path. Target insertion and lazy UNION ALL
        // remain outside the existing Query.Run interface.
        OffsetFetch offsetFetch = GetOffsetFetch(limit);
        int count = left.GetColumnCount();
        LocalResult result = CreateLocalResult(count);
        if (sort is not null) result.SetSortOrder(sort);
        if (distinct) { left.SetDistinctIfPossible(); right.SetDistinctIfPossible(); result.SetDistinct(); }
        switch (unionType)
        {
            case UnionType.UNION:
            case UnionType.EXCEPT:
                left.SetDistinctIfPossible(); right.SetDistinctIfPossible(); result.SetDistinct(); break;
            case UnionType.INTERSECT:
                left.SetDistinctIfPossible(); right.SetDistinctIfPossible(); break;
        }
        using ResultInterface l = left.Run(0);
        using ResultInterface r = right.Run(0);
        l.Reset(); r.Reset();
        switch (unionType)
        {
            case UnionType.UNION:
            case UnionType.UNION_ALL:
                while (l.Next()) result.AddRow(Convert(l.CurrentRow(), count));
                while (r.Next()) result.AddRow(Convert(r.CurrentRow(), count));
                break;
            case UnionType.EXCEPT:
                while (l.Next()) result.AddRow(Convert(l.CurrentRow(), count));
                while (r.Next()) result.RemoveDistinct(Convert(r.CurrentRow(), count));
                break;
            case UnionType.INTERSECT:
                LocalResult temp = CreateLocalResult(count);
                temp.SetDistinct();
                while (l.Next()) temp.AddRow(Convert(l.CurrentRow(), count));
                while (r.Next())
                {
                    Value?[] values = Convert(r.CurrentRow(), count);
                    if (temp.ContainsDistinct(values)) result.AddRow(values);
                }
                temp.Close();
                break;
            default: throw DbException.GetInternalError("type=" + unionType);
        }
        return FinishResult(result, offsetFetch.Offset, offsetFetch.Fetch, offsetFetch.FetchPercent, null)!;
    }

    public override void Init()
    {
        if (checkInit) throw DbException.GetInternalError();
        checkInit = true;
        left.Init(); right.Init();
        int count = left.GetColumnCount();
        if (count != right.GetColumnCount()) throw DbException.Get(Api.ErrorCode.ColumnCountDoesNotMatch);
        expressions = left.GetExpressions()[..count];
    }
    public override void PrepareExpressions()
    {
        left.PrepareExpressions(); right.PrepareExpressions();
        int count = left.GetColumnCount();
        Expression[] le = left.GetExpressions(), re = right.GetExpressions();
        expressions = new Expression[count];
        for (int i = 0; i < count; i++)
        {
            Column column = new(le[i].GetAlias(GetSession(), i), TypeInfo.GetHigherType(le[i].GetTypeInfo(), re[i].GetTypeInfo()));
            expressions[i] = new ExpressionColumn(GetSession().GetDatabase(), column);
        }
        if (orderList is not null && InitOrder(null, true, null, [.. expressions]))
        { PrepareOrder(orderList, count); CleanupOrder([.. expressions]); }
        expressionArray = expressions;
    }
    public override void PreparePlan(SessionLocal session, TableFilter? targetTableFilter)
    { left.PreparePlan(session, targetTableFilter); right.PreparePlan(session, targetTableFilter); isPrepared = true; }
    public override long GetCost() => left.GetCost() + right.GetCost();
    public override HashSet<Table> GetTables() { HashSet<Table> set = left.GetTables(); set.UnionWith(right.GetTables()); return set; }
    public override long GetRowCount(SessionLocal session, List<TableFilter> filters, long limit)
        => left.GetRowCount(session, filters, limit) + right.GetRowCount(session, filters, limit);
    public override void MapColumns(ColumnResolver resolver, int level, bool outer)
    { left.MapColumns(resolver, level, outer); right.MapColumns(resolver, level, outer); }
    public override void SetEvaluatable(TableFilter filter, bool value) { left.SetEvaluatable(filter, value); right.SetEvaluatable(filter, value); }
    public override void UpdateAggregate(SessionLocal session, int stage) { left.UpdateAggregate(session, stage); right.UpdateAggregate(session, stage); }
    public override bool IsEverything(ExpressionVisitor visitor) => left.IsEverything(visitor) && right.IsEverything(visitor);
    public override StringBuilder GetPlanSql(StringBuilder builder, int sqlFlags)
    {
        left.GetPlanSql(builder.Append('('), sqlFlags).Append(')');
        builder.Append(unionType switch
        {
            UnionType.UNION_ALL => "\nUNION ALL\n",
            UnionType.UNION => "\nUNION\n",
            UnionType.EXCEPT => "\nEXCEPT\n",
            UnionType.INTERSECT => "\nINTERSECT\n",
            _ => throw DbException.GetInternalError()
        });
        right.GetPlanSql(builder.Append('('), sqlFlags).Append(')');
        AppendEndOfQueryToSql(builder, sqlFlags, expressions);
        return builder;
    }
    public override int GetCommandType() => CommandInterface.Select;
    public override bool IsTransactional() => true;
    public override bool IsQuery() => true;
    public override ResultInterface? QueryMeta() => GetEmptyResult();
}
