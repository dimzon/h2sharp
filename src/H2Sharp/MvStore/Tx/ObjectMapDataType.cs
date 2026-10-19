/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Not in the reference: the transaction store here merges the reference's Store into one
 * object-keyed map class, so a typed data type cannot be installed as-is. This adapter is
 * the missing bridge; it is the only reason the class exists.
 */
using System;
using System.Collections.Generic;
using H2Sharp.MvStore.Type;

namespace H2Sharp.MvStore.Tx;

/// <summary>
/// Presents a typed data type as the object data type the transaction maps are declared with.
/// </summary>
/// <remarks>
/// The reference keeps every transactional map typed (<c>MVMap&lt;K, VersionedValue&lt;V&gt;&gt;</c>),
/// so a secondary index can install its <see cref="Db.RowDataType"/> and have keys ordered by
/// the indexed columns. This port stores all transactional maps in a single object-keyed map
/// class (see <see cref="TransactionStore"/>), and <see cref="Type.ObjectDataType"/> would
/// compare a row key by serializing it, which a row does not support. The adapter forwards to
/// the real type after the cast the erased Java type would have made. Comparison and the
/// binary search are the parts that run for in-memory maps; the read and write methods forward
/// as well so a future persistent store is not silently wrong.
/// </remarks>
/// <typeparam name="T">the key or value type</typeparam>
internal sealed class ObjectMapDataType<T> : DataType<object>
{
    private readonly DataType<T> type;

    internal ObjectMapDataType(DataType<T> type)
    {
        this.type = type;
    }

    /// <inheritdoc/>
    public int Compare(object? a, object? b) => type.Compare((T)a!, (T)b!);

    /// <inheritdoc/>
    public bool IsMemoryEstimationAllowed() => type.IsMemoryEstimationAllowed();

    /// <inheritdoc/>
    public int GetMemory(object? obj) => type.GetMemory((T)obj!);

    /// <inheritdoc/>
    public void Write(WriteBuffer buff, object? obj) => type.Write(buff, (T)obj!);

    /// <inheritdoc/>
    public void Write(WriteBuffer buff, object?[] storage, int len)
    {
        for (int i = 0; i < len; i++)
        {
            type.Write(buff, (T)storage[i]!);
        }
    }

    /// <inheritdoc/>
    public object Read(Span<byte> buff, ref int position) => type.Read(buff, ref position)!;

    /// <inheritdoc/>
    public void Read(Span<byte> buff, object?[] storage, int len, ref int position)
    {
        for (int i = 0; i < len; i++)
        {
            storage[i] = type.Read(buff, ref position);
        }
    }

    /// <inheritdoc/>
    public object[] CreateStorage(int size) => new object[size];

    /// <inheritdoc/>
    /// <remarks>
    /// The same search as <see cref="BasicDataType{T}.BinarySearch"/>, over the object storage
    /// the transaction maps actually allocate.
    /// </remarks>
    public int BinarySearch(object? key, object?[] storage, int size, int initialGuess)
    {
        int low = 0;
        int high = size - 1;

        int x = initialGuess - 1;
        if (x < 0 || x > high)
        {
            x = high >> 1;
        }

        while (low <= high)
        {
            int compare = Compare(key, storage[x]);
            if (compare > 0)
            {
                low = x + 1;
            }
            else if (compare < 0)
            {
                high = x - 1;
            }
            else
            {
                return x;
            }

            x = (low + high) >> 1;
        }

        return ~low;
    }
}
