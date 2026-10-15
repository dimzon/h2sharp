/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.expression.aggregate.AggregateType.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
namespace H2Sharp.Expressions.Aggregate;

/// <summary>
/// The type of an aggregate function.
/// </summary>
public enum AggregateType
{
    /// <summary>
    /// The aggregate type for COUNT(*).
    /// </summary>
    CountAll,

    /// <summary>
    /// The aggregate type for COUNT(expression).
    /// </summary>
    Count,

    /// <summary>
    /// The aggregate type for SUM(expression).
    /// </summary>
    Sum,

    /// <summary>
    /// The aggregate type for MIN(expression).
    /// </summary>
    Min,

    /// <summary>
    /// The aggregate type for MAX(expression).
    /// </summary>
    Max,

    /// <summary>
    /// The aggregate type for AVG(expression).
    /// </summary>
    Avg,

    /// <summary>
    /// The aggregate type for STDDEV_POP(expression).
    /// </summary>
    StddevPop,

    /// <summary>
    /// The aggregate type for STDDEV_SAMP(expression).
    /// </summary>
    StddevSamp,

    /// <summary>
    /// The aggregate type for VAR_POP(expression).
    /// </summary>
    VarPop,

    /// <summary>
    /// The aggregate type for VAR_SAMP(expression).
    /// </summary>
    VarSamp,

    /// <summary>
    /// The aggregate type for ANY_VALUE(expression).
    /// </summary>
    AnyValue,

    /// <summary>
    /// The aggregate type for ANY(expression).
    /// </summary>
    Any,

    /// <summary>
    /// The aggregate type for EVERY(expression).
    /// </summary>
    Every,

    /// <summary>
    /// The aggregate type for BIT_AND_AGG(expression).
    /// </summary>
    BitAndAgg,

    /// <summary>
    /// The aggregate type for BIT_OR_AGG(expression).
    /// </summary>
    BitOrAgg,

    /// <summary>
    /// The aggregate type for BIT_XOR_AGG(expression).
    /// </summary>
    BitXorAgg,

    /// <summary>
    /// The aggregate type for BIT_NAND_AGG(expression).
    /// </summary>
    BitNandAgg,

    /// <summary>
    /// The aggregate type for BIT_NOR_AGG(expression).
    /// </summary>
    BitNorAgg,

    /// <summary>
    /// The aggregate type for BIT_XNOR_AGG(expression).
    /// </summary>
    BitXnorAgg,

    /// <summary>
    /// The aggregate type for HISTOGRAM(expression).
    /// </summary>
    Histogram,

    /// <summary>
    /// The aggregate type for COVAR_POP binary set function.
    /// </summary>
    CovarPop,

    /// <summary>
    /// The aggregate type for COVAR_SAMP binary set function.
    /// </summary>
    CovarSamp,

    /// <summary>
    /// The aggregate type for CORR binary set function.
    /// </summary>
    Corr,

    /// <summary>
    /// The aggregate type for REGR_SLOPE binary set function.
    /// </summary>
    RegrSlope,

    /// <summary>
    /// The aggregate type for REGR_INTERCEPT binary set function.
    /// </summary>
    RegrIntercept,

    /// <summary>
    /// The aggregate type for REGR_COUNT binary set function.
    /// </summary>
    RegrCount,

    /// <summary>
    /// The aggregate type for REGR_R2 binary set function.
    /// </summary>
    RegrR2,

    /// <summary>
    /// The aggregate type for REGR_AVGX binary set function.
    /// </summary>
    RegrAvgx,

    /// <summary>
    /// The aggregate type for REGR_AVGY binary set function.
    /// </summary>
    RegrAvgy,

    /// <summary>
    /// The aggregate type for REGR_SXX binary set function.
    /// </summary>
    RegrSxx,

    /// <summary>
    /// The aggregate type for REGR_SYY binary set function.
    /// </summary>
    RegrSyy,

    /// <summary>
    /// The aggregate type for REGR_SXY binary set function.
    /// </summary>
    RegrSxy,

    /// <summary>
    /// The type for RANK() hypothetical set function.
    /// </summary>
    Rank,

    /// <summary>
    /// The type for DENSE_RANK() hypothetical set function.
    /// </summary>
    DenseRank,

    /// <summary>
    /// The type for PERCENT_RANK() hypothetical set function.
    /// </summary>
    PercentRank,

    /// <summary>
    /// The type for CUME_DIST() hypothetical set function.
    /// </summary>
    CumeDist,

    /// <summary>
    /// The aggregate type for PERCENTILE_CONT(expression).
    /// </summary>
    PercentileCont,

    /// <summary>
    /// The aggregate type for PERCENTILE_DISC(expression).
    /// </summary>
    PercentileDisc,

    /// <summary>
    /// The aggregate type for MEDIAN(expression).
    /// </summary>
    Median,

    /// <summary>
    /// The aggregate type for LISTAGG(...).
    /// </summary>
    Listagg,

    /// <summary>
    /// The aggregate type for ARRAY_AGG(expression).
    /// </summary>
    ArrayAgg,

    /// <summary>
    /// The aggregate type for MODE(expression).
    /// </summary>
    Mode,

    /// <summary>
    /// The aggregate type for ENVELOPE(expression).
    /// </summary>
    Envelope,

    /// <summary>
    /// The aggregate type for JSON_OBJECTAGG(expression: expression).
    /// </summary>
    JsonObjectagg,

    /// <summary>
    /// The aggregate type for JSON_ARRAYAGG(expression).
    /// </summary>
    JsonArrayagg,

    /// <summary>
    /// The aggregate type for GCD_AGG(expression).
    /// </summary>
    GcdAgg,

    /// <summary>
    /// The aggregate type for LCM_AGG(expression).
    /// </summary>
    LcmAgg,
}
