using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using H2Sharp.MvStore;

namespace H2Sharp.Compression;

/// <summary>
/// DEFLATE compression, byte-compatible with the reference's <c>CompressDeflate</c>.
/// </summary>
/// <remarks>
/// Java's <c>Deflater</c> writes zlib (RFC 1950): a two-byte header, raw deflate, and an Adler-32
/// trailer. .NET's <c>ZLibStream</c> writes the same framing natively, so there is no hand-built
/// header or trailer here — without them a file written on one side would not open on the other.
/// <para>
/// Levels map approximately: 0 passes through, 1 is the fastest, 2–6 and the default are optimal,
/// 7–9 are the smallest. There is no strategy knob on this side; the option is accepted and
/// validated, then ignored.
/// </para>
/// </remarks>
public class CompressDeflate : Compressor
{
    // The reference's errors come from org.h2.api.ErrorCode; the two codes are quoted here because
    // that file is generated and must not be hand-edited.
    private const int UnsupportedCompressionOptions = 90102;

    private const int CompressionError = 90104;

    private const int DefaultCompression = -1;

    private const int DefaultStrategy = 0;

    private int level = DefaultCompression;

    private int strategy = DefaultStrategy;

    /// <inheritdoc/>
    public void SetOptions(string options)
    {
        if (options is null)
        {
            return;
        }

        try
        {
            string[] tokens = options.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string option = tokens[i];
                if (option == "level" || option == "l")
                {
                    level = int.Parse(tokens[++i], CultureInfo.InvariantCulture);
                }
                else if (option == "strategy" || option == "s")
                {
                    strategy = int.Parse(tokens[++i], CultureInfo.InvariantCulture);
                }

                ValidateSettings();
            }
        }
        catch (Exception e) when (e is not MvStoreException)
        {
            throw DataUtils.NewMvStoreException(UnsupportedCompressionOptions, options);
        }
    }

    private void ValidateSettings()
    {
        // What constructing a Deflater and setting its strategy would reject.
        if (level < -1 || level > 9 || (uint)strategy > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(strategy));
        }
    }

    /// <inheritdoc/>
    public int Compress(byte[] input, int inPos, int inLen, byte[] output, int outPos)
    {
        // Compressed output is usually shorter than its input, so a buffer of the input's
        // size holds it; the rare expansion falls back to a growing one. Either way no
        // exact-sized copy is built just to copy a prefix of it into the caller's buffer.
        byte[] rented = ArrayPool<byte>.Shared.Rent(Math.Max(inLen, 1));
        try
        {
            int written;
            try
            {
                using (var buffer = new MemoryStream(rented, 0, rented.Length, writable: true))
                {
                    using (var zlib = new ZLibStream(buffer, MapLevel(level), leaveOpen: true))
                    {
                        zlib.Write(input, inPos, inLen);
                    }

                    written = (int)buffer.Position;
                }
            }
            catch (Exception e) when (e is NotSupportedException || e is IOException)
            {
                // The fixed buffer did not hold the answer: incompressible input can come
                // out longer than it went in. A real write error retries below and surfaces
                // from there.
                return CompressGrowing(input, inPos, inLen, output, outPos);
            }

            if (written == 0 && inLen > 0)
            {
                // the compressed length is 0, meaning compression didn't work
                // (sounds like a JDK bug)
                // try again, using the default strategy and compression level
                strategy = DefaultStrategy;
                level = DefaultCompression;
                return Compress(input, inPos, inLen, output, outPos);
            }

            // What fits is what the reference's bounded deflate would have written: a prefix of the
            // stream, which carries no trailer — only a complete stream does. The caller only keeps
            // the answer when it is shorter than the input, so a prefix is never read.
            int body = Math.Min(written, output.Length - outPos);
            Buffer.BlockCopy(rented, 0, output, outPos, body);
            return outPos + body;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private int CompressGrowing(byte[] input, int inPos, int inLen, byte[] output, int outPos)
    {
        byte[] full;
        using (var buffer = new MemoryStream())
        {
            using (var zlib = new ZLibStream(buffer, MapLevel(level), true))
            {
                zlib.Write(input, inPos, inLen);
            }

            full = buffer.ToArray();
        }

        int body = Math.Min(full.Length, output.Length - outPos);
        Buffer.BlockCopy(full, 0, output, outPos, body);
        return outPos + body;
    }

    /// <inheritdoc/>
    public int GetAlgorithm() => Compressors.Deflate;

    /// <inheritdoc/>
    public void Expand(byte[] input, int inPos, int inLen, byte[] output, int outPos, int outLen)
        => Expand(new ReadOnlyMemory<byte>(input, inPos, inLen), output.AsSpan(outPos, outLen));

    /// <inheritdoc/>
    public void Expand(ReadOnlyMemory<byte> input, Span<byte> output)
    {
        try
        {
            if (input.Length < 6 || input.Span[0] != 0x78 || (input.Span[0] * 256 + input.Span[1]) % 31 != 0)
            {
                throw new InvalidDataException("Not a zlib stream");
            }

            using (var buffer = new ReadOnlyMemoryStream(input.Slice(2, input.Length - 6)))
            using (var inflate = new DeflateStream(buffer, CompressionMode.Decompress))
            {
                int total = 0;
                int read;
                while (total < output.Length && (read = inflate.Read(output.Slice(total))) > 0)
                {
                    total += read;
                }

                if (total != output.Length)
                {
                    throw new InvalidDataException(total + " " + output.Length);
                }
            }

            uint expected = (uint)((input.Span[^4] << 24) | (input.Span[^3] << 16)
                | (input.Span[^2] << 8) | input.Span[^1]);
            if (expected != Adler32(output))
            {
                throw new InvalidDataException("Adler-32 mismatch");
            }
        }
        catch (InvalidDataException e)
        {
            throw DataUtils.NewMvStoreException(CompressionError, e.Message, e);
        }
    }

    private static CompressionLevel MapLevel(int level)
        => level == 0 ? CompressionLevel.NoCompression
            : level == 1 ? CompressionLevel.Fastest
            : level >= 7 ? CompressionLevel.SmallestSize
            : CompressionLevel.Optimal;

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        // Batched as zlib does: 5552 is the largest block whose running sums cannot overflow
        // 32 bits, so the modulo runs once per block instead of twice per byte, with the same
        // value either way.
        const uint mod = 65521;
        const int nmax = 5552;
        uint a = 1;
        uint b = 0;
        int len = data.Length;
        int pos = 0;
        while (len > 0)
        {
            int k = len < nmax ? len : nmax;
            len -= k;
            while (k-- > 0)
            {
                a += data[pos++];
                b += a;
            }

            a %= mod;
            b %= mod;
        }

        return (b << 16) | a;
    }
}
