/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.analysis.WindowFunction.
 * Copyright 2026 H2Sharp contributors. */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Tables;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Analysis;

/// <summary>A window function.</summary>
public class WindowFunction : DataAnalysisOperation
{
    private readonly WindowFunctionType type;
    private readonly Expression[]? args;
    private bool fromLast;
    private bool ignoreNulls;
    public static int GetMinArgumentCount(WindowFunctionType type) => type switch
    {
        WindowFunctionType.Ntile or WindowFunctionType.Lead or WindowFunctionType.Lag
            or WindowFunctionType.FirstValue or WindowFunctionType.LastValue or WindowFunctionType.RatioToReport => 1,
        WindowFunctionType.NthValue => 2,
        _ => 0
    };
    public static int GetMaxArgumentCount(WindowFunctionType type) => type switch
    {
        WindowFunctionType.Ntile or WindowFunctionType.FirstValue or WindowFunctionType.LastValue or WindowFunctionType.RatioToReport => 1,
        WindowFunctionType.Lead or WindowFunctionType.Lag => 3,
        WindowFunctionType.NthValue => 2,
        _ => 0
    };
    private static Value GetNthValue(IEnumerable<Value?[]> iterator, int number, bool ignoreNulls)
    {
        Value value = ValueNull.Instance;
        int count = 0;
        foreach (Value?[] row in iterator)
        {
            Value current = row[0]!;
            if (!ignoreNulls || current != ValueNull.Instance)
            {
                if (count++ == number) { value = current; break; }
            }
        }
        return value;
    }
    public WindowFunction(WindowFunctionType type, Select select, Expression[]? args) : base(select)
    { this.type = type; this.args = args; }
    public WindowFunctionType GetFunctionType() => type;
    public void SetFromLast(bool value) => fromLast = value;
    public void SetIgnoreNulls(bool value) => ignoreNulls = value;
    public override bool IsAggregate() => false;
    protected override void UpdateAggregate(SessionLocal session, SelectGroups groupData, int groupRowId)
        => UpdateOrderedAggregate(session, groupData, groupRowId, over!.GetOrderBy());
    protected override void UpdateGroupAggregates(SessionLocal session, int stage)
    {
        base.UpdateGroupAggregates(session, stage);
        if (args is not null) foreach (Expression arg in args) arg.UpdateAggregate(session, stage);
    }
    protected override int GetNumExpressions() => args?.Length ?? 0;
    protected override void RememberExpressions(SessionLocal session, Value?[] array)
    { if (args is not null) for (int i = 0; i < args.Length; i++) array[i] = args[i].GetValue(session); }
    protected override object CreateAggregateData() => throw DbException.GetUnsupportedException("Window function");
    protected override Value GetAggregatedValue(SessionLocal session, object? aggregateData)
        => throw DbException.GetUnsupportedException("Window function");
    protected override void GetOrderedResultLoop(SessionLocal session, Dictionary<int, Value> result, List<Value?[]> ordered, int rowIdColumn)
    {
        switch (type)
        {
            case WindowFunctionType.RowNumber:
                for (int i = 0; i < ordered.Count;) result[ordered[i][rowIdColumn]!.GetInt()] = ValueBigint.Get(++i);
                break;
            case WindowFunctionType.Rank:
            case WindowFunctionType.DenseRank:
            case WindowFunctionType.PercentRank: GetRank(result, ordered, rowIdColumn); break;
            case WindowFunctionType.CumeDist: GetCumeDist(result, ordered, rowIdColumn); break;
            case WindowFunctionType.Ntile: GetNtile(result, ordered, rowIdColumn); break;
            case WindowFunctionType.Lead:
            case WindowFunctionType.Lag: GetLeadLag(result, ordered, rowIdColumn, session); break;
            case WindowFunctionType.FirstValue:
            case WindowFunctionType.LastValue:
            case WindowFunctionType.NthValue: GetNth(session, result, ordered, rowIdColumn); break;
            case WindowFunctionType.RatioToReport: GetRatioToReport(result, ordered, rowIdColumn); break;
            default: throw DbException.GetInternalError("type=" + type);
        }
    }
    private void GetRank(Dictionary<int, Value> result, List<Value?[]> ordered, int rowIdColumn)
    {
        int number = 0, size = ordered.Count;
        for (int i = 0; i < size; i++)
        {
            Value?[] row = ordered[i];
            if (i == 0) number = 1;
            else if (GetOverOrderBySort()!.Compare(ordered[i - 1], row) != 0)
                number = type == WindowFunctionType.DenseRank ? number + 1 : i + 1;
            Value value = type == WindowFunctionType.PercentRank
                ? number == 1 ? ValueDouble.Zero : ValueDouble.Get((double)(number - 1) / (size - 1))
                : ValueBigint.Get(number);
            result[row[rowIdColumn]!.GetInt()] = value;
        }
    }
    private void GetCumeDist(Dictionary<int, Value> result, List<Value?[]> ordered, int rowIdColumn)
    {
        int size = ordered.Count;
        for (int start = 0; start < size;)
        {
            int end = start + 1;
            while (end < size && overOrderBySort!.Compare(ordered[start], ordered[end]) == 0) end++;
            ValueDouble value = ValueDouble.Get((double)end / size);
            for (int i = start; i < end; i++) result[ordered[i][rowIdColumn]!.GetInt()] = value;
            start = end;
        }
    }
    private static void GetNtile(Dictionary<int, Value> result, List<Value?[]> ordered, int rowIdColumn)
    {
        int size = ordered.Count;
        for (int i = 0; i < size; i++)
        {
            long buckets = ordered[i][0]!.GetLong();
            if (buckets <= 0) throw DbException.GetInvalidValueException("number of tiles", buckets);
            long perTile = size / buckets, numLarger = size - perTile * buckets, largerGroup = numLarger * (perTile + 1);
            long value = i >= largerGroup ? (i - largerGroup) / perTile + numLarger + 1 : i / (perTile + 1) + 1;
            result[ordered[i][rowIdColumn]!.GetInt()] = ValueBigint.Get(value);
        }
    }
    private void GetLeadLag(Dictionary<int, Value> result, List<Value?[]> ordered, int rowIdColumn, SessionLocal session)
    {
        int size = ordered.Count, numExpressions = GetNumExpressions();
        TypeInfo dataType = args![0].GetTypeInfo();
        for (int i = 0; i < size; i++)
        {
            Value?[] row = ordered[i];
            int n = numExpressions >= 2 ? row[1]!.GetInt() : 1;
            if (n < 0) throw DbException.GetInvalidValueException("nth row", n);
            Value? value = null;
            if (n == 0) value = row[0];
            else if (type == WindowFunctionType.Lead)
            {
                if (ignoreNulls)
                {
                    for (int j = i + 1; n > 0 && j < size; j++)
                    { value = ordered[j][0]; if (value != ValueNull.Instance) n--; }
                    if (n > 0) value = null;
                }
                else if (n <= size - i - 1) value = ordered[i + n][0];
            }
            else
            {
                if (ignoreNulls)
                {
                    for (int j = i - 1; n > 0 && j >= 0; j--)
                    { value = ordered[j][0]; if (value != ValueNull.Instance) n--; }
                    if (n > 0) value = null;
                }
                else if (n <= i) value = ordered[i - n][0];
            }
            value ??= numExpressions >= 3 ? row[2]!.ConvertToType(dataType, session) : ValueNull.Instance;
            result[row[rowIdColumn]!.GetInt()] = value;
        }
    }
    private void GetNth(SessionLocal session, Dictionary<int, Value> result, List<Value?[]> ordered, int rowIdColumn)
    {
        for (int i = 0; i < ordered.Count; i++)
        {
            Value?[] row = ordered[i];
            Value value;
            switch (type)
            {
                case WindowFunctionType.FirstValue:
                    value = GetNthValue(WindowFrame.Iterator(over!, session, ordered, GetOverOrderBySort(), i, false), 0, ignoreNulls); break;
                case WindowFunctionType.LastValue:
                    value = GetNthValue(WindowFrame.Iterator(over!, session, ordered, GetOverOrderBySort(), i, true), 0, ignoreNulls); break;
                case WindowFunctionType.NthValue:
                    int n = row[1]!.GetInt();
                    if (n <= 0) throw DbException.GetInvalidValueException("nth row", n);
                    value = GetNthValue(WindowFrame.Iterator(over!, session, ordered, GetOverOrderBySort(), i, fromLast), n - 1, ignoreNulls); break;
                default: throw DbException.GetInternalError("type=" + type);
            }
            result[row[rowIdColumn]!.GetInt()] = value;
        }
    }
    private static void GetRatioToReport(Dictionary<int, Value> result, List<Value?[]> ordered, int rowIdColumn)
    {
        Value? sum = null;
        foreach (Value?[] row in ordered)
        {
            Value value = row[0]!;
            if (value != ValueNull.Instance)
                sum = sum is null ? value.ConvertToType(TypeInfo.TypeDouble) : sum.Add(value.ConvertToType(TypeInfo.TypeDouble));
        }
        if (sum is not null && sum.GetSignum() == 0) sum = null;
        foreach (Value?[] row in ordered)
        {
            Value value = sum is null ? ValueNull.Instance : row[0]!;
            if (sum is not null && value != ValueNull.Instance) value = value.ConvertToType(TypeInfo.TypeDouble).Divide(sum, TypeInfo.TypeDouble);
            result[row[rowIdColumn]!.GetInt()] = value;
        }
    }
    protected override void MapColumnsAnalysis(ColumnResolver resolver, int level, int innerState)
    {
        if (args is not null) foreach (Expression arg in args) arg.MapColumns(resolver, level, innerState);
        base.MapColumnsAnalysis(resolver, level, innerState);
    }
    public override Expression Optimize(SessionLocal session)
    {
        if (over!.GetWindowFrame() is not null && type is not (WindowFunctionType.FirstValue or WindowFunctionType.LastValue or WindowFunctionType.NthValue))
        { string sql = GetTraceSql(); throw DbException.GetSyntaxError(sql, sql.Length - 1); }
        if (over.GetOrderBy() is null)
        {
            if (type.RequiresWindowOrdering()) { string sql = GetTraceSql(); throw DbException.GetSyntaxError(sql, sql.Length - 1, "ORDER BY"); }
        }
        else if (type == WindowFunctionType.RatioToReport) { string sql = GetTraceSql(); throw DbException.GetSyntaxError(sql, sql.Length - 1); }
        base.Optimize(session);
        if (over.GetOrderBy() is null)
        {
            switch (type)
            {
                case WindowFunctionType.Rank:
                case WindowFunctionType.DenseRank: return ValueExpression.Get(ValueBigint.Get(1));
                case WindowFunctionType.PercentRank: return ValueExpression.Get(ValueDouble.Zero);
                case WindowFunctionType.CumeDist: return ValueExpression.Get(ValueDouble.One);
            }
        }
        if (args is not null) for (int i = 0; i < args.Length; i++) args[i] = args[i].Optimize(session);
        return this;
    }
    public override void SetEvaluatable(TableFilter filter, bool value)
    { if (args is not null) foreach (Expression arg in args) arg.SetEvaluatable(filter, value); base.SetEvaluatable(filter, value); }
    public override TypeInfo GetTypeInfo() => type switch
    {
        WindowFunctionType.RowNumber or WindowFunctionType.Rank or WindowFunctionType.DenseRank or WindowFunctionType.Ntile => TypeInfo.TypeBigint,
        WindowFunctionType.PercentRank or WindowFunctionType.CumeDist or WindowFunctionType.RatioToReport => TypeInfo.TypeDouble,
        WindowFunctionType.Lead or WindowFunctionType.Lag or WindowFunctionType.FirstValue or WindowFunctionType.LastValue or WindowFunctionType.NthValue => args![0].GetTypeInfo(),
        _ => throw DbException.GetInternalError("type=" + type)
    };
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        builder.Append(type.GetSql()).Append('(');
        if (args is not null) WriteExpressions(builder, args, sqlFlags);
        builder.Append(')');
        if (fromLast && type == WindowFunctionType.NthValue) builder.Append(" FROM LAST");
        if (ignoreNulls && type is WindowFunctionType.Lead or WindowFunctionType.Lag or WindowFunctionType.FirstValue or WindowFunctionType.LastValue or WindowFunctionType.NthValue)
            builder.Append(" IGNORE NULLS");
        return AppendTailConditions(builder, sqlFlags, type.RequiresWindowOrdering());
    }
    public override int GetCost()
    { int cost = 1; if (args is not null) foreach (Expression arg in args) cost += arg.GetCost(); return cost; }
}
