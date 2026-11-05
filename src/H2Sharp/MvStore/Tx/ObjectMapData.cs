/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using H2Sharp.MvStore.Type;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// The object-keyed data plane: the map keeps <see cref="object"/> keys, and every key
/// crossing the boundary is cast or boxed. Used for the maps that are not typed by key
/// (the R-tree stays <c>MVMap&lt;object, …&gt;</c>, as do the system maps) and for maps
/// opened without a key type.
/// </summary>
/// <typeparam name="K">the key type the transaction map answers with</typeparam>
internal sealed class ObjectMapData<K> : ITransactionMapData<K>
{
    private readonly MVMap<object, VersionedValue<object>> map;

    internal ObjectMapData(MVMap<object, VersionedValue<object>> map)
    {
        this.map = map;
    }

    // Java's Long.valueOf interns -128..127, so the reference's Long-keyed maps reuse those
    // boxes. The same cache applies here while a long-keyed map runs on this data plane.
    private static readonly object[] SmallKeys = BuildSmallKeys();

    private static object[] BuildSmallKeys()
    {
        object[] keys = new object[128];
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = (long)i;
        }

        return keys;
    }

    private static object BoxKey(K key)
    {
        if (typeof(K) == typeof(long))
        {
            long value = Unsafe.As<K, long>(ref key);
            if ((ulong)value < (ulong)SmallKeys.Length)
            {
                return SmallKeys[(int)value];
            }

            return value;
        }

        return key!;
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
    public int CompareKeys(K a, K? b) => map.GetKeyType().Compare((object?)a, (object?)b);

    /// <inheritdoc/>
    public DataType<VersionedValue<object>> GetValueType() => map.GetValueType();

    /// <inheritdoc/>
    public VersionedValue<object>? Get(K key) => map.Get(BoxKey(key));

    /// <inheritdoc/>
    public VersionedValue<object>? Get(object root, K key)
        => map.Get(((RootReference<object, VersionedValue<object>>)root).root, BoxKey(key));

    /// <inheritdoc/>
    public VersionedValue<object>? Put(K key, VersionedValue<object> value) => map.Put(BoxKey(key), value);

    /// <inheritdoc/>
    public VersionedValue<object>? Operate(
        K key, VersionedValue<object>? value, DecisionMaker<VersionedValue<object>> decisionMaker)
        => map.Operate(BoxKey(key), value, decisionMaker);

    /// <inheritdoc/>
    public void Append(K key, VersionedValue<object> value) => map.Append(BoxKey(key), value);

    /// <inheritdoc/>
    public object GetRoot() => map.GetRoot();

    /// <inheritdoc/>
    public long GetTotalCount(object root)
        => ((RootReference<object, VersionedValue<object>>)root).GetTotalCount();

    /// <inheritdoc/>
    public object FlushAndGetRoot() => map.FlushAndGetRoot();

    /// <inheritdoc/>
    public ITransactionCursor<K> Cursor(object root, object? from, object? to, bool reverse)
        => new ObjectTransactionCursor<K>(map.Cursor(
            ((RootReference<object, VersionedValue<object>>)root), from, to, reverse));
}
