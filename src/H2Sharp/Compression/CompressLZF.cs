/*
 * Based on the LZF algorithm from Marc Lehmann, as reimplemented in
 * org.h2.compress.CompressLZF (H2, MPL 2.0 / EPL 1.0) and ported here.
 * Copyright 2000-2005 Marc Alexander Lehmann <schmorp@schmorp.de>
 * Copyright 2005 Oren J. Maurice <oymaurice@hazorea.org.il>
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Buffers.Binary;
using System.Numerics;

namespace H2Sharp.Compression;

/// <summary>
/// LZF: a Lempel-Ziv variant with byte-aligned output, made for speed.
/// </summary>
/// <remarks>
/// <para>
/// The compressed stream is a sequence of two kinds of piece. A literal run copies bytes straight
/// through, and a back-reference copies bytes from earlier in the output. The first byte of every
/// piece is its control byte: for a literal run the top three bits are clear and the rest is the
/// length minus one, and for a back-reference the top three bits are the length and the rest, with
/// the byte after it, is how far back to copy from. A length that does not fit in three bits takes
/// a second byte, which is why the format spends a byte to save one.
/// </para>
/// <para>
/// The reference writes the same algorithm twice, once over an array and once over a
/// <c>ByteBuffer</c>. Here the second form is a <see cref="Span{T}"/> of bytes, which is what a
/// byte buffer is in this port, and both forms are the same arithmetic on the same numbers.
/// </para>
/// <para>
/// Notes on use, from the reference: one instance per thread, buffers under a gigabyte, and the
/// expansion does not check its bounds while it copies, so damaged input ends in an
/// <see cref="IndexOutOfRangeException"/> rather than in a wrong answer.
/// </para>
/// </remarks>
public sealed class CompressLZF : Compressor
{
    /// <summary>
    /// How many slots the hash table has: a trade between collisions, which cost compression, and
    /// speed, which is what fits in a cache.
    /// </summary>
    /// <remarks>
    /// This size is not part of the format and not part of any contract. A stream this compressor
    /// writes is valid LZF whatever this is, and any LZF reader expands it; what the size decides
    /// is only how well it compresses and how fast it gets there. Sixty-four kilobytes does not
    /// fit in a first-level cache, and on data that does not compress this table is what the loop
    /// waits on: measured, half the slots are 11% faster there and a quarter are 48% faster, at the
    /// price of 5% on both the time and the size of text. It is kept at the reference's size on
    /// purpose — which one wins is a trade of processor time against file size, and that is not
    /// known until there are real pages to weigh. The measurements are in MIGRATION_STATUS.md.
    /// </remarks>
    private const int HashSize = 1 << 14;

    /// <summary>
    /// How many literals fit in one run.
    /// </summary>
    private const int MaxLiteral = 1 << 5;

    /// <summary>
    /// How far back a back-reference may reach.
    /// </summary>
    private const int MaxOff = 1 << 13;

    /// <summary>
    /// The longest back-reference.
    /// </summary>
    private const int MaxRef = (1 << 8) + (1 << 3);

    /// <summary>
    /// The hash table, kept between calls because building it is the expensive part.
    /// </summary>
    private int[]? cachedHashTable;

    /// <inheritdoc/>
    public void SetOptions(string options)
    {
        // Nothing to set.
    }

    /// <summary>
    /// The two bytes at the place, as an integer with the first one in the high half.
    /// </summary>
    /// <remarks>
    /// A call-through: the span overload below holds the logic, so the two cannot drift.
    /// </remarks>
    private static int First(byte[] @in, int inPos) => First(@in.AsSpan(), inPos);

    /// <summary>
    /// The two bytes at the place, as an integer with the first one in the high half.
    /// </summary>
    private static int First(ReadOnlySpan<byte> @in, int inPos) => (@in[inPos] << 8) | (@in[inPos + 1] & 255);

    /// <summary>
    /// The value moved one byte left, with the byte at <paramref name="inPos"/> + 2 added.
    /// </summary>
    /// <remarks>
    /// A call-through, same reason as <see cref="First(byte[], int)"/>.
    /// </remarks>
    private static int Next(int v, byte[] @in, int inPos) => Next(v, @in.AsSpan(), inPos);

    /// <summary>
    /// The value moved one byte left, with the byte at <paramref name="inPos"/> + 2 added.
    /// </summary>
    private static int Next(int v, ReadOnlySpan<byte> @in, int inPos) => (v << 8) | (@in[inPos + 2] & 255);

    /// <summary>
    /// Where in the hash table a value belongs.
    /// </summary>
    private static int Hash(int h) => ((h * 2777) >> 9) & (HashSize - 1);

    /// <summary>
    /// How many bytes from the two places are equal, up to <paramref name="max"/>.
    /// </summary>
    /// <remarks>
    /// A call-through: the span overload below holds the logic, so the two cannot drift.
    /// </remarks>
    private static int EqualFrom(byte[] left, int leftPos, byte[] right, int rightPos, int max)
        => EqualFrom(left.AsSpan(), leftPos, right.AsSpan(), rightPos, max);

    /// <summary>
    /// How many bytes from the two places are equal, up to <paramref name="max"/>.
    /// </summary>
    /// <remarks>
    /// The reference walks a match one byte at a time. A match can be up to 263 bytes long, and
    /// walking it that way is most of the work of compressing text, which is mostly matches: the
    /// bytes are compared eight at a time here instead, and the first pair that differs is found by
    /// the count of the trailing zeroes of the difference — which is exactly the index of the first
    /// byte that differs, because a little-endian read puts the earlier byte in the lower half. So
    /// the length comes out the same as it did one byte at a time.
    /// </remarks>
    private static int EqualFrom(ReadOnlySpan<byte> left, int leftPos, ReadOnlySpan<byte> right, int rightPos, int max)
    {
        int n = 0;
        while (n + 8 <= max)
        {
            ulong l = BinaryPrimitives.ReadUInt64LittleEndian(left.Slice(leftPos + n, 8));
            ulong r = BinaryPrimitives.ReadUInt64LittleEndian(right.Slice(rightPos + n, 8));
            if (l != r)
            {
                return n + (BitOperations.TrailingZeroCount(l ^ r) >> 3);
            }

            n += 8;
        }

        while (n < max && left[leftPos + n] == right[rightPos + n])
        {
            n++;
        }

        return n;
    }

    /// <inheritdoc/>
    public int Compress(byte[] @in, int inPos, int inLen, byte[] @out, int outPos)
    {
        int offset = inPos;
        inLen += inPos;
        int[] hashTab = cachedHashTable ??= new int[HashSize];
        int literals = 0;
        outPos++;
        int future = First(@in, inPos);
        while (inPos < inLen - 4)
        {
            byte p2 = @in[inPos + 2];
            future = (future << 8) + (p2 & 255);
            int off = Hash(future);
            int reference = hashTab[off];
            hashTab[off] = inPos;
            if (reference < inPos
                && reference > offset
                && (off = inPos - reference - 1) < MaxOff
                && @in[reference + 2] == p2
                && @in[reference + 1] == (byte)(future >> 8)
                && @in[reference] == (byte)(future >> 16))
            {
                // A match.
                int maxLen = inLen - inPos - 2;
                if (maxLen > MaxRef)
                {
                    maxLen = MaxRef;
                }

                if (literals == 0)
                {
                    // Back-references in a row, so there is no control byte for a literal run.
                    outPos--;
                }
                else
                {
                    // The control byte at the start of the literal run holds its length.
                    @out[outPos - literals - 1] = (byte)(literals - 1);
                    literals = 0;
                }

                // The three bytes of the match are already known to be equal, and maxLen is never
                // below three, so the count starts where the match does.
                int len = EqualFrom(@in, inPos, @in, reference, maxLen);

                len -= 2;
                if (len < 7)
                {
                    @out[outPos++] = (byte)((off >> 8) + (len << 5));
                }
                else
                {
                    @out[outPos++] = (byte)((off >> 8) + (7 << 5));
                    @out[outPos++] = (byte)(len - 7);
                }

                @out[outPos++] = (byte)off;

                // One byte along, to leave room for the control byte of a literal run.
                outPos++;
                inPos += len;

                // The future is built again and the last two places go into the table: hashing them
                // compresses a little better and costs a little speed.
                future = First(@in, inPos);
                future = Next(future, @in, inPos);
                hashTab[Hash(future)] = inPos++;
                future = Next(future, @in, inPos);
                hashTab[Hash(future)] = inPos++;
            }
            else
            {
                // One byte to the output as part of a literal run.
                @out[outPos++] = @in[inPos++];
                literals++;
                if (literals == MaxLiteral)
                {
                    @out[outPos - literals - 1] = (byte)(literals - 1);
                    literals = 0;

                    // One byte along, to leave room for the control byte of a literal run.
                    outPos++;
                }
            }
        }

        // What is left is too few bytes to look for a match in, so it goes out as literals.
        while (inPos < inLen)
        {
            @out[outPos++] = @in[inPos++];
            literals++;
            if (literals == MaxLiteral)
            {
                @out[outPos - literals - 1] = (byte)(literals - 1);
                literals = 0;
                outPos++;
            }
        }

        // The length of the last literal run goes into its control byte.
        @out[outPos - literals - 1] = (byte)(literals - 1);
        if (literals == 0)
        {
            outPos--;
        }

        return outPos;
    }

    /// <summary>
    /// Compresses a window of bytes, which is what the reference compresses out of a
    /// <c>ByteBuffer</c>: the whole window, with nothing before it to refer back to.
    /// </summary>
    /// <param name="in">the input</param>
    /// <param name="out">where to write</param>
    /// <param name="outPos">where to start writing</param>
    /// <returns>where the writing ended</returns>
    public int Compress(ReadOnlySpan<byte> @in, Span<byte> @out, int outPos)
    {
        const int offset = 0;
        int inLen = @in.Length;
        int[] hashTab = cachedHashTable ??= new int[HashSize];
        int inPos = 0;
        int literals = 0;
        outPos++;
        int future = First(@in, inPos);
        while (inPos < inLen - 4)
        {
            byte p2 = @in[inPos + 2];
            future = (future << 8) + (p2 & 255);
            int off = Hash(future);
            int reference = hashTab[off];
            hashTab[off] = inPos;
            if (reference < inPos
                && reference > offset
                && (off = inPos - reference - 1) < MaxOff
                && @in[reference + 2] == p2
                && @in[reference + 1] == (byte)(future >> 8)
                && @in[reference] == (byte)(future >> 16))
            {
                int maxLen = inLen - inPos - 2;
                if (maxLen > MaxRef)
                {
                    maxLen = MaxRef;
                }

                if (literals == 0)
                {
                    outPos--;
                }
                else
                {
                    @out[outPos - literals - 1] = (byte)(literals - 1);
                    literals = 0;
                }

                int len = EqualFrom(@in, inPos, @in, reference, maxLen);
                len -= 2;
                if (len < 7)
                {
                    @out[outPos++] = (byte)((off >> 8) + (len << 5));
                }
                else
                {
                    @out[outPos++] = (byte)((off >> 8) + (7 << 5));
                    @out[outPos++] = (byte)(len - 7);
                }

                @out[outPos++] = (byte)off;
                outPos++;
                inPos += len;

                future = First(@in, inPos);
                future = Next(future, @in, inPos);
                hashTab[Hash(future)] = inPos++;
                future = Next(future, @in, inPos);
                hashTab[Hash(future)] = inPos++;
            }
            else
            {
                @out[outPos++] = @in[inPos++];
                literals++;
                if (literals == MaxLiteral)
                {
                    @out[outPos - literals - 1] = (byte)(literals - 1);
                    literals = 0;
                    outPos++;
                }
            }
        }

        while (inPos < inLen)
        {
            @out[outPos++] = @in[inPos++];
            literals++;
            if (literals == MaxLiteral)
            {
                @out[outPos - literals - 1] = (byte)(literals - 1);
                literals = 0;
                outPos++;
            }
        }

        @out[outPos - literals - 1] = (byte)(literals - 1);
        if (literals == 0)
        {
            outPos--;
        }

        return outPos;
    }

    /// <inheritdoc/>
    public void Expand(byte[] @in, int inPos, int inLen, byte[] @out, int outPos, int outLen)
    {
        if (inPos < 0 || outPos < 0 || outLen < 0)
        {
            throw new ArgumentException();
        }

        ExpandCore(new ReadOnlySpan<byte>(@in, inPos, @in.Length - inPos), new Span<byte>(@out, outPos, outLen), outLen);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Explicit, because the static span form below already owns this signature.
    /// </remarks>
    void Compressor.Expand(ReadOnlyMemory<byte> input, Span<byte> output) => Expand(input.Span, output);

    /// <summary>
    /// Expands into a window of bytes, which is what the reference expands into a
    /// <c>ByteBuffer</c>.
    /// </summary>
    /// <param name="in">the compressed data</param>
    /// <param name="out">where to write</param>
    public static void Expand(ReadOnlySpan<byte> @in, Span<byte> @out)
    {
        ExpandCore(@in, @out, @out.Length);
    }

    /// <remarks>
    /// How many bytes the answer has is not necessarily how big the window is: the caller may hand
    /// over a buffer it keeps between blocks, and the loop has to stop at the answer, not at the end
    /// of the buffer. The reference keeps the two apart as well, which is why both are passed here.
    /// </remarks>
    private static void ExpandCore(ReadOnlySpan<byte> @in, Span<byte> @out, int outLen)
    {
        int inPos = 0;
        int outPos = 0;
        do
        {
            int ctrl = @in[inPos++] & 255;
            if (ctrl < MaxLiteral)
            {
                // A literal run of ctrl + 1 bytes.
                ctrl++;
                @in.Slice(inPos, ctrl).CopyTo(@out.Slice(outPos, ctrl));
                outPos += ctrl;
                inPos += ctrl;
            }
            else
            {
                // A back-reference: the top three bits are the length.
                int len = ctrl >> 5;
                if (len == 7)
                {
                    len += @in[inPos++] & 255;
                }

                // Three bytes is the least a back-reference can be, so two were taken off the
                // length before it was stored.
                len += 2;

                // What is left of the control byte, together with the byte after it, is how far
                // back to copy from. Writing it this way keeps the arithmetic in the size the
                // bytes arrive in.
                ctrl = -(((ctrl & 0x1F) << 8) + 1);
                ctrl -= @in[inPos++] & 255;
                ctrl += outPos;
                if (outPos + len >= @out.Length)
                {
                    // Checked once, so the copy below needs no check of its own.
                    throw new IndexOutOfRangeException();
                }

                // The copy runs from a place behind the writing, and it is allowed to catch up
                // with itself: a reference shorter than what it copies repeats what it copied, which
                // is how the format gets a run of a hundred equal bytes out of three. Copying byte
                // by byte is the honest way to say that and the slow one, so the first distance is
                // laid down once and then doubled, every round taking a whole distance worth of
                // bytes from what the round before has already written. The same bytes come out in
                // the same order, only faster.
                int back = outPos - ctrl;
                int whole = Math.Min(len, back);
                @out.Slice(ctrl, whole).CopyTo(@out.Slice(outPos, whole));
                outPos += whole;
                len -= whole;
                while (len > 0)
                {
                    int chunk = Math.Min(len, back);
                    @out.Slice(outPos - back, chunk).CopyTo(@out.Slice(outPos, chunk));
                    outPos += chunk;
                    len -= chunk;
                }
            }
        }
        while (outPos < outLen);
    }

    /// <inheritdoc/>
    public int GetAlgorithm() => Compressors.LZF;
}
