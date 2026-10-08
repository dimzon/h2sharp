/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The window of a stream, and the helpers around it.
 *
 * These are what a LOB is made of: bytes in the middle of a file with a length of their own, and
 * the store hands them out as a stream. The end of the window is not the end of the file, and the
 * cases here are about telling those two apart — reading at the end of the window is the end of the
 * data, reading past the window is still the end of the data and not a failure, and asking for more
 * than there is is an end of the stream.
 */
using System;
using System.IO;
using System.Text;
using H2Sharp.Store;
using H2Sharp.Util;
using Xunit;

namespace H2Sharp.Tests;

public sealed class RangeStreamTests
{
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void AWindowIsAPartOfAStreamAndNotAllOfIt()
    {
        using MemoryStream source = new(Bytes("0123456789"));
        using RangeInputStream window = new(source, 3, 4);

        // The length is the length of the whole stream, which is what the reference's window class
        // answers: it is a filter over another stream and does not override it.
        Assert.Equal(10, window.Length);
        Assert.Equal("3456", Encoding.UTF8.GetString(IOUtils.ReadBytesAndClose(window, 4)));
    }

    [Fact]
    public void TheEndOfTheWindowIsTheEndOfTheData()
    {
        using MemoryStream source = new(Bytes("0123456789"));
        using RangeInputStream window = new(source, 2, 3);

        byte[] buffer = new byte[10];
        int read = window.Read(buffer, 0, buffer.Length);
        Assert.Equal(3, read);
        Assert.Equal("234", Encoding.UTF8.GetString(buffer, 0, 3));

        // And reading again answers nothing, even though the stream underneath still has bytes.
        Assert.Equal(0, window.Read(buffer, 0, buffer.Length));
        Assert.Equal(-1, window.ReadByte());
    }

    [Fact]
    public void ReadingOneByteAtATimeStopsAtTheWindow()
    {
        using MemoryStream source = new(Bytes("abcdef"));
        using RangeInputStream window = new(source, 1, 2);

        Assert.Equal('b', window.ReadByte());
        Assert.Equal('c', window.ReadByte());
        Assert.Equal(-1, window.ReadByte());
    }

    [Fact]
    public void WhatIsLeftIsReportedWithoutGoingPastTheWindow()
    {
        using MemoryStream source = new(Bytes("abcdefghij"));
        using RangeInputStream window = new(source, 0, 3);

        Assert.Equal(3, window.Available());
        Assert.Equal(2, window.Skip(2));
        Assert.Equal(1, window.Available());
        Assert.Equal('c', window.ReadByte());
        Assert.Equal(0, window.Available());
    }

    [Fact]
    public void SkippingPastTheWindowStopsAtTheWindow()
    {
        using MemoryStream source = new(Bytes("abcdefghij"));
        using RangeInputStream window = new(source, 0, 3);

        Assert.Equal(3, window.Skip(100));
        Assert.Equal(0, window.ReadByte() + 1);
    }

    [Fact]
    public void AWindowThatStartsPastTheEndIsAnEndOfTheStream()
    {
        // A stream that can seek is allowed to be at a place past its end — there is nothing there,
        // and reading says so. A stream that cannot seek has no such place, so asking for one runs
        // out of bytes, and that is an end of the stream.
        using MemoryStream seekable = new(Bytes("abc"));
        using RangeInputStream window = new(seekable, 10, 5);
        Assert.Equal(0, window.Read(new byte[4]));

        using ForwardOnly forward = new(Bytes("abc"));
        Assert.ThrowsAny<IOException>(() => new RangeInputStream(forward, 10, 5));
    }

    /// <summary>
    /// A stream that can be read and not sought, which is what the store hands out when it hands
    /// out a part of a file it has already opened.
    /// </summary>
    private sealed class ForwardOnly : Stream
    {
        private readonly byte[] bytes;

        private int position;

        internal ForwardOnly(byte[] bytes) => this.bytes = bytes;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int n = Math.Min(buffer.Length, bytes.Length - position);
            if (n <= 0)
            {
                return 0;
            }

            bytes.AsSpan(position, n).CopyTo(buffer);
            position += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void TheWindowIsReadOnlyAndCannotBeSeeked()
    {
        using MemoryStream source = new(Bytes("abc"));
        using RangeInputStream window = new(source, 0, 3);

        Assert.True(window.CanRead);
        Assert.False(window.CanSeek);
        Assert.False(window.CanWrite);
        Assert.False(window.MarkSupported);
        Assert.Throws<NotSupportedException>(() => window.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => window.SetLength(1));
        Assert.Throws<NotSupportedException>(() => window.WriteByte(1));
        Assert.Throws<NotSupportedException>(() => window.Write([1], 0, 1));
    }

    [Fact]
    public void AWindowOfTextIsAPartOfTheTextAndNotAllOfIt()
    {
        using StringReader source = new("0123456789");
        using RangeReader window = new(source, 3, 4);

        char[] buffer = new char[10];
        int read = window.Read(buffer, 0, buffer.Length);
        Assert.Equal(4, read);
        Assert.Equal("3456", new string(buffer, 0, 4));
        Assert.Equal(0, window.Read(buffer, 0, buffer.Length));
        Assert.Equal(-1, window.Read());
    }

    [Fact]
    public void AWindowOfTextReadsOneCharacterAtATimeToo()
    {
        using StringReader source = new("abcdef");
        using RangeReader window = new(source, 1, 2);

        Assert.Equal('b', window.Read());
        Assert.Equal('c', window.Read());
        Assert.Equal(-1, window.Read());
    }

    [Fact]
    public void AWindowOfTextSkipsOnlyWhatIsInIt()
    {
        using StringReader source = new("abcdefghij");
        using RangeReader window = new(source, 0, 3);

        Assert.Equal(3, window.Skip(100));
        Assert.Equal(-1, window.Read());
    }

    [Fact]
    public void ClosingAWindowClosesWhatIsUnderIt()
    {
        MemoryStream source = new(Bytes("abc"));
        RangeInputStream window = new(source, 0, 3);
        window.Dispose();
        Assert.False(source.CanRead);
    }

    [Fact]
    public void ReadingFullyFillsWhatItCanAndStopsAtTheEnd()
    {
        using MemoryStream source = new(Bytes("abcdef"));
        byte[] buffer = new byte[10];

        // Six bytes into ten: what is there, and no exception about the rest.
        Assert.Equal(6, IOUtils.ReadFully(source, buffer, buffer.Length));
        Assert.Equal("abcdef", Encoding.UTF8.GetString(buffer, 0, 6));

        // And at the end it answers zero rather than throwing.
        Assert.Equal(0, IOUtils.ReadFully(source, buffer, buffer.Length));
    }

    [Fact]
    public void ReadingFullyOfTextFillsWhatItCan()
    {
        using StringReader source = new("abcdef");
        char[] buffer = new char[10];
        Assert.Equal(6, IOUtils.ReadFully(source, buffer, buffer.Length));
        Assert.Equal("abcdef", new string(buffer, 0, 6));
        Assert.Equal(0, IOUtils.ReadFully(source, buffer, buffer.Length));
    }

    [Fact]
    public void CopyingTakesOnlyWhatItIsAskedFor()
    {
        using MemoryStream source = new(Bytes("abcdef"));
        using MemoryStream target = new();

        Assert.Equal(3, IOUtils.Copy(source, target, 3));
        Assert.Equal("abc", Encoding.UTF8.GetString(target.ToArray()));
        Assert.Equal(3, IOUtils.Copy(source, target));
        Assert.Equal("abcdef", Encoding.UTF8.GetString(target.ToArray()));
    }

    [Fact]
    public void ReadingAStringToItsEndClosesTheReader()
    {
        StringReader source = new("текст");
        Assert.Equal("текст", IOUtils.ReadStringAndClose(source, 0));

        // The reader is closed, and .NET says so by refusing rather than by answering.
        Assert.Throws<ObjectDisposedException>(() => source.Read());
    }

    [Fact]
    public void ReadingAStringStopsAtTheLengthItIsGiven()
    {
        using StringReader source = new("0123456789");
        Assert.Equal("012", IOUtils.ReadStringAndClose(source, 3));
    }

    [Fact]
    public void ClosingSomethingThatFailsIsNotAnError()
    {
        IOUtils.CloseSilently(null);
        IOUtils.CloseSilently(() => throw new IOException("no"));
        IOUtils.CloseSilently(static () => throw new ObjectDisposedException("x"));

        // And one that works is simply called.
        bool closed = false;
        IOUtils.CloseSilently(() => closed = true);
        Assert.True(closed);
    }

    [Fact]
    public void TextOverAStreamIsUtf8WithoutAByteOrderMark()
    {
        using MemoryStream buffer = new();
        using (TextWriter writer = IOUtils.GetBufferedWriter(buffer))
        {
            writer.Write("Привет");
        }

        // No byte order mark: H2's own bytes never start with one.
        byte[] bytes = buffer.ToArray();
        Assert.NotEqual(0xEF, bytes[0]);

        buffer.Position = 0;
        using TextReader reader = IOUtils.GetReader(buffer);
        Assert.Equal("Привет", reader.ReadToEnd());
    }
}
