/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The stream forms of the variable-length writers must write what the buffer forms write:
 * the buffer forms are what the oracle compares against the JVM and what the pages carry,
 * so a stream form that diverged would write ids and block headers the reader refuses.
 */
using System;
using System.IO;
using H2Sharp.MvStore;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// Pins that the stream varint and varlong writers answer their buffer counterparts.
/// </summary>
public sealed class DataUtilsStreamWriteTests
{
    private static readonly int[] IntValues =
    [
        0, 1, 0x7f, 0x80, 0xff, 0x3fff, 0x4000, 0x1fffff, 0x200000,
        int.MaxValue, -1, int.MinValue, -0x40000000,
    ];

    private static readonly long[] LongValues =
    [
        0L, 1L, 0x7fL, 0x80L, 0x3fffL, 0x200000L, 0x10000000L, 0x100000000L,
        long.MaxValue, -1L, long.MinValue,
    ];

    [Fact]
    public void WriteVarIntToStreamMatchesTheBufferForm()
    {
        foreach (int x in IntValues)
        {
            MemoryStream streamForm = new();
            DataUtils.WriteVarInt(streamForm, x);

            byte[] bufferForm = new byte[16];
            int position = 0;
            DataUtils.WriteVarInt(bufferForm, ref position, x);

            Assert.Equal(bufferForm[..position], streamForm.ToArray());
        }
    }

    [Fact]
    public void WriteVarLongToStreamMatchesTheBufferForm()
    {
        foreach (long x in LongValues)
        {
            MemoryStream streamForm = new();
            DataUtils.WriteVarLong(streamForm, x);

            byte[] bufferForm = new byte[16];
            int position = 0;
            DataUtils.WriteVarLong(bufferForm, ref position, x);

            Assert.Equal(bufferForm[..position], streamForm.ToArray());
        }
    }
}
