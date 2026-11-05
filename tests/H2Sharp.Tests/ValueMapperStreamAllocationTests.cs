/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * A stream parameter that fits inline is copied into the array the value owns. The old path
 * copied it twice: MemoryStream grew its buffer while copying, then ToArray copied it again.
 * These pin the single copy and the bytes that come out of it.
 */
using System;
using System.IO;
using H2Sharp.Data;
using H2Sharp.Values;
using Xunit;
using Xunit.Abstractions;

namespace H2Sharp.Tests;

[Collection(MeasurementCollection.Name)]
public sealed class ValueMapperStreamAllocationTests
{
    private readonly ITestOutputHelper output;

    public ValueMapperStreamAllocationTests(ITestOutputHelper output)
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

    private static byte[] Bytes(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 31);
        }

        return data;
    }

    [Fact]
    public void AnInlineStreamIsCopiedOnce()
    {
        const int Length = 100 * 1024;
        byte[] data = Bytes(Length);
        MemoryStream stream = new(data);

        long bytes = Measure(() =>
        {
            stream.Position = 0;
            Value value = H2ValueMapper.ToValue(stream, null);
            if (value.GetBytesNoCopy()!.Length != Length)
            {
                throw new InvalidOperationException("unexpected blob length");
            }
        });

        output.WriteLine(FormattableString.Invariant($"stream({Length}) allocated {bytes} bytes"));
        Assert.True(
            bytes < 160 * 1024,
            FormattableString.Invariant($"a {Length}-byte stream allocated {bytes} bytes; the second copy is back"));
    }

    [Fact]
    public void TheBytesAreTheStreamsBytes()
    {
        byte[] data = Bytes(4096);
        Value value = H2ValueMapper.ToValue(new MemoryStream(data), null);

        Assert.Equal(data, value.GetBytesNoCopy());
    }

    [Fact]
    public void AStreamShorterThanItsLengthKeepsWhatItProduced()
    {
        byte[] data = Bytes(1000);
        Value value = H2ValueMapper.ToValue(new OverstatedLengthStream(data), null);

        Assert.Equal(data, value.GetBytesNoCopy());
    }

    /// <summary>
    /// A seekable stream whose Length promises more than it produces: the value keeps the
    /// bytes that actually arrived.
    /// </summary>
    private sealed class OverstatedLengthStream : Stream
    {
        private readonly byte[] data;

        private int position;

        internal OverstatedLengthStream(byte[] data)
        {
            this.data = data;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => data.Length + 100;

        public override long Position
        {
            get => position;
            set => position = (int)value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(count, data.Length - position);
            if (n <= 0)
            {
                return 0;
            }

            Array.Copy(data, position, buffer, offset, n);
            position += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
    }
}
