/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new tests for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Linq;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class IntArrayTests
{
    [Fact]
    public void DefaultConstructor_HasDefaultCapacityAndZeroSize()
    {
        IntArray a = new();
        Assert.Equal(0, a.Size());
    }

    [Fact]
    public void Add_AppendsAndGrows()
    {
        IntArray a = new();
        for (int i = 0; i < 100; i++)
        {
            a.Add(i * i);
        }

        Assert.Equal(100, a.Size());
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(i * i, a.Get(i));
        }
    }

    [Fact]
    public void Constructor_WithValues_SetsSizeToLength()
    {
        IntArray a = new([1, 2, 3]);
        Assert.Equal(3, a.Size());
        Assert.Equal(2, a.Get(1));
    }

    [Fact]
    public void Get_ThrowsWhenIndexOutOfRange()
    {
        IntArray a = new([1, 2, 3]);
        IndexOutOfRangeException e = Assert.Throws<IndexOutOfRangeException>(() => a.Get(3));
        Assert.Contains("i=3 size=3", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_ShiftsElementsLeft()
    {
        IntArray a = new([1, 2, 3, 4]);
        a.Remove(1);
        Assert.Equal(3, a.Size());
        Assert.Equal(1, a.Get(0));
        Assert.Equal(3, a.Get(1));
        Assert.Equal(4, a.Get(2));
    }

    [Fact]
    public void Remove_ThrowsWhenIndexOutOfRange()
    {
        IntArray a = new([1, 2, 3]);
        Assert.Throws<IndexOutOfRangeException>(() => a.Remove(3));
    }

    [Fact]
    public void RemoveRange_RemovesSlice()
    {
        IntArray a = new([0, 1, 2, 3, 4, 5]);
        a.RemoveRange(1, 4);
        Assert.Equal([0, 4, 5], Enumerable.Range(0, a.Size()).Select(a.Get));
    }

    [Fact]
    public void RemoveRange_ThrowsOnInvalidBounds()
    {
        IntArray a = new([0, 1, 2]);
        Assert.Throws<IndexOutOfRangeException>(() => a.RemoveRange(2, 1));
        Assert.Throws<IndexOutOfRangeException>(() => a.RemoveRange(0, 4));
    }

    [Fact]
    public void EnsureCapacity_EnforcesMinimumOfFour()
    {
        IntArray a = new();
        a.EnsureCapacity(1);
        a.Add(42);
        Assert.Equal(42, a.Get(0));
    }

    [Fact]
    public void ToArray_CopiesValues()
    {
        IntArray a = new([7, 8, 9]);
        int[] target = new int[3];
        a.ToArray(target);
        Assert.Equal([7, 8, 9], target);
    }

    [Fact]
    public void Equality_IsByContent()
    {
        IntArray a = new([1, 2, 3]);
        IntArray b = new([1, 2, 3]);
        IntArray c = new([1, 2, 4]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
        Assert.False(a.Equals("not an IntArray"));
    }

    [Fact]
    public void HashCode_IsStableAcrossCalls()
    {
        IntArray a = new([5, 6, 7]);
        int first = a.GetHashCode();
        Assert.Equal(first, a.GetHashCode());
        Assert.Equal(first, a.GetHashCode());
    }

    [Fact]
    public void ToString_UsesJavaBraceFormat()
    {
        Assert.Equal("{}", new IntArray().ToString());
        Assert.Equal("{1}", new IntArray([1]).ToString());
        Assert.Equal("{1, 2, 3}", new IntArray([1, 2, 3]).ToString());
    }
}