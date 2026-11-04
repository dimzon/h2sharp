/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The production extension surface of H2Connection. It is implemented explicitly, so the
 * registration methods do not appear on the ordinary ADO.NET connection surface.
 */
using System;
using System.Security.Cryptography;
using H2Sharp.Data.Extensions;

namespace H2Sharp.Data;

/// <summary>
/// Registers host-provided functions and platform services on a closed connection.
/// </summary>
/// <remarks>
/// Registration is the .NET extension surface, not SQL <c>CREATE ALIAS</c>: the embedded
/// engine stores no executable code, so functions registered here are session-scoped and
/// never become schema objects (D-037). The connection owns the configuration until it is
/// opened; opening takes an immutable snapshot, so an active session is never reconfigured
/// underneath a statement.
/// </remarks>
public interface IH2Extensions
{
    /// <summary>Registers a scalar function with no arguments.</summary>
    void RegisterUdf<TResult>(string name, Func<TResult> implementation, bool deterministic = false);

    /// <summary>Registers a scalar function with one argument.</summary>
    void RegisterUdf<T1, TResult>(string name, Func<T1, TResult> implementation, bool deterministic = false);

    /// <summary>Registers a scalar function with two arguments.</summary>
    void RegisterUdf<T1, T2, TResult>(string name, Func<T1, T2, TResult> implementation, bool deterministic = false);

    /// <summary>Registers a scalar function with three arguments.</summary>
    void RegisterUdf<T1, T2, T3, TResult>(string name, Func<T1, T2, T3, TResult> implementation, bool deterministic = false);

    /// <summary>Registers a scalar function with four arguments.</summary>
    void RegisterUdf<T1, T2, T3, T4, TResult>(string name, Func<T1, T2, T3, T4, TResult> implementation, bool deterministic = false);

    /// <summary>
    /// Registers a user-defined aggregate provider.
    /// </summary>
    /// <param name="name">the SQL name</param>
    /// <param name="provider">the aggregate provider</param>
    void RegisterAggregate(string name, IH2AggregateProvider provider);

    /// <summary>
    /// Registers a hash algorithm factory. The factory is called for every operation, so the
    /// returned stateful algorithm is never shared between concurrent statements.
    /// </summary>
    void RegisterHashAlgorithm(string name, Func<HashAlgorithm> factory);

    /// <summary>Registers the session random generator factory.</summary>
    void RegisterRandomGenerator(Func<long?, Random> factory);

    /// <summary>Registers the session UUID generator by RFC 9562 version.</summary>
    void RegisterUuidGenerator(Func<int, Guid> factory);
}
