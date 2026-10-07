/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 2.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;

namespace H2Sharp.Util;

/// <summary>
/// A string keyed map that iterates in the order the keys were first added.
/// </summary>
/// <typeparam name="TValue">the value type</typeparam>
/// <remarks>
/// <para>
/// This is the port of <c>java.util.LinkedHashMap&lt;String, X&gt;</c> in insertion order,
/// which is what the reference uses for a row's field list and what the rest of the value
/// layer will keep using for anything that has to remember insertion order.
/// </para>
/// <para>
/// It is a thin typed skin over <see cref="OrderedDictionary"/> from
/// <c>System.Collections.Specialized</c>, which is in the framework on both targets, rather
/// than a hand written structure. Two things decided it.
/// <c>OrderedDictionary&lt;TKey, TValue&gt;</c> exists only from .NET 9, and this project
/// also builds for .NET 8, so using it would mean one implementation per target. And a
/// package would be a dependency in a core that currently has none, for a structure the
/// framework already ships. What is left is non generic underneath, so this type exists to
/// keep the <c>object</c> casts in one place instead of spreading them over the callers.
/// </para>
/// <para>
/// Only what the reference uses is offered: adding, reading and iterating. There is no
/// removal, because nothing needs it and each operation added is one more thing that has to
/// behave.
/// </para>
/// </remarks>
public sealed class OrderedStringMap<TValue>
    where TValue : notnull
{
    private readonly OrderedDictionary map;

    /// <summary>
    /// Creates an empty map.
    /// </summary>
    public OrderedStringMap()
        : this(0)
    {
    }

    /// <summary>
    /// Creates an empty map with room for the given number of entries.
    /// </summary>
    /// <param name="capacity">the expected number of entries</param>
    public OrderedStringMap(int capacity)
    {
        map = new OrderedDictionary(capacity < 0 ? 0 : capacity, StringComparer.Ordinal);
    }

    /// <summary>
    /// The number of entries.
    /// </summary>
    public int Count => map.Count;

    /// <summary>
    /// Adds an entry, or updates the value of an existing key.
    /// </summary>
    /// <remarks>
    /// An update keeps the position the key was first added at, which is what the reference
    /// gets from <c>LinkedHashMap</c> in insertion order. It is easy to get wrong with a
    /// plain list and a dictionary side by side, and it is observable in a row's field list
    /// and in its rendered SQL.
    /// </remarks>
    /// <param name="key">the key</param>
    /// <param name="value">the value</param>
    public void Put(string key, TValue value) => map[key] = value;

    /// <summary>
    /// Returns whether there is an entry with the given key.
    /// </summary>
    /// <param name="key">the key</param>
    /// <returns>whether the key is present</returns>
    public bool ContainsKey(string key) => map.Contains(key);

    /// <summary>
    /// Returns the value for the given key.
    /// </summary>
    /// <param name="key">the key</param>
    /// <param name="value">the value, if the key is present</param>
    /// <returns>whether the key was present</returns>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out TValue value)
    {
        if (map.Contains(key))
        {
            value = (TValue)map[key]!;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// The entries, in the order the keys were first added.
    /// </summary>
    public IEnumerable<KeyValuePair<string, TValue>> Entries
    {
        get
        {
            // The underlying enumerator walks an array of entries in order, so this allocates
            // one enumerator and yields lazily; it is the iteration order that is the point,
            // not the allocation.
            IDictionaryEnumerator enumerator = map.GetEnumerator();
            while (enumerator.MoveNext())
            {
                yield return new KeyValuePair<string, TValue>(
                    (string)enumerator.Key, (TValue)enumerator.Value!);
            }
        }
    }

    /// <summary>
    /// The keys, in the order they were first added.
    /// </summary>
    public IEnumerable<string> Keys
    {
        get
        {
            foreach (KeyValuePair<string, TValue> entry in Entries)
            {
                yield return entry.Key;
            }
        }
    }
}
