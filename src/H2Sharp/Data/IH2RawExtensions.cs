/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using H2Sharp.Values;

namespace H2Sharp.Data;

/// <summary>
/// The raw-value registration seam used by optional H2-compatibility packages.
/// </summary>
internal interface IH2RawExtensions
{
    void RegisterRawUdf(
        string name,
        int arity,
        TypeInfo resultType,
        Func<Value[], Value> implementation,
        bool deterministic);
}
