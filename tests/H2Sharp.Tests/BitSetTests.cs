/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The range methods, checked against java.util.BitSet edge for edge.
 *
 * The hot paths are word-wise, but the edges are the contract: a reversed or negative range
 * is refused rather than silently skipped, an empty range neither answers nor grows, and a
 * position past the end is clear because past the end everything is. Each line below is the
 * reference's answer for the same call, probed on the JVM, next to this side's.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Util;
using H2Sharp.Tests.Oracle;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class BitSetTests
{
    private const string JavaEdges = """
        java.util.BitSet s = new java.util.BitSet();
        try { s.set(-1, 5); System.out.println("=setNeg:ok"); }
        catch (RuntimeException e) { System.out.println("=setNeg:throws"); }
        try { s.set(5, 3); System.out.println("=setRev:ok"); }
        catch (RuntimeException e) { System.out.println("=setRev:throws"); }
        s.set(5, 5); System.out.println("=setEmpty:" + s.size());
        try { s.clear(-1, 5); System.out.println("=clearNeg:ok"); }
        catch (RuntimeException e) { System.out.println("=clearNeg:throws"); }
        try { s.clear(5, 3); System.out.println("=clearRev:ok"); }
        catch (RuntimeException e) { System.out.println("=clearRev:throws"); }
        System.out.println("=prevNeg1:" + s.previousClearBit(-1));
        try { System.out.println("=prevNeg2:" + s.previousClearBit(-2)); }
        catch (RuntimeException e) { System.out.println("=prevNeg2:throws"); }
        System.out.println("=prevBeyond:" + s.previousClearBit(1000));
        s.set(10, 20); System.out.println("=prevMid:" + s.previousClearBit(15));
        """;

    [JavaOracleFact]
    public void EdgesMatchTheReference()
    {
        List<string> actual = [];
        BitSet s = new();
        try { s.SetRange(-1, 5); actual.Add("=setNeg:ok"); }
        catch (IndexOutOfRangeException) { actual.Add("=setNeg:throws"); }
        try { s.SetRange(5, 3); actual.Add("=setRev:ok"); }
        catch (IndexOutOfRangeException) { actual.Add("=setRev:throws"); }
        s.SetRange(5, 5); actual.Add("=setEmpty:" + s.Capacity);
        try { s.ClearRange(-1, 5); actual.Add("=clearNeg:ok"); }
        catch (IndexOutOfRangeException) { actual.Add("=clearNeg:throws"); }
        try { s.ClearRange(5, 3); actual.Add("=clearRev:ok"); }
        catch (IndexOutOfRangeException) { actual.Add("=clearRev:throws"); }
        actual.Add("=prevNeg1:" + s.PreviousClearBit(-1));
        try { actual.Add("=prevNeg2:" + s.PreviousClearBit(-2)); }
        catch (IndexOutOfRangeException) { actual.Add("=prevNeg2:throws"); }
        actual.Add("=prevBeyond:" + s.PreviousClearBit(1000));
        s.SetRange(10, 20); actual.Add("=prevMid:" + s.PreviousClearBit(15));

        List<string> java = OracleRunner.Run([], JavaEdges, null, null, null);
        Assert.Equal(java, actual);
    }

    [Fact]
    public void RangesCrossWordBoundaries()
    {
        BitSet s = new();
        s.SetRange(60, 70);
        Assert.False(s.Get(59));
        for (int i = 60; i < 70; i++)
        {
            Assert.True(s.Get(i));
        }

        Assert.False(s.Get(70));
        Assert.Equal(10, s.Cardinality());

        s.ClearRange(63, 65);
        Assert.True(s.Get(62));
        Assert.False(s.Get(63));
        Assert.False(s.Get(64));
        Assert.True(s.Get(65));
        Assert.Equal(8, s.Cardinality());

        s.SetRange(0, 128);
        Assert.Equal(128, s.Cardinality());
        Assert.Equal(-1, s.PreviousClearBit(127));
        Assert.Equal(200, s.PreviousClearBit(200));
        s.ClearRange(0, 64);
        Assert.Equal(63, s.PreviousClearBit(127));
        Assert.Equal(63, s.PreviousClearBit(63));
        BitSet full = new();
        full.SetRange(0, 64);
        Assert.Equal(-1, full.PreviousClearBit(63));
    }

    /// <summary>
    /// <c>set(int)</c>, <c>set(int, boolean)</c> and <c>clear(int)</c>, the three calls the
    /// parser and the transaction snapshots make.
    /// </summary>
    /// <remarks>
    /// No oracle line: these are the Java methods the port's own classes call, and what
    /// matters is that growth follows the highest set bit and that clearing the top bit
    /// shrinks the answer, exactly as <c>length()</c> does on the JVM.
    /// </remarks>
    [Fact]
    public void SingleBitSetAndClearFollowLength()
    {
        BitSet s = new();
        Assert.Equal(0, s.Length);

        s.Set(5);
        Assert.True(s.Get(5));
        Assert.Equal(6, s.Length);

        s.Set(5);
        Assert.Equal(1, s.Cardinality());

        s.Set(5, false);
        Assert.False(s.Get(5));
        Assert.Equal(0, s.Length);

        s.Set(70);
        Assert.Equal(71, s.Length);
        Assert.Equal(128, s.Capacity);

        s.Clear(70);
        Assert.Equal(0, s.Length);
        Assert.Equal(0, s.Cardinality());
        Assert.Equal(128, s.Capacity);

        s.Clear(1000);
        Assert.Equal(0, s.Length);
    }}
