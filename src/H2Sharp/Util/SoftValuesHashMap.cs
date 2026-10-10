using System;
using System.Collections;
using System.Collections.Generic;

namespace H2Sharp.Util;

/// <summary>
/// A map whose values may be collected when memory runs short, and whose keys go with them.
/// </summary>
/// <remarks>
/// The point is the removal policy: when a value is collected its key has to go too, and a key left
/// behind would keep an entry that can never be answered.
/// <para>
/// Java reaches that with a <c>ReferenceQueue</c> and a <c>SoftReference</c> whose finalizer reports
/// back. .NET has no equivalent of a soft reference that is reported on collection, and
/// <c>WeakReference{T}</c> has no finalizer either, so a key of a collected value cannot be found
/// here. What is kept instead is the policy that is observable: the map never keeps a key without a
/// value, and it does not hold a strong reference to what it holds.
/// </para>
/// <para>
/// The cost is stated rather than hidden: a value collected behind the map's back leaves its key,
/// and the map will report the key as present with a default value. That is a leak of keys, not of
/// values, and it is the shape of the trade this port made instead of inventing a finalizer that
/// would not survive collection.
/// </para>
/// </remarks>
/// <typeparam name="TKey">the key type</typeparam>
/// <typeparam name="TValue">the value type</typeparam>
public class SoftValuesHashMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : notnull
    where TValue : class
{
    private readonly Dictionary<TKey, WeakReference<TValue>> map = [];

    /// <inheritdoc/>
    /// <remarks>
    /// Enumerating is refused, as in the reference: a value may go between the moment it is handed
    /// out and the moment it is read, so an enumeration would have to answer for entries that no longer
    /// hold anything.
    /// </remarks>
    /// <exception cref="NotSupportedException">always</exception>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        => throw new NotSupportedException();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// The value for a key, if it has not been collected.
    /// </summary>
    /// <param name="key">the key</param>
    /// <returns>the value, or the default</returns>
    public TValue? Get(TKey key)
        => map.TryGetValue(key, out WeakReference<TValue>? reference)
            && reference.TryGetTarget(out TValue? value)
                ? value
                : default;

    /// <summary>
    /// Stores a value, and what was there before it.
    /// </summary>
    /// <param name="key">the key</param>
    /// <param name="value">the value</param>
    /// <returns>the value that was there, or the default</returns>
    public TValue? Put(TKey key, TValue value)
    {
        TValue? previous = Get(key);
        map[key] = new WeakReference<TValue>(value);
        return previous;
    }

    /// <summary>
    /// Drops a key.
    /// </summary>
    /// <param name="key">the key</param>
    /// <returns>the value that was there, or the default</returns>
    public TValue? Remove(TKey key)
    {
        TValue? previous = Get(key);
        _ = map.Remove(key);
        return previous;
    }

    /// <summary>
    /// Whether a key is here and its value is still alive.
    /// </summary>
    /// <param name="key">the key</param>
    /// <returns>true when there is an answer</returns>
    public bool ContainsKey(TKey key)
        => map.TryGetValue(key, out WeakReference<TValue>? reference) && reference.TryGetTarget(out _);

    /// <summary>
    /// How many keys are here, whether or not their values survived.
    /// </summary>
    public int Count => map.Count;

    /// <summary>
    /// Forgets everything.
    /// </summary>
    public void Clear() => map.Clear();
}
