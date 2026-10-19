/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mvstore.db.NullValueDataType.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The value type of maps that only need their keys.
 *
 * A secondary index stores the indexed values in the key and has nothing to put in the value:
 * the row itself lives in the primary index, and the key already carries the row id that finds
 * it. This type writes zero bytes for the value and reads SQL NULL back, so the map costs
 * nothing for the half it does not use.
 */
using System;
using H2Sharp.MvStore.Type;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// Dummy data type used when no value is required.
/// </summary>
public sealed class NullValueDataType : BasicDataType<Value>
{
    /// <summary>
    /// Dummy data type instance.
    /// </summary>
    public static readonly NullValueDataType Instance = new();

    private NullValueDataType()
    {
    }

    /// <inheritdoc/>
    public override int Compare(Value? a, Value? b) => 0;

    /// <inheritdoc/>
    public override int BinarySearch(Value key, Value[] storage, int size, int initialGuess) => 0;

    /// <inheritdoc/>
    public override int GetMemory(Value obj) => 0;

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, Value obj)
    {
    }

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, Value[] storage, int len)
    {
    }

    /// <inheritdoc/>
    public override Value Read(Span<byte> buff, ref int position) => ValueNull.Instance;

    /// <inheritdoc/>
    public override void Read(Span<byte> buff, Value[] storage, int len, ref int position)
    {
        Array.Fill(storage, ValueNull.Instance, 0, len);
    }

    /// <inheritdoc/>
    public override Value[] CreateStorage(int size) => new Value[size];
}
