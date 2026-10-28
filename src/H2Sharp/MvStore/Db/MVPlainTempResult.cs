/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.mvstore.db.MVPlainTempResult.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A plain temporary result: rows keep insertion order.
 */
using System.Collections.Generic;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.MvStore.Tx;
using H2Sharp.MvStore.Type;
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.MvStore.Db;

/// <summary>
/// Plain temporary result.
/// </summary>
internal sealed class MVPlainTempResult : MVTempResult
{
    /// <summary>
    /// Map with identities of rows as keys and rows as values.
    /// </summary>
    private readonly MVMap<object, object> map;

    /// <summary>
    /// Counter for row identities. It is separate from <see cref="MVTempResult.rowCount"/>
    /// because <see cref="RemoveRow"/> is not supported and every row needs its own identity.
    /// </summary>
    private long counter;

    /// <summary>
    /// Cursor for <see cref="Next"/>.
    /// </summary>
    private Cursor<object, object>? cursor;

    private MVPlainTempResult(MVPlainTempResult parent)
        : base(parent)
    {
        map = parent.map;
    }

    /// <summary>
    /// Creates a new plain temporary result.
    /// </summary>
    /// <param name="database">the database</param>
    /// <param name="expressions">the column expressions</param>
    /// <param name="visibleColumnCount">count of visible columns</param>
    /// <param name="resultColumnCount">the number of columns including virtual columns</param>
    public MVPlainTempResult(
        Database database,
        Expression[] expressions,
        int visibleColumnCount,
        int resultColumnCount)
        : base(database, expressions, visibleColumnCount, resultColumnCount)
    {
        ValueDataType valueType = new(
            database, database.GetCompareMode(), null, new int[resultColumnCount]);
        // The reference declares the map as MVMap<Long, ValueRow>. C# generics are invariant,
        // so the object-keyed map adapter carries the same two data types.
        MVMap<object, object>.Builder<object, object> builder = new MVMap<object, object>.Builder<object, object>()
            .KeyType(new ObjectMapDataType<long>(LongDataType.Instance))
            .ValueType(new ObjectMapDataType<Value>(valueType))
            .SingleWriter();
        map = store.OpenMap("tmp", builder);
    }

    /// <inheritdoc/>
    public override int AddRow(Value?[] values)
    {
        map.Append(counter++, ValueRow.Get(values));
        return ++rowCount;
    }

    /// <inheritdoc/>
    public override bool Contains(Value?[] values)
        => throw DbException.GetUnsupportedException("contains()");

    /// <inheritdoc/>
    public override ResultExternal? CreateShallowCopy()
    {
        lock (this)
        {
            if (parent is not null)
            {
                return parent.CreateShallowCopy();
            }

            if (closed)
            {
                return null;
            }

            childCount++;
            return new MVPlainTempResult(this);
        }
    }

    /// <inheritdoc/>
    public override Value?[] Next()
    {
        cursor ??= map.Cursor(null);
        if (!cursor.HasNext())
        {
            return null!;
        }

        _ = cursor.Next();
        return ((ValueRow)cursor.GetValue()).GetList();
    }

    /// <inheritdoc/>
    public override int RemoveRow(Value?[] values)
        => throw DbException.GetUnsupportedException("removeRow()");

    /// <inheritdoc/>
    public override void Reset() => cursor = null;
}
