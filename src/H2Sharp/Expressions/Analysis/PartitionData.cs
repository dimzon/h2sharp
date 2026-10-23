/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.expression.analysis.PartitionData. */
using System.Collections.Generic;
using H2Sharp.Values;
namespace H2Sharp.Expressions.Analysis;

/// <summary>Data and cached results of a window partition.</summary>
public sealed class PartitionData
{
    private object? data;
    private Value? result;
    private Dictionary<int, Value>? orderedResult;
    internal PartitionData(object data) => this.data = data;
    internal object? GetData() => data;
    internal Value? GetResult() => result;
    internal void SetResult(Value result) { this.result = result; data = null; }
    internal Dictionary<int, Value>? GetOrderedResult() => orderedResult;
    internal void SetOrderedResult(Dictionary<int, Value> result) { orderedResult = result; data = null; }
}
