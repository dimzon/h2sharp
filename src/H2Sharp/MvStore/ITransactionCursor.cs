/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */

using H2Sharp.Values;

namespace H2Sharp.MvStore;

/// <summary>
/// A cursor over a versioned map, with the key typed but the value erased. Java gets the
/// typed walk from erasure; this is the same seam for a cursor over
/// <c>MVMap&lt;K, VersionedValue&lt;object&gt;&gt;</c> and over an object-keyed map.
/// </summary>
/// <typeparam name="K">the key type</typeparam>
public interface ITransactionCursor<K>
{
    /// <summary>
    /// Whether there is another key.
    /// </summary>
    /// <returns>true when there is</returns>
    bool HasNext();

    /// <summary>
    /// Advances and returns the next key.
    /// </summary>
    /// <returns>the key</returns>
    K Next();

    /// <summary>
    /// The last key seen.
    /// </summary>
    /// <returns>the key</returns>
    K GetKey();

    /// <summary>
    /// The value of the last key seen.
    /// </summary>
    /// <returns>the value</returns>
    VersionedValue<object> GetValue();
}

/// <summary>
/// An object-keyed cursor seen with typed keys: every key the underlying
/// <c>Cursor&lt;object, …&gt;</c> answers is cast to <typeparamref name="K"/>.
/// </summary>
/// <typeparam name="K">the key type</typeparam>
internal sealed class ObjectTransactionCursor<K> : ITransactionCursor<K>
{
    private readonly Cursor<object, VersionedValue<object>> cursor;

    internal ObjectTransactionCursor(Cursor<object, VersionedValue<object>> cursor)
    {
        this.cursor = cursor;
    }

    /// <inheritdoc/>
    public bool HasNext() => cursor.HasNext();

    /// <inheritdoc/>
    public K Next() => (K)cursor.Next();

    /// <inheritdoc/>
    public K GetKey() => (K)cursor.GetKey();

    /// <inheritdoc/>
    public VersionedValue<object> GetValue() => cursor.GetValue();
}
