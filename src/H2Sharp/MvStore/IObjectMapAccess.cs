/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A versioned map seen through its erased key: the undo log and rollback store an Object
 * key (a boxed Long for a primary index) and must reach a map whose key type is K. Java
 * gets that from erasure; C# needs this seam.
 */
using System;
using H2Sharp.Values;

namespace H2Sharp.MvStore;

/// <summary>
/// The object-keyed view of a map whose values are versioned values.
/// </summary>
public interface IObjectMapAccess
{
    /// <summary>
    /// The map id.
    /// </summary>
    int MapId { get; }

    /// <summary>
    /// Whether the map is closed.
    /// </summary>
    bool IsClosed();

    /// <summary>
    /// The map name.
    /// </summary>
    /// <returns>the name</returns>
    string? GetName();

    /// <summary>
    /// Gets an entry by the erased key.
    /// </summary>
    /// <param name="key">the key</param>
    /// <returns>the value, or null</returns>
    VersionedValue<object>? GetObject(object? key);

    /// <summary>
    /// Operates on an entry by the erased key.
    /// </summary>
    /// <param name="key">the key</param>
    /// <param name="value">the offered value, or null</param>
    /// <param name="decisionMaker">the decision maker</param>
    /// <returns>the previous value</returns>
    VersionedValue<object>? OperateObject(
        object? key, VersionedValue<object>? value, DecisionMaker<VersionedValue<object>> decisionMaker);

    /// <summary>
    /// Writes an erased key with the map's key type.
    /// </summary>
    /// <param name="buff">the target</param>
    /// <param name="key">the key</param>
    void WriteKey(WriteBuffer buff, object? key);

    /// <summary>
    /// Reads an erased key with the map's key type.
    /// </summary>
    /// <param name="buff">the source</param>
    /// <param name="position">the position</param>
    /// <returns>the key</returns>
    object? ReadKey(Span<byte> buff, ref int position);

    /// <summary>
    /// Writes an erased versioned value with the map's value type.
    /// </summary>
    /// <param name="buff">the target</param>
    /// <param name="value">the value</param>
    void WriteValue(WriteBuffer buff, VersionedValue<object>? value);

    /// <summary>
    /// Reads an erased versioned value with the map's value type.
    /// </summary>
    /// <param name="buff">the source</param>
    /// <param name="position">the position</param>
    /// <returns>the value</returns>
    VersionedValue<object>? ReadValue(Span<byte> buff, ref int position);

    /// <summary>
    /// The memory of an erased key.
    /// </summary>
    /// <param name="key">the key</param>
    /// <returns>the estimate</returns>
    int GetKeyMemory(object? key);

    /// <summary>
    /// The memory of an erased versioned value.
    /// </summary>
    /// <param name="value">the value</param>
    /// <returns>the estimate</returns>
    int GetValueMemory(VersionedValue<object>? value);
}
