/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.aggregate.Aggregate.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sliced to COUNT, SUM, MIN, MAX, AVG, the STDDEV / VAR family, ANY / EVERY,
 * MEDIAN, ANY_VALUE, PERCENTILE_CONT / PERCENTILE_DISC, LISTAGG, the single-row
 * regressions (REGR_COUNT, REGR_AVGX, REGR_AVGY, REGR_SXX, REGR_SYY) and the
 * two-row regressions (COVAR_POP, COVAR_SAMP, CORR, REGR_SLOPE, REGR_INTERCEPT,
 * REGR_R2, REGR_SXY), ARRAY_AGG, HISTOGRAM, MODE, JSON_ARRAYAGG, JSON_OBJECTAGG and the
 * BIT_*_AGG reductions: the flat,
 * quick and grouped paths carry any of them — reading, mapping and accumulation.
 * Every other aggregate type refuses where the reference would branch to it (the
 * data factory, the value reader and the type switch), so adding the next one is
 * new branches, not a new shape. The static name map is the reference's entire one,
 * as with the built-in function list: it says what a name is, and that answer does
 * not change with the port's progress.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Expressions.Functions;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Util.Json;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// Implements the integrated aggregate functions, such as COUNT, MAX, SUM.
/// </summary>
public class Aggregate : AbstractAggregate
{
    private readonly AggregateType aggregateType;

    private List<QueryOrderBy>? orderByList;
    private Result.SortOrder? orderBySort;

    private object? extraArguments;

    // The reference declares these on the ExpressionWithFlags interface, which is not ported;
    // the names and semantics are the reference's.
    private int flags;

    /// <summary>
    /// Sets the flags of this aggregate.
    /// </summary>
    /// <param name="flags">the flags</param>
    public void SetFlags(int flags) => this.flags = flags;

    /// <summary>
    /// Returns the flags of this aggregate.
    /// </summary>
    /// <returns>the flags</returns>
    public int GetFlags() => flags;

    /// <summary>
    /// Create a new aggregate object.
    /// </summary>
    /// <param name="aggregateType">the aggregate type</param>
    /// <param name="args">the aggregated expressions</param>
    /// <param name="select">the select statement</param>
    /// <param name="distinct">if distinct is used</param>
    public Aggregate(AggregateType aggregateType, Expression[] args, Select select, bool distinct)
        : base(select, args, distinct)
    {
        if (distinct && aggregateType == AggregateType.CountAll)
        {
            throw Message.DbException.GetInternalError();
        }

        this.aggregateType = aggregateType;
    }

    /// <summary>
    /// Returns the type of this aggregate.
    /// </summary>
    /// <returns>the type of this aggregate</returns>
    public AggregateType GetAggregateType() => aggregateType;

    /// <summary>
    /// Returns the select statement.
    /// </summary>
    /// <returns>the select statement</returns>
    public Select GetSelect() => select;

    /// <summary>
    /// Returns if distinct is used.
    /// </summary>
    /// <returns>if distinct is used</returns>
    public bool IsDistinct() => distinct;

    private static readonly Dictionary<string, AggregateType> Aggregates = new(128);

    static Aggregate()
    {
        /*
         * Update initial size of AGGREGATES after editing the following list.
         */
        AddAggregate("COUNT", AggregateType.Count);
        AddAggregate("SUM", AggregateType.Sum);
        AddAggregate("MIN", AggregateType.Min);
        AddAggregate("MAX", AggregateType.Max);
        AddAggregate("AVG", AggregateType.Avg);
        AddAggregate("LISTAGG", AggregateType.Listagg);
        // MySQL compatibility: group_concat(expression, delimiter)
        AddAggregate("GROUP_CONCAT", AggregateType.Listagg);
        // PostgreSQL compatibility: string_agg(expression, delimiter)
        AddAggregate("STRING_AGG", AggregateType.Listagg);
        AddAggregate("STDDEV_SAMP", AggregateType.StddevSamp);
        AddAggregate("STDDEV", AggregateType.StddevSamp);
        AddAggregate("STDDEV_POP", AggregateType.StddevPop);
        AddAggregate("STDDEVP", AggregateType.StddevPop);
        AddAggregate("VAR_POP", AggregateType.VarPop);
        AddAggregate("VARP", AggregateType.VarPop);
        AddAggregate("VAR_SAMP", AggregateType.VarSamp);
        AddAggregate("VAR", AggregateType.VarSamp);
        AddAggregate("VARIANCE", AggregateType.VarSamp);
        AddAggregate("ANY_VALUE", AggregateType.AnyValue);
        AddAggregate("ANY", AggregateType.Any);
        AddAggregate("SOME", AggregateType.Any);
        // PostgreSQL compatibility
        AddAggregate("BOOL_OR", AggregateType.Any);
        AddAggregate("EVERY", AggregateType.Every);
        // PostgreSQL compatibility
        AddAggregate("BOOL_AND", AggregateType.Every);
        AddAggregate("HISTOGRAM", AggregateType.Histogram);
        AddAggregate("BIT_AND_AGG", AggregateType.BitAndAgg);
        AddAggregate("BIT_AND", AggregateType.BitAndAgg);
        AddAggregate("BIT_OR_AGG", AggregateType.BitOrAgg);
        AddAggregate("BIT_OR", AggregateType.BitOrAgg);
        AddAggregate("BIT_XOR_AGG", AggregateType.BitXorAgg);
        AddAggregate("BIT_NAND_AGG", AggregateType.BitNandAgg);
        AddAggregate("BIT_NOR_AGG", AggregateType.BitNorAgg);
        AddAggregate("BIT_XNOR_AGG", AggregateType.BitXnorAgg);

        AddAggregate("COVAR_POP", AggregateType.CovarPop);
        AddAggregate("COVAR_SAMP", AggregateType.CovarSamp);
        AddAggregate("CORR", AggregateType.Corr);
        AddAggregate("REGR_SLOPE", AggregateType.RegrSlope);
        AddAggregate("REGR_INTERCEPT", AggregateType.RegrIntercept);
        AddAggregate("REGR_COUNT", AggregateType.RegrCount);
        AddAggregate("REGR_R2", AggregateType.RegrR2);
        AddAggregate("REGR_AVGX", AggregateType.RegrAvgx);
        AddAggregate("REGR_AVGY", AggregateType.RegrAvgy);
        AddAggregate("REGR_SXX", AggregateType.RegrSxx);
        AddAggregate("REGR_SYY", AggregateType.RegrSyy);
        AddAggregate("REGR_SXY", AggregateType.RegrSxy);

        AddAggregate("RANK", AggregateType.Rank);
        AddAggregate("DENSE_RANK", AggregateType.DenseRank);
        AddAggregate("PERCENT_RANK", AggregateType.PercentRank);
        AddAggregate("CUME_DIST", AggregateType.CumeDist);

        AddAggregate("PERCENTILE_CONT", AggregateType.PercentileCont);
        AddAggregate("PERCENTILE_DISC", AggregateType.PercentileDisc);
        AddAggregate("MEDIAN", AggregateType.Median);

        AddAggregate("ARRAY_AGG", AggregateType.ArrayAgg);
        AddAggregate("MODE", AggregateType.Mode);
        // Oracle compatibility
        AddAggregate("STATS_MODE", AggregateType.Mode);
        AddAggregate("ENVELOPE", AggregateType.Envelope);

        AddAggregate("JSON_OBJECTAGG", AggregateType.JsonObjectagg);
        AddAggregate("JSON_ARRAYAGG", AggregateType.JsonArrayagg);

        AddAggregate("GCD_AGG", AggregateType.GcdAgg);
        AddAggregate("LCM_AGG", AggregateType.LcmAgg);
    }

    private static void AddAggregate(string name, AggregateType type)
    {
        Aggregates.Add(name, type);
    }

    /// <summary>
    /// Get the aggregate type for this name, or null if no aggregate has been
    /// found.
    /// </summary>
    /// <param name="name">the aggregate function name</param>
    /// <returns>null if no aggregate function has been found, or the aggregate type</returns>
    public static AggregateType? GetAggregateType(string name)
    {
        return Aggregates.TryGetValue(name, out AggregateType type) ? type : null;
    }

    /// <inheritdoc/>
    public override void SetEvaluatable(TableFilter tableFilter, bool value)
    {
        if (orderByList is not null)
        {
            foreach (QueryOrderBy o in orderByList)
            {
                o.expression!.SetEvaluatable(tableFilter, value);
            }
        }

        base.SetEvaluatable(tableFilter, value);
    }

    /// <summary>
    /// Sets the ORDER BY list of a WITHIN GROUP aggregate.
    /// </summary>
    /// <param name="orderBy">the ORDER BY list</param>
    public void SetOrderByList(List<QueryOrderBy> orderBy) => orderByList = orderBy;

    /// <summary>
    /// Sets the additional arguments of this aggregate.
    /// </summary>
    /// <param name="extraArguments">the additional arguments</param>
    public void SetExtraArguments(object extraArguments) => this.extraArguments = extraArguments;

    /// <summary>
    /// Returns the additional arguments of this aggregate.
    /// </summary>
    /// <returns>the additional arguments</returns>
    public object? GetExtraArguments() => extraArguments;

    /// <inheritdoc/>
    protected override void UpdateAggregate(SessionLocal session, object aggregateData)
    {
        AggregateData data = (AggregateData)aggregateData;
        Value? v = args.Length == 0 ? null : args[0].GetValue(session);
        // COUNT takes the argument as is; the type-specific arms arrive with their types.
        // The reference wraps values for ORDER BY lists here (updateCollecting), but no
        // reader builds those lists yet, so the wrap would be identity.
        UpdateData(session, data, v, null);
    }

    /// <inheritdoc/>
    protected override object CreateAggregateData()
    {
        switch (aggregateType)
        {
            case AggregateType.CountAll:
            case AggregateType.RegrCount:
                return new AggregateDataCount(true);
            case AggregateType.Count:
                if (!distinct)
                {
                    return new AggregateDataCount(false);
                }

                break;
            case AggregateType.Sum:
            case AggregateType.Avg:
            case AggregateType.RegrAvgx:
            case AggregateType.RegrAvgy:
                if (!distinct)
                {
                    return aggregateType switch
                    {
                        AggregateType.Avg or AggregateType.RegrAvgx or AggregateType.RegrAvgy => new AggregateDataAvg(type!),
                        _ => new AggregateDataDefault(aggregateType, type!),
                    };
                }

                break;
            case AggregateType.Min:
            case AggregateType.Max:
            case AggregateType.Any:
            case AggregateType.Every:
            case AggregateType.BitAndAgg:
            case AggregateType.BitOrAgg:
            case AggregateType.BitXorAgg:
            case AggregateType.BitNandAgg:
            case AggregateType.BitNorAgg:
            case AggregateType.BitXnorAgg:
                // DISTINCT changes nothing for an extremum, a conjunction or a bit
                // reduction: duplicates collapse on their own.
                return new AggregateDataDefault(aggregateType, type!);
            case AggregateType.StddevPop:
            case AggregateType.StddevSamp:
            case AggregateType.VarPop:
            case AggregateType.VarSamp:
            case AggregateType.RegrSxx:
            case AggregateType.RegrSyy:
                if (!distinct)
                {
                    return new AggregateDataStdVar(aggregateType);
                }

                break;
            case AggregateType.Median:
                break;
            case AggregateType.AnyValue:
                if (!distinct)
                {
                    return new AggregateDataAnyValue();
                }

                break;
            case AggregateType.PercentileCont:
            case AggregateType.PercentileDisc:
                break;
            case AggregateType.Listagg:
            case AggregateType.ArrayAgg:
                // NULL values are excluded by Aggregate
                return new AggregateDataCollecting(
                    distinct, orderByList is not null,
                    AggregateDataCollecting.NullCollectionMode.UsedOrImpossible);
            case AggregateType.CovarPop:
            case AggregateType.CovarSamp:
            case AggregateType.RegrSxy:
                return new AggregateDataCovar(aggregateType);
            case AggregateType.Corr:
            case AggregateType.RegrSlope:
            case AggregateType.RegrIntercept:
            case AggregateType.RegrR2:
                return new AggregateDataCorr(aggregateType);
            case AggregateType.Histogram:
                return new AggregateDataDistinctWithCounts(
                    ignoreNulls: false, Engine.Constants.SelectivityDistinctCount);
            case AggregateType.Mode:
                return new AggregateDataDistinctWithCounts(ignoreNulls: true, int.MaxValue);
            case AggregateType.JsonArrayagg:
                return new AggregateDataCollecting(
                    distinct, orderByList is not null,
                    (flags & Util.Json.JsonConstructorUtils.JsonAbsentOnNull) != 0
                        ? AggregateDataCollecting.NullCollectionMode.Excluded
                        : AggregateDataCollecting.NullCollectionMode.UsedOrImpossible);
            case AggregateType.JsonObjectagg:
                // ROW(key, value) is collected, so NULL values cannot be passed.
                return new AggregateDataCollecting(
                    distinct, false, AggregateDataCollecting.NullCollectionMode.UsedOrImpossible);
            default:
                throw new TODOException(
                    "Aggregate.createAggregateData: only COUNT, SUM, MIN, MAX, AVG, STDDEV, VAR, MEDIAN, ANY_VALUE, PERCENTILE, LISTAGG, ARRAY_AGG, HISTOGRAM, MODE, JSON_ARRAYAGG, JSON_OBJECTAGG, the BIT_*_AGG reductions, REGR_COUNT, REGR_AVGX, REGR_AVGY, REGR_SXX, REGR_SYY, COVAR_POP, COVAR_SAMP, CORR, REGR_SLOPE, REGR_INTERCEPT, REGR_R2 and REGR_SXY are ported (type="
                    + aggregateType + ")");
        }

        return new AggregateDataCollecting(
            distinct, false, AggregateDataCollecting.NullCollectionMode.Ignored);
    }

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        return select.IsQuickAggregateQuery() ? GetValueQuick(session) : base.GetValue(session);
    }

    private Value GetValueQuick(SessionLocal session)
    {
        switch (aggregateType)
        {
            case AggregateType.Count:
            case AggregateType.CountAll:
                Table? table = select.GetTopFilter()?.GetTable();
                if (table is null)
                {
                    throw Message.DbException.GetInternalError("type=" + aggregateType);
                }

                return ValueBigint.Get(table.GetRowCount(session));
            case AggregateType.Min:
            case AggregateType.Max:
                {
                    bool first = aggregateType == AggregateType.Min;
                    H2Sharp.Indexes.Index index = GetMinMaxColumnIndex()
                        ?? throw Message.DbException.GetInternalError("type=" + aggregateType);
                    int sortType = index.GetColumns()[0].SortType;
                    if ((sortType & H2Sharp.Result.SortOrder.Descending) != 0)
                    {
                        first = !first;
                    }

                    H2Sharp.Indexes.Cursor cursor = index.FindFirstOrLast(session, first);
                    H2Sharp.Result.SearchRow? row = cursor.GetSearchRow();
                    Value v;
                    if (row is null)
                    {
                        v = ValueNull.Instance;
                    }
                    else
                    {
                        // A missing stored value is NULL the way the reference only knows ValueNull.
                        v = row.GetValue(((ExpressionColumn)args[0]).GetColumn()!.GetColumnId())
                            ?? ValueNull.Instance;
                    }

                    return v;
                }

            default:
                throw Message.DbException.GetInternalError("type=" + aggregateType);
        }
    }

    private H2Sharp.Indexes.Index? GetMinMaxColumnIndex()
    {
        Expression arg = args[0];
        if (arg is ExpressionColumn col)
        {
            Column? column = col.GetColumn();
            TableFilter? filter = col.GetTableFilter();
            if (column is not null && filter is not null)
            {
                return filter.GetTable()?.GetIndexForColumn(column, true, false);
            }
        }

        return null;
    }

    /// <inheritdoc/>
    protected override Value GetAggregatedValue(SessionLocal session, object? aggregateData)
    {
        // Ported from Aggregate.getAggregatedValue: an empty group hands over no data,
        // the aggregate starts fresh and reports its empty-set value (NULL, or 0).
        AggregateData data = (AggregateData?)aggregateData ?? (AggregateData)CreateAggregateData();
        switch (aggregateType)
        {
            case AggregateType.Count:
                if (distinct)
                {
                    return ValueBigint.Get(((AggregateDataCollecting)data).GetCount());
                }

                break;
            case AggregateType.Sum:
                if (distinct)
                {
                    AggregateDataCollecting collected = (AggregateDataCollecting)data;
                    if (collected.GetCount() == 0)
                    {
                        return ValueNull.Instance;
                    }

                    return Collect(session, collected, new AggregateDataDefault(aggregateType, type!));
                }

                break;
            case AggregateType.Avg:
                if (distinct)
                {
                    AggregateDataCollecting collected = (AggregateDataCollecting)data;
                    if (collected.GetCount() == 0)
                    {
                        return ValueNull.Instance;
                    }

                    return Collect(session, collected, new AggregateDataAvg(type!));
                }

                break;
            case AggregateType.StddevPop:
            case AggregateType.StddevSamp:
            case AggregateType.VarPop:
            case AggregateType.VarSamp:
                if (distinct)
                {
                    AggregateDataCollecting collected = (AggregateDataCollecting)data;
                    if (collected.GetCount() == 0)
                    {
                        return ValueNull.Instance;
                    }

                    return Collect(session, collected, new AggregateDataStdVar(aggregateType));
                }

                break;
            case AggregateType.Median:
                {
                    Value[]? array = ((AggregateDataCollecting)data).GetArray();
                    if (array is null)
                    {
                        return ValueNull.Instance;
                    }

                    return Percentile.GetValue(session, array, type!.GetValueType(), null, Percentile.Half, true);
                }

            case AggregateType.AnyValue:
                {
                    if (distinct)
                    {
                        Value[]? values = ((AggregateDataCollecting)data).GetArray();
                        if (values is null)
                        {
                            return ValueNull.Instance;
                        }

                        return values[session.GetRandom().NextInt(values.Length)];
                    }

                    break;
                }

            case AggregateType.PercentileCont:
            case AggregateType.PercentileDisc:
                {
                    AggregateDataCollecting collectingData = (AggregateDataCollecting)data;
                    Value[]? array = collectingData.GetArray();
                    if (array is null)
                    {
                        return ValueNull.Instance;
                    }

                    Value? shared = collectingData.GetSharedArgument();
                    if (shared is null || shared == ValueNull.Instance)
                    {
                        return ValueNull.Instance;
                    }

                    BigDecimal arg = shared.GetBigDecimal();
                    if (arg.Signum() >= 0 && arg.CompareTo(BigDecimal.One) <= 0)
                    {
                        return Percentile.GetValue(
                            session, array, type!.GetValueType(), orderByList,
                            arg, aggregateType == AggregateType.PercentileCont);
                    }

                    throw Message.DbException.GetInvalidValueException(
                        aggregateType == AggregateType.PercentileCont
                            ? "PERCENTILE_CONT argument" : "PERCENTILE_DISC argument",
                        arg);
                }

            case AggregateType.Listagg:
                return GetListagg(session, data);
            case AggregateType.ArrayAgg:
                return GetArrayAgg(session, data);
            case AggregateType.Histogram:
                return GetHistogram(session, data);
            case AggregateType.Mode:
                return GetMode(session, data);
            case AggregateType.JsonArrayagg:
                return GetJsonArrayAgg(session, data);
            case AggregateType.JsonObjectagg:
                return GetJsonObjectAgg(session, data);
            default:
                break;
        }

        return data.GetValue(session);
    }

    private Value GetHistogram(SessionLocal session, AggregateData data)
    {
        SortedDictionary<Value, LongDataCounter>? distinctValues =
            ((AggregateDataDistinctWithCounts)data).GetValues();
        TypeInfo rowType = (TypeInfo)type!.GetExtTypeInfo()!;
        if (distinctValues is null)
        {
            return ValueArray.Get(rowType, [], session);
        }

        ValueRow[] values = new ValueRow[distinctValues.Count];
        int i = 0;
        foreach (KeyValuePair<Value, LongDataCounter> entry in distinctValues)
        {
            LongDataCounter counter = entry.Value;
            values[i] = ValueRow.Get(rowType, [entry.Key, ValueBigint.Get(counter.Count)]);
            i++;
        }

        CompareMode compareMode = session.GetCompareMode();
        Array.Sort(
            values, (v1, v2) => v1.GetList()[0].CompareTo(v2.GetList()[0], session, compareMode));
        return ValueArray.Get(rowType, values, session);
    }

    private Value GetMode(SessionLocal session, AggregateData data)
    {
        Value v = ValueNull.Instance;
        SortedDictionary<Value, LongDataCounter>? distinctValues =
            ((AggregateDataDistinctWithCounts)data).GetValues();
        if (distinctValues is null)
        {
            return v;
        }

        long count = 0L;
        if (orderByList is not null)
        {
            bool desc = (orderByList[0].sortType & Result.SortOrder.Descending) != 0;
            foreach (KeyValuePair<Value, LongDataCounter> entry in distinctValues)
            {
                long c = entry.Value.Count;
                if (c > count)
                {
                    v = entry.Key;
                    count = c;
                }
                else if (c == count)
                {
                    Value v2 = entry.Key;
                    int cmp = v.CompareTypeSafe(v2, session.GetCompareMode(), session);
                    if (desc)
                    {
                        if (cmp >= 0)
                        {
                            continue;
                        }
                    }
                    else if (cmp <= 0)
                    {
                        continue;
                    }

                    v = v2;
                }
            }
        }
        else
        {
            foreach (KeyValuePair<Value, LongDataCounter> entry in distinctValues)
            {
                long c = entry.Value.Count;
                if (c > count)
                {
                    v = entry.Key;
                    count = c;
                }
            }
        }

        return v;
    }

    private Value GetJsonArrayAgg(SessionLocal session, AggregateData data)
    {
        Value[]? array = ((AggregateDataCollecting)data).GetArray();
        if (array is null)
        {
            return ValueNull.Instance;
        }

        if (orderByList is not null)
        {
            SortWithOrderBy(array);
        }

        using MemoryStream baos = new();
        baos.WriteByte((byte)'[');
        foreach (Value value in array)
        {
            Value v = value;
            if (orderByList is not null)
            {
                v = ((ValueRow)v).GetList()[0];
            }

            JsonConstructorUtils.JsonArrayAppend(
                baos, v is not ValueNull ? v : ValueJson.Null, flags);
        }

        baos.WriteByte((byte)']');
        return ValueJson.GetInternal(baos.ToArray());
    }

    private Value GetJsonObjectAgg(SessionLocal session, AggregateData data)
    {
        Value[]? array = ((AggregateDataCollecting)data).GetArray();
        if (array is null)
        {
            return ValueNull.Instance;
        }

        using MemoryStream baos = new();
        baos.WriteByte((byte)'{');
        foreach (Value value in array)
        {
            Value[] row = ((ValueRow)value).GetList();
            string? key = row[0].GetString();
            if (key is null)
            {
                throw Message.DbException.GetInvalidValueException("JSON_OBJECTAGG key", "NULL");
            }

            Value jsonValue = row[1];
            if (jsonValue is ValueNull || ReferenceEquals(jsonValue, ValueJson.Null))
            {
                if ((flags & JsonConstructorUtils.JsonAbsentOnNull) != 0)
                {
                    continue;
                }

                jsonValue = ValueJson.Null;
            }

            JsonConstructorUtils.JsonObjectAppend(baos, key, jsonValue);
        }

        return JsonConstructorUtils.JsonObjectFinish(baos, flags);
    }

    private static Value Collect(SessionLocal session, AggregateDataCollecting c, AggregateData d)
    {
        foreach (Value v in c)
        {
            d.Add(session, v);
        }

        return d.GetValue(session);
    }

    private void SortWithOrderBy(Value[] array)
    {
        // orderBySort is not ported — no ordered aggregate is read yet — so the
        // reference's sort always takes its fallback: session comparison, taken once
        // rather than re-read through the select on every pair.
        Array.Sort(array, orderBySort is null ? select.GetSession().GetValueComparison()
            : orderBySort.GetRowValueComparator());
    }

    private Value GetArrayAgg(SessionLocal session, AggregateData data)
    {
        Value[]? array = ((AggregateDataCollecting)data).GetArray();
        if (array is null)
        {
            return ValueNull.Instance;
        }

        if (orderByList is not null || distinct)
        {
            SortWithOrderBy(array);
        }

        if (orderByList is not null)
        {
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = ((ValueRow)array[i]).GetList()[0];
            }
        }

        return ValueArray.Get((TypeInfo)type!.GetExtTypeInfo()!, array, session);
    }

    private Value GetListagg(SessionLocal session, AggregateData data)
    {
        AggregateDataCollecting collectingData = (AggregateDataCollecting)data;
        Value[]? array = collectingData.GetArray();
        if (array is null)
        {
            return ValueNull.Instance;
        }

        if (array.Length == 1)
        {
            Value v = array[0];
            if (orderByList is not null)
            {
                v = ((ValueRow)v).GetList()[0];
            }

            return v.ConvertToType(TypeInfo.TypeVarchar);
        }

        if (orderByList is not null || distinct)
        {
            SortWithOrderBy(array);
        }

        ListaggArguments arguments = (ListaggArguments)extraArguments!;
        string separator = arguments.GetEffectiveSeparator(session);
        return ValueVarchar.Get(
            (arguments.GetOnOverflowTruncate()
                ? GetListaggTruncate(
                    array, separator, arguments.GetEffectiveFilter(session), arguments.IsWithoutCount())
                : GetListaggError(array, separator)).ToString(),
            session.CastDataProvider);
    }

    private StringBuilder GetListaggError(Value[] array, string separator)
    {
        StringBuilder builder = new(GetListaggItem(array[0]));
        for (int i = 1, count = array.Length; i < count; i++)
        {
            string s = GetListaggItem(array[i]);
            long length = (long)builder.Length + separator.Length + s.Length;
            if (length > Engine.Constants.MaxStringLength)
            {
                int limit = 81;
                StringUtils.AppendToLength(builder, separator, limit);
                StringUtils.AppendToLength(builder, s, limit);
                throw Message.DbException.GetValueTooLongException(
                    "CHARACTER VARYING", builder.ToString(0, limit), -1L);
            }

            _ = builder.Append(separator).Append(s);
        }

        return builder;
    }

    private StringBuilder GetListaggTruncate(Value[] array, string separator, string filter, bool withoutCount)
    {
        int count = array.Length;
        string[] strings = new string[count];
        string s = GetListaggItem(array[0]);
        strings[0] = s;
        int estimatedLength = (int)Math.Min(Engine.Constants.MaxStringLength, s.Length * (long)count);
        StringBuilder builder = new(estimatedLength);
        _ = builder.Append(s);
        for (int i = 1; i < count; i++)
        {
            strings[i] = s = GetListaggItem(array[i]);
            int length = builder.Length;
            long longLength = (long)length + separator.Length + s.Length;
            if (longLength > Engine.Constants.MaxStringLength)
            {
                if (longLength - s.Length >= Engine.Constants.MaxStringLength)
                {
                    i--;
                }
                else
                {
                    _ = builder.Append(separator);
                    length = (int)longLength;
                }

                for (; i > 0; i--)
                {
                    length -= strings[i].Length;
                    builder.Length = length;
                    StringUtils.AppendToLength(builder, filter, Engine.Constants.MaxStringLength + 1);
                    if (!withoutCount)
                    {
                        _ = builder.Append('(').Append(count - i).Append(')');
                    }

                    if (builder.Length <= Engine.Constants.MaxStringLength)
                    {
                        goto Truncated;
                    }

                    length -= separator.Length;
                }

                builder.Length = 0;
                _ = builder.Append(filter).Append('(').Append(count).Append(')');
                break;
            }

            _ = builder.Append(separator).Append(s);
        }

    Truncated:
        return builder;
    }

    private string GetListaggItem(Value v)
    {
        if (orderByList is not null)
        {
            v = ((ValueRow)v).GetList()[0];
        }

        // NULLs never reach the collection: the update arm excludes them.
        return v.GetString()!;
    }

    /// <inheritdoc/>
    protected override void UpdateFromExpressions(SessionLocal session, object aggregateData, Value?[] array)
    {
        if (filterCondition is not null && !array[GetNumExpressions() - 1]!.IsTrue()) return;
        AggregateData data = (AggregateData)aggregateData;
        Value? v = args.Length == 0 ? null : array[0];
        UpdateData(session, data, v, array);
    }

    private void UpdateData(SessionLocal session, AggregateData data, Value? v, Value?[]? remembered)
    {
        switch (aggregateType)
        {
            case AggregateType.CovarPop:
            case AggregateType.CovarSamp:
            case AggregateType.Corr:
            case AggregateType.RegrSlope:
            case AggregateType.RegrIntercept:
            case AggregateType.RegrR2:
            case AggregateType.RegrSxy:
                {
                    Value? x;
                    if (v is ValueNull || (x = GetSecondValue(session, remembered)) is ValueNull)
                    {
                        return;
                    }

                    ((AggregateDataBinarySet)data).Add(session, v, x);
                    return;
                }

            case AggregateType.PercentileCont:
            case AggregateType.PercentileDisc:
                ((AggregateDataCollecting)data).SetSharedArgument(v!);
                v = remembered is not null ? remembered[1] : orderByList![0].expression!.GetValue(session);
                break;
            case AggregateType.RegrCount:
            case AggregateType.RegrAvgy:
            case AggregateType.RegrSyy:
                if (v is ValueNull || GetSecondValue(session, remembered) is ValueNull)
                {
                    return;
                }

                break;
            case AggregateType.RegrAvgx:
            case AggregateType.RegrSxx:
                if (v is ValueNull || (v = GetSecondValue(session, remembered)) is ValueNull)
                {
                    return;
                }

                break;
            case AggregateType.Listagg:
                if (v is ValueNull)
                {
                    return;
                }

                // updateCollecting wraps for ORDER BY lists, but LISTAGG never
                // carries one here: the reader refuses WITHIN GROUP for it.
                v = v!.ConvertToType(TypeInfo.TypeVarchar);
                break;
            case AggregateType.Mode:
                // MODE carries no argument on the expression list: its value rides on the
                // ORDER BY list the reader built.
                v = remembered is not null
                    ? remembered[0]
                    : orderByList![0].expression!.GetValue(session);
                break;
            case AggregateType.JsonArrayagg:
                // updateCollecting wraps for ORDER BY lists, but JSON_ARRAYAGG's list is not
                // read yet, so the value goes as is.
                v = UpdateCollecting(session, v!, remembered);
                break;
            case AggregateType.ArrayAgg:
                v = UpdateCollecting(session, v!, remembered);
                break;
            case AggregateType.JsonObjectagg:
                {
                    Value key = v!;
                    Value value = GetSecondValue(session, remembered)!;
                    if (key is ValueNull)
                    {
                        throw Message.DbException.GetInvalidValueException("JSON_OBJECTAGG key", "NULL");
                    }

                    v = ValueRow.Get([key, value]);
                    break;
                }

            default:
                // Use argument as is
                break;
        }

        // COUNT takes the argument as is; the type-specific arms arrive with their types.
        // The reference wraps values for ORDER BY lists here (updateCollecting), but only
        // ARRAY_AGG-style aggregates carry those lists, so the wrap would be identity.
        data.Add(session, v);
    }

    private Value? GetSecondValue(SessionLocal session, Value?[]? remembered)
        => remembered is not null ? remembered[1] : args[1].GetValue(session);

    private Value UpdateCollecting(SessionLocal session, Value value, Value?[]? remembered)
    {
        if (orderByList is null) return value;
        Value[] row = new Value[1 + orderByList.Count];
        row[0] = value;
        for (int i = 0; i < orderByList.Count; i++)
            row[i + 1] = remembered is null ? orderByList[i].expression!.GetValue(session) : remembered[i + 1]!;
        return ValueRow.Get(row);
    }

    /// <inheritdoc/>
    protected override int GetNumExpressions()
    {
        int n = args.Length;
        if (orderByList is not null)
        {
            n += orderByList.Count;
        }

        if (filterCondition is not null) n++;
        return n;
    }

    /// <inheritdoc/>
    protected override void RememberExpressions(SessionLocal session, Value?[] array)
    {
        int offset = 0;
        for (; offset < args.Length; offset++)
        {
            array[offset] = args[offset].GetValue(session);
        }

        if (orderByList is not null)
        {
            foreach (QueryOrderBy o in orderByList)
            {
                array[offset++] = o.expression!.GetValue(session);
            }
        }
        if (filterCondition is not null) array[offset] = ValueBoolean.Get(filterCondition.GetBooleanValue(session));
    }

    /// <inheritdoc/>
    protected override void MapColumnsAnalysis(ColumnResolver resolver, int level, int innerState)
    {
        if (orderByList is not null)
        {
            foreach (QueryOrderBy o in orderByList)
            {
                o.expression!.MapColumns(resolver, level, innerState);
            }
        }

        base.MapColumnsAnalysis(resolver, level, innerState);
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        base.Optimize(session);
        if (args.Length == 1)
        {
            type = args[0].GetTypeInfo();
        }

        if (orderByList is not null)
        {
            int offset = aggregateType is AggregateType.ArrayAgg or AggregateType.Listagg or AggregateType.JsonArrayagg ? 1 : 0;
            for (int i = 0; i < orderByList.Count; i++)
            {
                QueryOrderBy o = orderByList[i];
                o.expression = o.expression!.Optimize(session);
                if (offset != 0 && o.expression.IsConstant()) orderByList.RemoveAt(i--);
            }
            if (orderByList.Count == 0) orderByList = null;
            else orderBySort = CreateOrder(session, orderByList, offset);
        }

        switch (aggregateType)
        {
            case AggregateType.Count:
                if (args[0].IsConstant() && over is null)
                {
                    if (args[0].IsNullConstant())
                    {
                        return ValueExpression.Get(ValueBigint.Get(0L));
                    }

                    if (!distinct)
                    {
                        Aggregate rewritten = new(AggregateType.CountAll, [], select, false);
                        if (filterCondition is not null)
                        {
                            rewritten.SetFilterCondition(filterCondition);
                        }

                        return rewritten.Optimize(session);
                    }
                }

                type = TypeInfo.TypeBigint;
                break;
            case AggregateType.CountAll:
            case AggregateType.RegrCount:
                type = TypeInfo.TypeBigint;
                break;
            case AggregateType.Sum:
                type = GetSumType(type!) ?? throw Message.DbException.Get(
                    Api.ErrorCode.SumOrAvgOnWrongDatatype1, GetTraceSql());
                break;
            case AggregateType.Min:
            case AggregateType.Max:
            case AggregateType.AnyValue:
                break;
            case AggregateType.Any:
            case AggregateType.Every:
                type = TypeInfo.TypeBoolean;
                break;
            case AggregateType.Avg:
                type = GetAvgType(type!) ?? throw Message.DbException.Get(
                    Api.ErrorCode.SumOrAvgOnWrongDatatype1, GetTraceSql());
                break;
            case AggregateType.StddevPop:
            case AggregateType.StddevSamp:
            case AggregateType.VarPop:
            case AggregateType.VarSamp:
            case AggregateType.CovarPop:
            case AggregateType.CovarSamp:
            case AggregateType.Corr:
            case AggregateType.RegrSlope:
            case AggregateType.RegrIntercept:
            case AggregateType.RegrR2:
            case AggregateType.RegrSxx:
            case AggregateType.RegrSyy:
            case AggregateType.RegrSxy:
                type = TypeInfo.TypeDouble;
                break;
            case AggregateType.Median:
                switch (type!.GetValueType())
                {
                    case Value.Tinyint:
                    case Value.Smallint:
                    case Value.Integer:
                    case Value.Bigint:
                    case Value.Numeric:
                    case Value.Real:
                    case Value.Double:
                    case Value.Decfloat:
                        type = TypeInfo.TypeNumericFloatingPoint;
                        break;
                }

                break;
            case AggregateType.PercentileCont:
                type = orderByList![0].expression!.GetTypeInfo();
                goto case AggregateType.Median;
            case AggregateType.PercentileDisc:
                type = orderByList![0].expression!.GetTypeInfo();
                break;
            case AggregateType.RegrAvgx:
                type = GetAvgType(args[1].GetTypeInfo()) ?? throw Message.DbException.Get(
                    Api.ErrorCode.SumOrAvgOnWrongDatatype1, GetTraceSql());
                break;
            case AggregateType.RegrAvgy:
                type = GetAvgType(args[0].GetTypeInfo()) ?? throw Message.DbException.Get(
                    Api.ErrorCode.SumOrAvgOnWrongDatatype1, GetTraceSql());
                break;
            case AggregateType.ArrayAgg:
                type = TypeInfo.GetTypeInfo(Value.Array, -1, 0, args[0].GetTypeInfo());
                break;
            case AggregateType.Histogram:
                {
                    OrderedStringMap<TypeInfo> fields = new(4);
                    fields.Put("VALUE", type!);
                    fields.Put("COUNT", TypeInfo.TypeBigint);
                    type = TypeInfo.GetTypeInfo(
                        Value.Array, -1, 0,
                        TypeInfo.GetTypeInfo(Value.Row, -1, -1, new ExtTypeInfoRow(fields)));
                    break;
                }
            case AggregateType.Mode:
                // MODE takes no argument on the expression list; the value it reads is carried
                // on the ORDER BY list, so there is nothing to type here.
                break;
            case AggregateType.JsonObjectagg:
            case AggregateType.JsonArrayagg:
                type = TypeInfo.TypeJson;
                break;
            case AggregateType.BitAndAgg:
            case AggregateType.BitOrAgg:
            case AggregateType.BitXorAgg:
            case AggregateType.BitNandAgg:
            case AggregateType.BitNorAgg:
            case AggregateType.BitXnorAgg:
                type = BitFunction.CheckArgType(args[0]);
                break;
            case AggregateType.Listagg:
                type = TypeInfo.TypeVarchar;
                break;
            default:
                throw Message.DbException.GetInternalError("type=" + aggregateType);
        }

        return this;
    }

    /// <summary>
    /// The additional result precision in decimal digits for a SUM aggregate function.
    /// </summary>
    private const int AdditionalSumPrecision = 10;

    /// <summary>
    /// The result type of a SUM over the argument type, or null when SUM makes no sense.
    /// </summary>
    /// <param name="type">the argument type</param>
    /// <returns>the result type, or null</returns>
    private static TypeInfo? GetSumType(TypeInfo type)
    {
        switch (type.GetValueType())
        {
            case Value.Boolean:
            case Value.Tinyint:
            case Value.Smallint:
            case Value.Integer:
                return TypeInfo.TypeBigint;
            case Value.Bigint:
                return TypeInfo.GetTypeInfo(
                    Value.Numeric, ValueBigint.DecimalPrecision + AdditionalSumPrecision, -1, null);
            case Value.Numeric:
                return TypeInfo.GetTypeInfo(
                    Value.Numeric, type.GetPrecision() + AdditionalSumPrecision, type.GetDeclaredScale(), null);
            case Value.Real:
                return TypeInfo.TypeDouble;
            case Value.Double:
                return TypeInfo.GetTypeInfo(
                    Value.Decfloat, ValueDouble.DecimalPrecision + AdditionalSumPrecision, -1, null);
            case Value.Decfloat:
                return TypeInfo.GetTypeInfo(
                    Value.Decfloat, type.GetPrecision() + AdditionalSumPrecision, -1, null);
            default:
                if (type.GetValueType() is >= Value.IntervalYear and <= Value.IntervalMinuteToSecond)
                {
                    throw new TODOException("Aggregate.getSumType: SUM over intervals is not ported");
                }

                return null;
        }
    }

    /// <summary>
    /// The additional result scale in decimal digits for an AVG aggregate function.
    /// </summary>
    private const int AdditionalAvgScale = 10;

    /// <summary>
    /// The result type of an AVG over the argument type, or null when AVG makes no sense.
    /// </summary>
    /// <param name="type">the argument type</param>
    /// <returns>the result type, or null</returns>
    private static TypeInfo? GetAvgType(TypeInfo type)
    {
        switch (type.GetValueType())
        {
            case Value.Tinyint:
            case Value.Smallint:
            case Value.Integer:
            case Value.Real:
                return TypeInfo.TypeDouble;
            case Value.Bigint:
                return TypeInfo.GetTypeInfo(
                    Value.Numeric, ValueBigint.DecimalPrecision + AdditionalAvgScale, AdditionalAvgScale, null);
            case Value.Numeric:
                {
                    int additionalScale = Math.Min(
                        ValueNumeric.MaximumScale - type.GetScale(),
                        Math.Min(Engine.Constants.MaxNumericPrecision - (int)type.GetPrecision(), AdditionalAvgScale));
                    return TypeInfo.GetTypeInfo(
                        Value.Numeric, type.GetPrecision() + additionalScale, type.GetScale() + additionalScale, null);
                }

            case Value.Double:
                return TypeInfo.GetTypeInfo(
                    Value.Decfloat, ValueDouble.DecimalPrecision + AdditionalAvgScale, -1, null);
            case Value.Decfloat:
                return TypeInfo.GetTypeInfo(
                    Value.Decfloat, type.GetPrecision() + AdditionalAvgScale, -1, null);
            case Value.IntervalYear:
            case Value.IntervalYearToMonth:
                return TypeInfo.GetTypeInfo(Value.IntervalYearToMonth, type.GetDeclaredPrecision(), 0, null);
            case Value.IntervalMonth:
                return TypeInfo.GetTypeInfo(Value.IntervalMonth, type.GetDeclaredPrecision(), 0, null);
            case Value.IntervalDay:
            case Value.IntervalDayToHour:
            case Value.IntervalDayToMinute:
            case Value.IntervalDayToSecond:
                return TypeInfo.GetTypeInfo(
                    Value.IntervalDayToSecond, type.GetDeclaredPrecision(), ValueInterval.MaximumScale, null);
            case Value.IntervalHour:
            case Value.IntervalHourToMinute:
            case Value.IntervalHourToSecond:
                return TypeInfo.GetTypeInfo(
                    Value.IntervalHourToSecond, type.GetDeclaredPrecision(), ValueInterval.MaximumScale, null);
            case Value.IntervalMinute:
            case Value.IntervalMinuteToSecond:
                return TypeInfo.GetTypeInfo(
                    Value.IntervalMinuteToSecond, type.GetDeclaredPrecision(), ValueInterval.MaximumScale, null);
            case Value.IntervalSecond:
                return TypeInfo.GetTypeInfo(
                    Value.IntervalSecond, type.GetDeclaredPrecision(), ValueInterval.MaximumScale, null);
            default:
                return null;
        }
    }

    /// <inheritdoc/>
    public override bool IsEverything(ExpressionVisitor visitor)
    {
        if (!base.IsEverything(visitor))
        {
            return false;
        }

        if (filterCondition is not null && !filterCondition.IsEverything(visitor))
        {
            return false;
        }

        if (visitor.GetVisitorType() == ExpressionVisitor.OptimizableAggregate)
        {
            switch (aggregateType)
            {
                case AggregateType.Count:
                    if (distinct || args[0].GetNullable() != Column.NotNullable)
                    {
                        return false;
                    }

                    goto case AggregateType.CountAll;
                case AggregateType.CountAll:
                    Table? quickTable = visitor.GetTable();
                    return quickTable is not null && quickTable.CanGetRowCount(select.GetSession());
                case AggregateType.Min:
                case AggregateType.Max:
                    return GetMinMaxColumnIndex() is not null;
                default:
                    return false;
            }
        }

        if (visitor.GetVisitorType() == ExpressionVisitor.Deterministic
            && aggregateType == AggregateType.AnyValue)
        {
            return false;
        }

        foreach (Expression arg in args)
        {
            if (!arg.IsEverything(visitor))
            {
                return false;
            }
        }

        if (orderByList is not null)
        {
            foreach (QueryOrderBy o in orderByList)
            {
                if (!o.expression!.IsEverything(visitor))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetCost()
    {
        int cost = 1;
        foreach (Expression arg in args)
        {
            cost += arg.GetCost();
        }

        if (orderByList is not null)
        {
            foreach (QueryOrderBy o in orderByList)
            {
                cost += o.expression!.GetCost();
            }
        }

        if (filterCondition is not null)
        {
            cost += filterCondition.GetCost();
        }

        return cost;
    }

    /// <inheritdoc/>
    public override StringBuilder GetUnenclosedSql(StringBuilder builder, int sqlFlags)
    {
        if (aggregateType == AggregateType.CountAll)
        {
            return AppendTailConditions(builder.Append("COUNT(*)"), sqlFlags, false);
        }

        if (aggregateType == AggregateType.Listagg)
        {
            return GetSqlListagg(builder, sqlFlags);
        }

        if (aggregateType == AggregateType.JsonArrayagg)
        {
            return GetSqlJsonArrayAgg(builder, sqlFlags);
        }

        if (aggregateType == AggregateType.JsonObjectagg)
        {
            return GetSqlJsonObjectAgg(builder, sqlFlags);
        }

        string name = aggregateType switch
        {
            AggregateType.Count => "COUNT",
            AggregateType.Sum => "SUM",
            AggregateType.Min => "MIN",
            AggregateType.Max => "MAX",
            AggregateType.Avg => "AVG",
            AggregateType.StddevPop => "STDDEV_POP",
            AggregateType.StddevSamp => "STDDEV_SAMP",
            AggregateType.VarPop => "VAR_POP",
            AggregateType.VarSamp => "VAR_SAMP",
            AggregateType.Any => "ANY",
            AggregateType.Every => "EVERY",
            AggregateType.Median => "MEDIAN",
            AggregateType.AnyValue => "ANY_VALUE",
            AggregateType.PercentileCont => "PERCENTILE_CONT",
            AggregateType.PercentileDisc => "PERCENTILE_DISC",
            AggregateType.Listagg => "LISTAGG",
            AggregateType.ArrayAgg => "ARRAY_AGG",
            AggregateType.Histogram => "HISTOGRAM",
            AggregateType.Mode => "MODE",
            AggregateType.JsonArrayagg => "JSON_ARRAYAGG",
            AggregateType.JsonObjectagg => "JSON_OBJECTAGG",
            AggregateType.BitAndAgg => "BIT_AND_AGG",
            AggregateType.BitOrAgg => "BIT_OR_AGG",
            AggregateType.BitXorAgg => "BIT_XOR_AGG",
            AggregateType.BitNandAgg => "BIT_NAND_AGG",
            AggregateType.BitNorAgg => "BIT_NOR_AGG",
            AggregateType.BitXnorAgg => "BIT_XNOR_AGG",
            AggregateType.RegrCount => "REGR_COUNT",
            AggregateType.RegrAvgx => "REGR_AVGX",
            AggregateType.RegrAvgy => "REGR_AVGY",
            AggregateType.RegrSxx => "REGR_SXX",
            AggregateType.RegrSyy => "REGR_SYY",
            AggregateType.CovarPop => "COVAR_POP",
            AggregateType.CovarSamp => "COVAR_SAMP",
            AggregateType.Corr => "CORR",
            AggregateType.RegrSlope => "REGR_SLOPE",
            AggregateType.RegrIntercept => "REGR_INTERCEPT",
            AggregateType.RegrR2 => "REGR_R2",
            AggregateType.RegrSxy => "REGR_SXY",
            _ => throw Message.DbException.GetInternalError("type=" + aggregateType),
        };

        _ = builder.Append(distinct ? name + "(DISTINCT " : name + "(");
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            args[i].GetUnenclosedSql(builder, sqlFlags);
        }

        _ = builder.Append(')');
        AppendWithinGroup(builder, sqlFlags);
        return AppendTailConditions(builder, sqlFlags, false);
    }

    private StringBuilder GetSqlListagg(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append("LISTAGG(");
        if (distinct)
        {
            _ = builder.Append("DISTINCT ");
        }

        args[0].GetUnenclosedSql(builder, sqlFlags);
        ListaggArguments arguments = (ListaggArguments)extraArguments!;
        Expression? separator = arguments.GetSeparator();
        if (separator is not null)
        {
            separator.GetUnenclosedSql(builder.Append(", "), sqlFlags);
        }

        if (arguments.GetOnOverflowTruncate())
        {
            _ = builder.Append(" ON OVERFLOW TRUNCATE ");
            Expression? filter = arguments.GetFilter();
            if (filter is not null)
            {
                filter.GetUnenclosedSql(builder, sqlFlags).Append(' ');
            }

            _ = builder.Append(arguments.IsWithoutCount() ? "WITHOUT" : "WITH").Append(" COUNT");
        }

        _ = builder.Append(')');
        // The reference always renders WITHIN GROUP here; with no ORDER BY list
        // there is nothing to render, and the reader refuses that clause anyway.
        AppendWithinGroup(builder, sqlFlags);
        return AppendTailConditions(builder, sqlFlags, false);
    }

    private StringBuilder GetSqlJsonArrayAgg(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append("JSON_ARRAYAGG(");
        if (distinct)
        {
            _ = builder.Append("DISTINCT ");
        }

        args[0].GetUnenclosedSql(builder, sqlFlags);
        JsonConstructorFunction.GetJsonFunctionFlagsSql(builder, flags, forArray: true);
        _ = builder.Append(')');
        return AppendTailConditions(builder, sqlFlags, false);
    }

    private StringBuilder GetSqlJsonObjectAgg(StringBuilder builder, int sqlFlags)
    {
        _ = builder.Append("JSON_OBJECTAGG(");
        args[0].GetUnenclosedSql(builder, sqlFlags).Append(": ");
        args[1].GetUnenclosedSql(builder, sqlFlags);
        JsonConstructorFunction.GetJsonFunctionFlagsSql(builder, flags, forArray: false).Append(')');
        return AppendTailConditions(builder, sqlFlags, false);
    }

    private void AppendWithinGroup(StringBuilder builder, int sqlFlags)
    {
        if (orderByList is null)
        {
            return;
        }

        _ = builder.Append(" WITHIN GROUP (ORDER BY ");
        for (int i = 0; i < orderByList.Count; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            orderByList[i].GetSql(builder, sqlFlags);
        }

        _ = builder.Append(')');
    }
}
