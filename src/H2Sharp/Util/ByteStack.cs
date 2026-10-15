/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.util.ByteStack.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: poll(int) and size() are not ported — nothing reads them, and a method
 * nobody calls is a promise nobody checks. NoSuchElementException on an empty pop
 * becomes InvalidOperationException, which is what System.Collections.Generic.Stack
 * throws there: the same refusal in the platform's own spelling.
 */
using System;

namespace H2Sharp.Util;

/// <summary>
/// The stack of byte values. This class is not synchronized and should not be
/// used by multiple threads concurrently.
/// </summary>
public sealed class ByteStack
{
    private static readonly int MaxArraySize = int.MaxValue - 8;

    private int size;

    private byte[] array;

    /// <summary>
    /// Creates a new empty instance.
    /// </summary>
    public ByteStack()
    {
        array = [];
    }

    /// <summary>
    /// Pushes an item onto the top of this stack.
    /// </summary>
    /// <param name="item">the item to push</param>
    public void Push(byte item)
    {
        int index = size;
        int oldLength = array.Length;
        if (index >= oldLength)
        {
            Grow(oldLength);
        }

        array[index] = item;
        size = index + 1;
    }

    /// <summary>
    /// Removes the item at the top of this stack and returns that item.
    /// </summary>
    /// <returns>the item at the top of this stack</returns>
    /// <exception cref="InvalidOperationException">if the stack is empty</exception>
    public byte Pop()
    {
        int index = size - 1;
        if (index < 0)
        {
            throw new InvalidOperationException("Stack empty.");
        }

        size = index;
        return array[index];
    }

    /// <summary>
    /// Looks at the item at the top of this stack without removing it.
    /// </summary>
    /// <param name="defaultValue">value to return if the stack is empty</param>
    /// <returns>the item at the top of this stack, or the default value</returns>
    public int Peek(int defaultValue)
    {
        int index = size - 1;
        if (index < 0)
        {
            return defaultValue;
        }

        return array[index];
    }

    /// <summary>
    /// Returns true if this stack is empty.
    /// </summary>
    /// <returns>true if this stack is empty</returns>
    public bool IsEmpty() => size == 0;

    private void Grow(int length)
    {
        if (length == 0)
        {
            length = 0x10;
        }
        else if (length >= MaxArraySize)
        {
            throw new OutOfMemoryException();
        }
        else if ((length <<= 1) < 0)
        {
            length = MaxArraySize;
        }

        Array.Resize(ref array, length);
    }
}
