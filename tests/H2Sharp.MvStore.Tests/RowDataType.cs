/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.test.store.RowDataType.
 *
 * A row type: each column has its own data type, and values travel column by column. The column
 * types arrive as an array, which C# generics cannot spell heterogeneously the way Java's raw
 * array can; a string column therefore arrives wrapped so that it still writes the string bytes.
 */

using System;
using H2Sharp.MvStore;
using H2Sharp.MvStore.Type;

namespace H2Sharp.MvStore.Tests;

/// <summary>
/// A row type.
/// </summary>
public sealed class RowDataType : BasicDataType<object?[]>
{
    private readonly DataType<object?>[] types;

    internal RowDataType(DataType<object?>[] types) => this.types = types;

    /// <inheritdoc/>
    public override object?[][] CreateStorage(int size) => new object?[size][];

    /// <inheritdoc/>
    public override int Compare(object?[]? a, object?[]? b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        int al = a!.Length;
        int bl = b!.Length;
        int len = Math.Min(al, bl);
        for (int i = 0; i < len; i++)
        {
            int comp = types[i].Compare(a[i], b[i]);
            if (comp != 0)
            {
                return comp;
            }
        }

        if (len < al)
        {
            return -1;
        }
        else if (len < bl)
        {
            return 1;
        }

        return 0;
    }

    /// <inheritdoc/>
    public override int GetMemory(object?[] x)
    {
        int len = x.Length;
        int memory = 0;
        for (int i = 0; i < len; i++)
        {
            memory += types[i].GetMemory(x[i]);
        }

        return memory;
    }

    /// <inheritdoc/>
    public override object?[] Read(Span<byte> buff, ref int position)
    {
        int len = DataUtils.ReadVarInt(buff, ref position);
        object?[] x = new object?[len];
        for (int i = 0; i < len; i++)
        {
            x[i] = types[i].Read(buff, ref position);
        }

        return x;
    }

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, object?[] x)
    {
        int len = x.Length;
        _ = buff.PutVarInt(len);
        for (int i = 0; i < len; i++)
        {
            types[i].Write(buff, x[i]);
        }
    }
}

/// <summary>
/// A string column for a row type: the row holds data types of one kind, and a string column
/// would not fit without this wrapper, which writes the string bytes.
/// </summary>
internal sealed class StringAsObjectType : BasicDataType<object?>
{
    internal static readonly StringAsObjectType Instance = new();

    private StringAsObjectType()
    {
    }

    /// <inheritdoc/>
    public override object?[] CreateStorage(int size) => new object?[size];

    /// <inheritdoc/>
    public override int GetMemory(object? obj) => StringDataType.Instance.GetMemory((string)obj!);

    /// <inheritdoc/>
    public override int Compare(object? a, object? b)
        => StringDataType.Instance.Compare((string?)a, (string?)b);

    /// <inheritdoc/>
    public override void Write(WriteBuffer buff, object? obj)
        => StringDataType.Instance.Write(buff, (string)obj!);

    /// <inheritdoc/>
    public override object? Read(Span<byte> buff, ref int position)
        => StringDataType.Instance.Read(buff, ref position);
}
