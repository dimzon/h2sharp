/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */

using H2Sharp.MvStore.Type;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// The data plane of a transactional map. The reference reaches one
/// <c>MVMap&lt;K, VersionedValue&lt;V&gt;&gt;</c> through erasure; here the two
/// implementations are a map typed by key and the object-keyed map the port
/// keeps for the spatial index and the system maps.
/// </summary>
/// <typeparam name="K">the key type the transaction map answers with</typeparam>
public interface ITransactionMapData<K>
{
    /// <summary>
    /// The map id.
    /// </summary>
    int GetId();

    /// <summary>
    /// The map name.
    /// </summary>
    /// <returns>the name</returns>
    string? GetName();

    /// <summary>
    /// The underlying map, for the store's commit and rollback passes.
    /// </summary>
    IAnyMap InnerMap { get; }

    /// <summary>
    /// Whether the map is closed.
    /// </summary>
    bool IsClosed();

    /// <summary>
    /// Drops all entries.
    /// </summary>
    void Clear();

    /// <summary>
    /// Marks the map volatile.
    /// </summary>
    void SetVolatile(bool isVolatile);

    /// <summary>
    /// The number of entries as the map sees it.
    /// </summary>
    /// <returns>the count</returns>
    long SizeAsLong();

    /// <summary>
    /// The number of entries, uncapped.
    /// </summary>
    /// <returns>the count</returns>
    long SizeAsLongMax();

    /// <summary>
    /// The disk space of the map's current root.
    /// </summary>
    /// <param name="approximate">whether an estimate is allowed</param>
    /// <returns>the bytes</returns>
    long GetDiskSpaceUsed(bool approximate);

    /// <summary>
    /// The last key of the map, boxed.
    /// </summary>
    /// <returns>the key, or null when empty</returns>
    object? LastKey();

    /// <summary>
    /// The number of entries of the given root reference.
    /// </summary>
    /// <param name="root">the root reference</param>
    /// <returns>the count</returns>
    long GetTotalCount(object root);

    /// <summary>
    /// The key type, when the map is typed by key; the object map answers the adapter.
    /// </summary>
    /// <returns>the type</returns>
    object GetKeyType();

    /// <summary>
    /// Compares two keys by the map's order.
    /// </summary>
    /// <param name="a">the first key</param>
    /// <param name="b">the second key</param>
    /// <returns>the comparison</returns>
    int CompareKeys(K a, K? b);

    /// <summary>
    /// The value type of the versioned values.
    /// </summary>
    /// <returns>the type</returns>
    DataType<VersionedValue<object>> GetValueType();

    /// <summary>
    /// Gets an entry.
    /// </summary>
    /// <param name="key">the key</param>
    /// <returns>the value, or null</returns>
    VersionedValue<object>? Get(K key);

    /// <summary>
    /// Gets an entry from a root this map produced.
    /// </summary>
    /// <param name="root">the root reference</param>
    /// <param name="key">the key</param>
    /// <returns>the value, or null</returns>
    VersionedValue<object>? Get(object root, K key);

    /// <summary>
    /// Puts an entry.
    /// </summary>
    /// <param name="key">the key</param>
    /// <param name="value">the versioned value</param>
    /// <returns>the previous value</returns>
    VersionedValue<object>? Put(K key, VersionedValue<object> value);

    /// <summary>
    /// Operates on an entry with a decision maker.
    /// </summary>
    /// <param name="key">the key</param>
    /// <param name="value">the offered value, or null</param>
    /// <param name="decisionMaker">the decision maker</param>
    /// <returns>the previous value</returns>
    VersionedValue<object>? Operate(
        K key, VersionedValue<object>? value, DecisionMaker<VersionedValue<object>> decisionMaker);

    /// <summary>
    /// Appends an entry that sorts after every existing one.
    /// </summary>
    /// <param name="key">the key</param>
    /// <param name="value">the versioned value</param>
    void Append(K key, VersionedValue<object> value);

    /// <summary>
    /// The current root reference, boxed.
    /// </summary>
    /// <returns>the root</returns>
    object GetRoot();

    /// <summary>
    /// The current root reference, flushing pending appends first.
    /// </summary>
    /// <returns>the root</returns>
    object FlushAndGetRoot();

    /// <summary>
    /// A cursor over the given root.
    /// </summary>
    /// <param name="root">the root reference</param>
    /// <param name="from">the lower bound, or null</param>
    /// <param name="to">the upper bound, or null</param>
    /// <param name="reverse">whether to walk backwards</param>
    /// <returns>the cursor</returns>
    ITransactionCursor<K> Cursor(object root, object? from, object? to, bool reverse);
}
