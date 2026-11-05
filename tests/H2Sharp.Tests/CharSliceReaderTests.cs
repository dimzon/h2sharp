/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The windowed reader over a value: the slice must read exactly what StringReader over
 * the corresponding substring reads, and creating it must not copy the window.
 */
using System;
using System.IO;
using System.Text;
using H2Sharp.Message;
using H2Sharp.Values;
using Xunit;
using Xunit.Abstractions;

namespace H2Sharp.Tests;

[Collection(MeasurementCollection.Name)]
public sealed class CharSliceReaderTests
{
    private readonly ITestOutputHelper output;

    public CharSliceReaderTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    private static long Measure(Action action)
    {
        for (int i = 0; i < 3; i++)
        {
            action();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static string Drain(TextReader reader, int chunk)
    {
        StringBuilder builder = new();
        char[] buffer = new char[chunk];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }

    private static void AssertSameAsStringReader(string text, int chunk)
    {
        Value value = ValueVarchar.Get(text);
        using StringReader expected = new(text);
        using TextReader actual = value.GetReader(1, text.Length);

        Assert.Equal(Drain(expected, chunk), Drain(actual, chunk));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(1024)]
    public void ChunkedReadsMatchStringReader(int chunk)
        => AssertSameAsStringReader("ab\uD83C\uDF89cd\nef\r\ngh", chunk);

    [Fact]
    public void PeekDoesNotAdvance()
    {
        using TextReader reader = ValueVarchar.Get("abc").GetReader(2, 2);
        Assert.Equal('b', reader.Peek());
        Assert.Equal('b', reader.Peek());
        Assert.Equal('b', reader.Read());
        Assert.Equal('c', reader.Peek());
    }

    [Fact]
    public void ReadToEndFromTheMiddleMatches()
    {
        using TextReader reader = ValueVarchar.Get("abcdef").GetReader(3, 4);
        Assert.Equal("cdef", reader.ReadToEnd());
        Assert.Equal(string.Empty, reader.ReadToEnd());
        Assert.Equal(-1, reader.Read());
    }

    [Fact]
    public void ReadLineMatches()
    {
        string text = "one\ntwo\r\nthree\rfour";
        using StringReader expected = new(text);
        using TextReader actual = ValueVarchar.Get(text).GetReader(1, text.Length);
        while (true)
        {
            string? left = expected.ReadLine();
            string? right = actual.ReadLine();
            Assert.Equal(left, right);
            if (left is null)
            {
                break;
            }
        }
    }

    [Fact]
    public void AnEmptyWindowReadsNothing()
    {
        using TextReader reader = ValueVarchar.Get("abc").GetReader(2, 0);
        Assert.Equal(-1, reader.Peek());
        Assert.Equal(-1, reader.Read());
        Assert.Equal(string.Empty, reader.ReadToEnd());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 2)]
    [InlineData(1, -1)]
    public void OutOfRangeWindowsAreRefusedAsBefore(long offset, long length)
    {
        Value value = ValueVarchar.Get("abc");
        Assert.Throws<H2SharpException>(() => value.GetReader(offset, length));
    }

    [Fact]
    public void AWindowedReaderDoesNotCopyTheWindow()
    {
        const int Length = 100 * 1024;
        const int Window = 50 * 1024;
        string text = new('a', Length);
        Value value = ValueVarchar.Get(text);

        long bytes = Measure(() =>
        {
            using TextReader reader = value.GetReader(1, Window);
            if (reader.Read() != 'a')
            {
                throw new InvalidOperationException("unexpected first character");
            }
        });

        output.WriteLine(FormattableString.Invariant($"GetReader(1, {Window}) allocated {bytes} bytes"));
        Assert.True(
            bytes < 1024,
            FormattableString.Invariant($"a {Window}-char window allocated {bytes} bytes; the substring copy is back"));
    }
}
