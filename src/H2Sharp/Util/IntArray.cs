/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.util.IntArray.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Modified: IndexOutOfRangeException replaces ArrayIndexOutOfBoundsException; overflow
 * checks on HashCode arithmetic are disabled project-wide (see Directory.Build.props).
 */
using System;
using System.Text;

namespace H2Sharp.Util;

/// <summary>
/// An array with integer element.
/// </summary>
public class IntArray
{
    private int[] data;
    private int size;
    private int hash;

    /// <summary>
    /// Create an int array with the default initial capacity.
    /// </summary>
    public IntArray() : this(10)
    {
    }

    /// <summary>
    /// Create an int array with specified initial capacity.
    /// </summary>
    /// <param name="capacity">the initial capacity</param>
    public IntArray(int capacity)
    {
        data = new int[capacity];
    }

    /// <summary>
    /// Create an int array with the given values and size.
    /// </summary>
    /// <param name="data">the int array</param>
    public IntArray(int[] data)
    {
        this.data = data;
        size = data.Length;
    }

    /// <summary>
    /// Append a value.
    /// </summary>
    /// <param name="value">the value to append</param>
    public void Add(int value)
    {
        if (size >= data.Length)
        {
            EnsureCapacity(size + size);
        }

        data[size++] = value;
    }

    /// <summary>
    /// Get the value at the given index.
    /// </summary>
    /// <param name="index">the index</param>
    /// <returns>the value</returns>
    public int Get(int index)
    {
        if (index >= size)
        {
            throw new IndexOutOfRangeException("i=" + index + " size=" + size);
        }

        return data[index];
    }

    /// <summary>
    /// Remove the value at the given index.
    /// </summary>
    /// <param name="index">the index</param>
    public void Remove(int index)
    {
        if (index >= size)
        {
            throw new IndexOutOfRangeException("i=" + index + " size=" + size);
        }

        Array.Copy(data, index + 1, data, index, size - index - 1);
        size--;
    }

    /// <summary>
    /// Ensure the underlying array is large enough for the given number of entries.
    /// </summary>
    /// <param name="minCapacity">the minimum capacity</param>
    public void EnsureCapacity(int minCapacity)
    {
        minCapacity = Math.Max(4, minCapacity);
        if (minCapacity >= data.Length)
        {
            Array.Resize(ref data, minCapacity);
        }
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is not IntArray other)
        {
            return false;
        }

        if (GetHashCode() != other.GetHashCode() || size != other.size)
        {
            return false;
        }

        for (int i = 0; i < size; i++)
        {
            if (data[i] != other.data[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (hash != 0)
        {
            return hash;
        }

        int h = size + 1;
        for (int i = 0; i < size; i++)
        {
            h = h * 31 + data[i];
        }

        hash = h;
        return h;
    }

    /// <summary>
    /// Get the size of the list.
    /// </summary>
    /// <returns>the size</returns>
    public int Size() => size;

    /// <summary>
    /// Convert this list to an array. The target array must be big enough.
    /// </summary>
    /// <param name="array">the target array</param>
    public void ToArray(int[] array) => Array.Copy(data, 0, array, 0, size);

    /// <inheritdoc/>
    public override string ToString()
    {
        StringBuilder builder = new("{");
        for (int i = 0; i < size; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(data[i]);
        }

        return builder.Append('}').ToString();
    }

    /// <summary>
    /// Remove a number of elements.
    /// </summary>
    /// <param name="fromIndex">the index of the first item to remove</param>
    /// <param name="toIndex">upper bound (exclusive)</param>
    public void RemoveRange(int fromIndex, int toIndex)
    {
        if (fromIndex > toIndex || toIndex > size)
        {
            throw new IndexOutOfRangeException("from=" + fromIndex + " to=" + toIndex + " size=" + size);
        }

        Array.Copy(data, toIndex, data, fromIndex, size - toIndex);
        size -= toIndex - fromIndex;
    }
}
