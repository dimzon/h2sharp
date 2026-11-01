/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Host-provided aggregate functions. The lifecycle is modelled after SQL Server CLR
 * aggregates (Init / Accumulate / GetResult), but the execution contract stays H2-shaped:
 * DISTINCT, FILTER and OVER are handled by the engine, and GetResult may be evaluated
 * more than once.
 */
using System.Collections.Generic;
using H2Sharp.Values;

namespace H2Sharp.Data.Extensions;

/// <summary>
/// A user-defined aggregate function.
/// </summary>
/// <remarks>
/// A factory is called for every aggregate instance the engine needs; instances are never
/// shared between groups or statements. <see cref="Init"/> is called once per instance.
/// <see cref="Accumulate"/> receives the argument value directly for a single-argument
/// aggregate, and an <see cref="System.Array"/> of values for a multi-argument aggregate.
/// NULL arguments are passed as <see langword="null"/>.
/// <para>
/// <see cref="GetResult"/> is the SQL Server <c>Terminate</c> analogue, but it may be
/// called more than once for the same instance: window frames and repeated reads re-evaluate
/// it. Implementations must not destroy their state when answering.
/// </para>
/// </remarks>
public interface IH2Aggregate
{
    /// <summary>
    /// Initializes the aggregate instance.
    /// </summary>
    void Init();

    /// <summary>
    /// Returns the result type for the supplied argument types.
    /// </summary>
    /// <param name="inputTypes">the argument types</param>
    /// <returns>the result type</returns>
    TypeInfo GetResultType(IReadOnlyList<TypeInfo> inputTypes);

    /// <summary>
    /// Adds one row to the aggregate.
    /// </summary>
    /// <param name="value">the argument value, or an array for a multi-argument aggregate</param>
    void Accumulate(object? value);

    /// <summary>
    /// Returns the aggregated value.
    /// </summary>
    /// <returns>the aggregated value, or <see langword="null"/> for SQL NULL</returns>
    object? GetResult();
}

/// <summary>
/// An aggregate that can merge another instance of the same aggregate.
/// </summary>
/// <remarks>
/// The current execution engine does not create partial aggregate states, so it never calls
/// <see cref="Merge"/>. The interface is a reserved hook for a future parallel or partial
/// execution path; it is deliberately separate from <see cref="IH2Aggregate"/> so that an
/// aggregate is not forced to implement a method the engine cannot call.
/// </remarks>
public interface IH2MergeableAggregate : IH2Aggregate
{
    /// <summary>
    /// Merges the state of another instance into this instance.
    /// </summary>
    /// <param name="other">the other instance</param>
    void Merge(IH2MergeableAggregate other);
}
