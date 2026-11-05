/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */

using System;
using System.Collections.Generic;
using H2Sharp.MvStore.Type;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// The typed data plane: a map whose key type is <typeparamref name="K"/> for real, so no
/// key crossing the transaction boundary is boxed. This is the reference's
/// <c>MVMap&lt;K, VersionedValue&lt;V&gt;&gt;</c> shape; the values stay erased, which is
/// how the undo log sees them anyway.
/// </summary>
/// <typeparam name="K">the key type</typeparam>
internal sealed class TypedMapData<K> : ITransactionMapData<K>
{
    private readonly MVMap<K, VersionedValue<object>> map;

    internal TypedMapData(MVMap<K, VersionedValue<object>> map)
    {
        this.map = map;
    }

    /// <inheritdoc/>
    public int GetId() => map.GetId();

    /// <inheritdoc/>
    public string? GetName() => map.GetName();

    /// <inheritdoc/>
    public IAnyMap InnerMap => map;

    /// <inheritdoc/>
    public bool IsClosed() => map.IsClosed();

    /// <inheritdoc/>
    public void Clear() => map.Clear();

    /// <inheritdoc/>
    public void SetVolatile(bool isVolatile) => map.SetVolatile(isVolatile);

    /// <inheritdoc/>
    public long SizeAsLong() => map.SizeAsLong();

    /// <inheritdoc/>
    public long SizeAsLongMax() => map.SizeAsLong();

    /// <inheritdoc/>
    public long GetDiskSpaceUsed(bool approximate) => map.GetRootPage().GetDiskSpaceUsed(approximate);

    /// <inheritdoc/>
    public object? LastKey() => map.LastKey();

    /// <inheritdoc/>
    public object GetKeyType() => map.GetKeyType();

    /// <inheritdoc/>
    public int CompareKeys(K a, K? b) => map.GetKeyType().Compare(a, b);

    /// <inheritdoc/>
    public DataType<VersionedValue<object>> GetValueType() => map.GetValueType();

    /// <inheritdoc/>
    public VersionedValue<object>? Get(K key) => map.Get(key);

    /// <inheritdoc/>
    public VersionedValue<object>? Get(object root, K key)
        => map.Get(((RootReference<K, VersionedValue<object>>)root).root, key);

    /// <inheritdoc/>
    public VersionedValue<object>? Put(K key, VersionedValue<object> value) => map.Put(key, value);

    /// <inheritdoc/>
    public VersionedValue<object>? Operate(
        K key, VersionedValue<object>? value, DecisionMaker<VersionedValue<object>> decisionMaker)
        => map.Operate(key, value, decisionMaker);

    /// <inheritdoc/>
    public void Append(K key, VersionedValue<object> value) => map.Append(key, value);

    /// <inheritdoc/>
    public object GetRoot() => map.GetRoot();

    /// <inheritdoc/>
    public long GetTotalCount(object root)
        => ((RootReference<K, VersionedValue<object>>)root).GetTotalCount();

    /// <inheritdoc/>
    public object FlushAndGetRoot() => map.FlushAndGetRoot();

    /// <inheritdoc/>
    public ITransactionCursor<K> Cursor(object root, object? from, object? to, bool reverse)
        => (ITransactionCursor<K>)(object)map.Cursor(
            ((RootReference<K, VersionedValue<object>>)root), from, to, reverse);
}
