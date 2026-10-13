/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.util.SmallLRUCache.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the reference is a LinkedHashMap with access order, which evicts the least recently
 * used entry by overriding one method. .NET has no map with an order, so this is the two structures
 * that answer the question: a dictionary for the lookup and a linked list for the order. Reading and
 * writing move the entry to the front of the list, and the entry at the back goes when there is one
 * too many.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace H2Sharp.Util;

/// <summary>
/// A cache that keeps a fixed number of entries and drops the one that was used longest ago.
/// </summary>
/// <typeparam name="TKey">what the entries are keyed by</typeparam>
/// <typeparam name="TValue">what they hold</typeparam>
public sealed class SmallLRUCache<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> entries;

    private readonly LinkedList<KeyValuePair<TKey, TValue>> order = new();

    private int size;

    private SmallLRUCache(int size)
    {
        this.size = size;
        entries = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(size);
    }

    /// <summary>
    /// A cache that holds so many entries.
    /// </summary>
    /// <typeparam name="TKey">what the entries are keyed by</typeparam>
    /// <typeparam name="TValue">what they hold</typeparam>
    /// <param name="size">how many</param>
    /// <returns>the cache</returns>
    public static SmallLRUCache<TKey, TValue> NewInstance(int size) => new(size);

    /// <summary>
    /// How many entries the cache holds.
    /// </summary>
    public int Count => entries.Count;

    /// <summary>
    /// Change how many entries the cache holds, dropping what no longer fits.
    /// </summary>
    /// <param name="newSize">how many</param>
    public void SetMaxSize(int newSize)
    {
        size = newSize;
        Trim();
    }

    /// <summary>
    /// The value for a key, and the entry counts as used.
    /// </summary>
    /// <param name="key">the key</param>
    /// <param name="value">the value that was there, or what was given</param>
    /// <returns>true if the key was there</returns>
    public bool TryGetValue(TKey key, out TValue value)
    {
        if (entries.TryGetValue(key, out LinkedListNode<KeyValuePair<TKey, TValue>>? node))
        {
            value = node.Value.Value;

            // Used most recently: to the front of the order.
            order.Remove(node);
            order.AddFirst(node);
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Put an entry in, and drop the least recently used one if there is no room.
    /// </summary>
    /// <param name="key">the key</param>
    /// <param name="value">the value</param>
    /// <returns>the value that was there, or what was given</returns>
    public TValue Put(TKey key, TValue value)
    {
        // One lookup: the slot is written before anything resizes the dictionary, so the
        // reference stays valid. The order list is touched in between, but it is not the
        // dictionary.
        ref LinkedListNode<KeyValuePair<TKey, TValue>>? slot =
            ref CollectionsMarshal.GetValueRefOrAddDefault(entries, key, out bool exists);
        TValue previous = default!;
        if (exists)
        {
            LinkedListNode<KeyValuePair<TKey, TValue>> node = slot!;
            previous = node.Value.Value;
            order.Remove(node);
        }

        slot = order.AddFirst(new KeyValuePair<TKey, TValue>(key, value));
        Trim();
        return previous;
    }

    /// <summary>
    /// Take an entry out.
    /// </summary>
    /// <param name="key">the key</param>
    /// <returns>the value, or what an absent key gives</returns>
    public TValue Remove(TKey key)
    {
        if (!entries.Remove(key, out LinkedListNode<KeyValuePair<TKey, TValue>>? node))
        {
            return default!;
        }

        order.Remove(node);
        return node.Value.Value;
    }

    /// <summary>
    /// Forget everything.
    /// </summary>
    public void Clear()
    {
        entries.Clear();
        order.Clear();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// From the least recently used to the most, which is the order they would be dropped in. The
    /// reference iterates its map in access order, which walks the other way round.
    /// </remarks>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        LinkedListNode<KeyValuePair<TKey, TValue>>? node = order.Last;
        while (node is not null)
        {
            yield return node.Value;
            node = node.Previous;
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Trim()
    {
        while (entries.Count > size)
        {
            LinkedListNode<KeyValuePair<TKey, TValue>>? last = order.Last;
            if (last is null)
            {
                return;
            }
            order.RemoveLast();
            entries.Remove(last.Value.Key);
        }
    }
}
