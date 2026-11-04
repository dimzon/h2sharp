/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Pins the UTF-16 unit counting of in-memory LOBs (the answer string.Length would give,
 * without decoding). The astral case is the trap: U+10000 is one code point but two
 * UTF-16 units, and a naive code-point count answers 1 instead of 2.
 *
 * The decoded text is deliberately not cached: the reference hands the value's own byte
 * array out of GetBytesNoCopy, and mutations through it must be visible in GetString
 * (pinned by ValueLobOracleTests), which a text cache would hide.
 */
using System;
using System.Text;
using H2Sharp.Util;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class ValueLobCodeUnitTests
{
    private static byte[] Utf8Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void CodeUnitCount_TreatsAstralAsTwoUnits()
    {
        Assert.Equal(3, Utf8.CodeUnitCount(Utf8Bytes("ABC")));
        // 'A' + 'é' (two bytes, one unit)
        Assert.Equal(2, Utf8.CodeUnitCount(Utf8Bytes("Aé")));
        // 'A' + U+10000 (four bytes, two units)
        Assert.Equal(3, Utf8.CodeUnitCount(Utf8Bytes("A\uD800\uDC00")));
        // 'A' + U+1F600 (emoji, four bytes, two units)
        Assert.Equal(3, Utf8.CodeUnitCount(Utf8Bytes("A\uD83D\uDE00")));
    }

    [Fact]
    public void ValueBlob_CharLengthCountsUtf16UnitsWithoutDecoding()
    {
        string text = "A\uD800\uDC00";
        ValueBlob blob = ValueBlob.CreateSmall(Utf8Bytes(text));
        Assert.Equal(3, blob.CharLength());
        Assert.Equal(text, blob.GetString());
    }

    [Fact]
    public void ValueClob_CharLengthCountsUtf16UnitsWithoutDecoding()
    {
        string text = "A\uD800\uDC00";
        ValueClob clob = ValueClob.CreateSmall(text);
        Assert.Equal(3, clob.CharLength());
        Assert.Equal(text, clob.GetString());
    }

    [Fact]
    public void ValueBlob_CharLengthAllocatesNothingAfterWarmup()
    {
        ValueBlob blob = ValueBlob.CreateSmall(Utf8Bytes("A\uD800\uDC00"));
        _ = blob.CharLength();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            _ = blob.CharLength();
        }

        // A decode per call would allocate 1000 short strings; the counter answers from the
        // byte count alone, so the ceiling is far below that.
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - start, 0, 1024);
    }
}
