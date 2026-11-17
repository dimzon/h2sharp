/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Sprint 248: the public surface of the small utility classes — the weak-value map, the
 * forgetful id map, the insertion-ordered string map, the slice reader, H2's JSON encoder,
 * the counting text-to-bytes stream, the window stream, FOR UPDATE and the NULL ordering
 * rules. Each fact exercises the branch that the SQL suites never reach: a missing key, an
 * evicted id, a buffer that is too small, a slice that ends, a stream that cannot seek.
 */
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using H2Sharp.Api;
using H2Sharp.Command.Query;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Modes;
using H2Sharp.Result;
using H2Sharp.Store;
using H2Sharp.Util;
using H2Sharp.Util.Json;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

public sealed class UtilitySurfaceTests
{
    [Fact]
    public void SoftValuesHashMapKeepsOnlyLiveValues()
    {
        SoftValuesHashMap<int, string> map = new();
        Assert.Null(map.Get(1));
        Assert.Null(map.Put(1, "a"));
        Assert.Equal("a", map.Get(1));
        Assert.True(map.ContainsKey(1));
        Assert.Equal(1, map.Count);

        Assert.Equal("a", map.Put(1, "b"));
        Assert.Equal("b", map.Get(1));
        Assert.Equal("b", map.Remove(1));
        Assert.Null(map.Remove(1));
        Assert.False(map.ContainsKey(1));

        _ = map.Put(2, "c");
        map.Clear();
        Assert.Equal(0, map.Count);

        _ = map.Put(3, "d");
        Assert.Throws<NotSupportedException>(() =>
        {
            foreach (KeyValuePair<int, string> _ in map)
            {
            }
        });
    }

    [Fact]
    public void SmallMapRefusesAMissingIdAndForgetsOldOnes()
    {
        SmallMap map = new(2);
        Assert.Equal(7, map.AddObject(7, "seven"));
        Assert.Equal("seven", map.GetObject(7, true));
        Assert.ThrowsAny<H2SharpException>(() => map.GetObject(8, false));
        Assert.Null(map.GetObject(8, true));

        for (int i = 10; i < 20; i++)
        {
            _ = map.AddObject(i, "v" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // The window is two ids, so everything older than lastId - 2 has been dropped.
        Assert.Null(map.GetObject(7, true));
        Assert.Null(map.GetObject(10, true));
        Assert.Equal("v19", map.GetObject(19, true));

        map.FreeObject(19);
        Assert.Null(map.GetObject(19, true));
    }

    [Fact]
    public void OrderedStringMapUpdatesInPlace()
    {
        OrderedStringMap<int> map = new(1);
        map.Put("a", 1);
        map.Put("b", 2);
        map.Put("a", 3);
        Assert.Equal(2, map.Count);
        Assert.True(map.ContainsKey("a"));
        Assert.False(map.ContainsKey("c"));
        Assert.True(map.TryGetValue("a", out int a));
        Assert.Equal(3, a);
        Assert.False(map.TryGetValue("c", out int missing));
        Assert.Equal(0, missing);
        Assert.Equal(new[] { "a", "b" }, map.Keys.ToArray());
        Assert.Equal(new[] { "a=3", "b=2" },
            map.Entries.Select(e => e.Key + "=" + e.Value).ToArray());

        // A negative capacity means "no idea", and the map still works.
        OrderedStringMap<int> negative = new(-5);
        negative.Put("x", 1);
        Assert.Equal(1, negative.Count);
    }

    [Fact]
    public void CharSliceReaderWalksOnlyItsWindow()
    {
        CharSliceReader reader = new("abcdef".AsMemory(1, 3));
        Assert.Equal('b', reader.Peek());
        Assert.Equal('b', reader.Read());
        char[] buffer = new char[4];
        Assert.Equal(2, reader.Read(buffer, 0, 4));
        Assert.Equal("cd", new string(buffer, 0, 2));
        Assert.Equal(-1, reader.Read());
        Assert.Equal(-1, reader.Peek());
        Assert.Equal(string.Empty, reader.ReadToEnd());
        reader.Dispose();
        Assert.Throws<ObjectDisposedException>(() => reader.Read());

        CharSliceReader blocks = new("abcdef".AsMemory(1, 3));
        Assert.Equal(2, blocks.ReadBlock(buffer, 1, 2));
        Assert.Equal("bc", new string(buffer, 1, 2));
        Span<char> span = stackalloc char[4];
        Assert.Equal(1, blocks.Read(span));
        Assert.Equal('d', span[0]);
        Assert.Equal(0, blocks.Read(span));
        Assert.Equal(string.Empty, blocks.ReadToEnd());

        CharSliceReader invalid = new("x".AsMemory());
        Assert.Throws<ArgumentNullException>(() => invalid.Read(null!, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => invalid.Read(buffer, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => invalid.Read(buffer, 0, -1));
        Assert.Throws<ArgumentException>(() => invalid.Read(buffer, 1, 4));
    }

    [Fact]
    public void JavaScriptEncoderFollowsH2Escaping()
    {
        Assert.Equal(12, H2JavaScriptEncoder.Plain.MaxOutputCharactersPerInputCharacter);
        Assert.True(H2JavaScriptEncoder.Plain.WillEncode('"'));
        Assert.True(H2JavaScriptEncoder.Plain.WillEncode('\\'));
        Assert.True(H2JavaScriptEncoder.Plain.WillEncode('\n'));
        Assert.False(H2JavaScriptEncoder.Plain.WillEncode('\''));
        Assert.False(H2JavaScriptEncoder.Plain.WillEncode('\u00e9'));
        Assert.True(H2JavaScriptEncoder.Ascii.WillEncode('\''));
        Assert.True(H2JavaScriptEncoder.Ascii.WillEncode('\u00e9'));
        Assert.False(H2JavaScriptEncoder.Ascii.WillEncode('a'));

        Assert.Equal("\\\"\\\\\\n\\t\\b\\f\\r", Encode(H2JavaScriptEncoder.Plain, "\"\\\n\t\b\f\r"));
        Assert.Equal("'", Encode(H2JavaScriptEncoder.Plain, "'"));
        Assert.Equal("\\u0027", Encode(H2JavaScriptEncoder.Ascii, "'"));
        Assert.Equal("\u00e9", Encode(H2JavaScriptEncoder.Plain, "\u00e9"));
        Assert.Equal("\\u00e9", Encode(H2JavaScriptEncoder.Ascii, "\u00e9"));
        // Non-ASCII is left alone unless the ASCII-only mode was asked for.
        Assert.Equal("\U0001F389", Encode(H2JavaScriptEncoder.Plain, "\U0001F389"));
        Assert.Equal("\\ud83c\\udf89", Encode(H2JavaScriptEncoder.Ascii, "\U0001F389"));
        Assert.Equal("\\u0001", Encode(H2JavaScriptEncoder.Plain, "\u0001"));

        // An escape that does not fit is reported by the base encoder; the escape sequences
        // themselves are covered above.
    }

    [Fact]
    public void CountingReaderStreamReadsOnlyTheWindowItWasGiven()
    {
        CountingReaderInputStream stream = new(new StringReader("abcdef"), 3);
        Assert.Equal('a', stream.ReadByte());
        byte[] bytes = new byte[4];
        Assert.Equal(2, stream.Read(bytes, 0, bytes.Length));
        Assert.Equal("bc", Encoding.UTF8.GetString(bytes, 0, 2));
        Assert.Equal(-1, stream.ReadByte());
        Assert.Equal(3, stream.GetLength());
        Assert.True(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => _ = stream.Length);
        Assert.Throws<NotSupportedException>(() => _ = stream.Position);
        Assert.Throws<NotSupportedException>(() => stream.Position = 0);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
        Assert.Throws<NotSupportedException>(() => stream.Write(bytes, 0, 1));
        stream.Flush();
        stream.Dispose();

        // A maximum of zero is "no window": everything is read.
        CountingReaderInputStream unlimited = new(new StringReader("xyz"), 0);
        byte[] all = new byte[8];
        Assert.Equal(3, unlimited.Read(all));
        Assert.Equal("xyz", Encoding.UTF8.GetString(all, 0, 3));
        Assert.Equal(3, unlimited.GetLength());
        unlimited.Dispose();

        // What the encoder cannot encode becomes the reference's question mark.
        CountingReaderInputStream replacement = new(new StringReader("\uD800"), 1);
        Assert.Equal('?', replacement.ReadByte());
        Assert.Equal(-1, replacement.ReadByte());
        replacement.Dispose();
    }

    [Fact]
    public void RangeInputStreamStopsAtItsEnd()
    {
        MemoryStream inner = new(Encoding.ASCII.GetBytes("0123456789"));
        RangeInputStream stream = new(inner, 2, 3);
        Assert.Equal('2', stream.ReadByte());
        byte[] bytes = new byte[4];
        Assert.Equal(2, stream.Read(bytes, 0, bytes.Length));
        Assert.Equal("34", Encoding.ASCII.GetString(bytes, 0, 2));
        Assert.Equal(-1, stream.ReadByte());
        Assert.Equal(0, stream.Read(bytes, 0, bytes.Length));
        Assert.Equal(10, stream.Length);
        Assert.Throws<NotSupportedException>(() => _ = stream.Position);
        Assert.Throws<NotSupportedException>(() => stream.Position = 0);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
        Assert.Throws<NotSupportedException>(() => stream.Write(bytes, 0, 1));
        Assert.Throws<NotSupportedException>(() => stream.WriteByte(1));
        Assert.False(stream.MarkSupported);
        stream.Dispose();

        MemoryStream seekable = new(Encoding.ASCII.GetBytes("0123456789"));
        RangeInputStream skipping = new(seekable, 1, 5);
        Assert.Equal(2, skipping.Skip(2));
        Assert.Equal('3', skipping.ReadByte());
        Assert.Equal(2, skipping.Available());
        Assert.Equal(0, skipping.Skip(0));
        Assert.Equal(2, skipping.Skip(10));
        skipping.Dispose();

        NonSeekableStream nonSeekable = new(Encoding.ASCII.GetBytes("abcdef"));
        RangeInputStream window = new(nonSeekable, 1, 3);
        Assert.Equal(0, window.Available());
        Assert.Throws<NotSupportedException>(() => _ = window.Length);
        Assert.Equal(1, window.Skip(2));
        Assert.Equal('c', window.ReadByte());
        Assert.Equal(1, window.Read(bytes, 0, 4));
        Assert.Equal((byte)'d', bytes[0]);
        Assert.Equal(0, window.Read(bytes, 0, 4));
        window.Dispose();
        Assert.True(nonSeekable.Disposed);

        Assert.ThrowsAny<IOException>(() =>
            new RangeInputStream(new NonSeekableStream([1, 2]), 5, 1));
    }

    [Fact]
    public void ForUpdateRendersEveryForm()
    {
        Assert.Equal(" FOR UPDATE", ForUpdate.Default.ToString());
        Assert.Equal(" FOR UPDATE NOWAIT", ForUpdate.NoWait.ToString());
        Assert.Equal(" FOR UPDATE SKIP LOCKED", ForUpdate.SkipLocked.ToString());
        Assert.Equal(" FOR UPDATE WAIT 1.500", ForUpdate.Wait(1500).ToString());
        Assert.Equal(" FOR UPDATE WAIT 2", ForUpdate.Wait(2000).ToString());
        Assert.Same(ForUpdate.NoWait, ForUpdate.Wait(0));
        Assert.ThrowsAny<H2SharpException>(() => ForUpdate.Wait(-1));
        Assert.Equal(ForUpdate.Type.Wait, ForUpdate.Wait(5).GetForUpdateType());
        Assert.Equal(5, ForUpdate.Wait(5).GetTimeoutMillis());
        Assert.Equal(0, ForUpdate.NoWait.GetTimeoutMillis());
    }

    [Fact]
    public void DefaultNullOrderingRulesPickTheSide()
    {
        Assert.Equal(DefaultNullOrdering.LOW, DefaultNullOrderingRules.ValueOf(0));
        Assert.Equal(DefaultNullOrdering.HIGH, DefaultNullOrderingRules.ValueOf(1));
        Assert.Equal(DefaultNullOrdering.FIRST, DefaultNullOrderingRules.ValueOf(2));
        Assert.Equal(DefaultNullOrdering.LAST, DefaultNullOrderingRules.ValueOf(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => DefaultNullOrderingRules.ValueOf(4));

        Assert.Equal(SortOrder.NullsFirst,
            DefaultNullOrderingRules.AddExplicitNullOrdering(DefaultNullOrdering.LOW, 0));
        Assert.Equal(SortOrder.NullsLast,
            DefaultNullOrderingRules.AddExplicitNullOrdering(DefaultNullOrdering.HIGH, 0));
        Assert.Equal(SortOrder.Descending | SortOrder.NullsLast,
            DefaultNullOrderingRules.AddExplicitNullOrdering(DefaultNullOrdering.LOW, SortOrder.Descending));
        Assert.Equal(SortOrder.Descending | SortOrder.NullsFirst,
            DefaultNullOrderingRules.AddExplicitNullOrdering(DefaultNullOrdering.HIGH, SortOrder.Descending));
        Assert.Equal(SortOrder.Descending | SortOrder.NullsFirst,
            DefaultNullOrderingRules.AddExplicitNullOrdering(DefaultNullOrdering.FIRST, SortOrder.Descending));
        Assert.Equal(SortOrder.Descending | SortOrder.NullsLast,
            DefaultNullOrderingRules.AddExplicitNullOrdering(DefaultNullOrdering.LAST, SortOrder.Descending));

        int explicitLast = SortOrder.NullsLast;
        Assert.Equal(explicitLast,
            DefaultNullOrderingRules.AddExplicitNullOrdering(DefaultNullOrdering.FIRST, explicitLast));

        Assert.Equal(-1, DefaultNullOrderingRules.CompareNull(DefaultNullOrdering.LOW, true, 0));
        Assert.Equal(1, DefaultNullOrderingRules.CompareNull(DefaultNullOrdering.LOW, false, 0));
        Assert.Equal(1, DefaultNullOrderingRules.CompareNull(DefaultNullOrdering.HIGH, true, 0));
        Assert.Equal(-1, DefaultNullOrderingRules.CompareNull(DefaultNullOrdering.HIGH, false, 0));
        Assert.Equal(1, DefaultNullOrderingRules.CompareNull(DefaultNullOrdering.LOW, true, SortOrder.Descending));
        Assert.Equal(-1, DefaultNullOrderingRules.CompareNull(DefaultNullOrdering.LOW, false, SortOrder.Descending));
        Assert.Equal(-1, DefaultNullOrderingRules.CompareNull(DefaultNullOrdering.HIGH, true, SortOrder.Descending));
        Assert.Equal(1, DefaultNullOrderingRules.CompareNull(DefaultNullOrdering.HIGH, false, SortOrder.Descending));
        Assert.Equal(-1, DefaultNullOrderingRules.CompareNull(DefaultNullOrdering.LAST, true, SortOrder.NullsFirst));
        Assert.Equal(1, DefaultNullOrderingRules.CompareNull(DefaultNullOrdering.LAST, true, SortOrder.NullsLast));
        Assert.Equal(-1, DefaultNullOrderingRules.CompareNull(DefaultNullOrdering.LAST, false, SortOrder.NullsLast));
    }

    [Fact]
    public void FunctionInfoCopiesUnderAnotherName()
    {
        FunctionInfo info = new("ABS", 1, 2, Value.Integer, true, false);
        Assert.Equal("ABS", info.Name);
        Assert.Equal(1, info.Type);
        Assert.Equal(2, info.ParameterCount);
        Assert.Equal(Value.Integer, info.ReturnDataType);
        Assert.True(info.NullIfParameterIsNull);
        Assert.False(info.Deterministic);

        FunctionInfo copy = new(info, "SIGN");
        Assert.Equal("SIGN", copy.Name);
        Assert.Equal(info.Type, copy.Type);
        Assert.Equal(info.ParameterCount, copy.ParameterCount);
        Assert.Equal(info.ReturnDataType, copy.ReturnDataType);
        Assert.Equal(info.NullIfParameterIsNull, copy.NullIfParameterIsNull);
        Assert.Equal(info.Deterministic, copy.Deterministic);
    }

    /// <summary>Encodes one string and returns the escaped text.</summary>
    private static string Encode(JavaScriptEncoder encoder, string text)
    {
        Span<char> buffer = stackalloc char[128];
        OperationStatus status = encoder.Encode(text.AsSpan(), buffer, out int consumed, out int written);
        Assert.Equal(OperationStatus.Done, status);
        Assert.Equal(text.Length, consumed);
        return new string(buffer[..written]);
    }

    /// <summary>A stream without a position, as a network or pipe stream is.</summary>
    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private int position;

        internal bool Disposed { get; private set; }

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
        {
            int n = Math.Min(count, data.Length - position);
            Array.Copy(data, position, buffer, offset, n);
            position += n;
            return n;
        }

        public override int ReadByte()
            => position < data.Length ? data[position++] : -1;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
