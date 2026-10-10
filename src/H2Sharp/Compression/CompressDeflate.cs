using System;
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
/// trailer. .NET's <c>DeflateStream</c> writes only the raw deflate middle, so the header and the
/// trailer go on here — without them a file written on one side would not open on the other.
/// The header's second byte follows zlib's own mapping of level to header, and the trailer is the
/// Adler-32 of the input, which the reference's <c>Inflater</c> checks itself.
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
        byte[] raw;
        using (var buffer = new MemoryStream())
        {
            using (var deflate = new DeflateStream(buffer, MapLevel(level), true))
            {
                deflate.Write(input, inPos, inLen);
            }

            raw = buffer.ToArray();
        }

        if (raw.Length == 0 && inLen > 0)
        {
            // the compressed length is 0, meaning compression didn't work
            // (sounds like a JDK bug)
            // try again, using the default strategy and compression level
            strategy = DefaultStrategy;
            level = DefaultCompression;
            return Compress(input, inPos, inLen, output, outPos);
        }

        output[outPos] = 0x78;
        output[outPos + 1] = level <= 1 ? (byte)0x01 : level >= 7 ? (byte)0xDA : (byte)0x9C;
        // What fits is what the reference's bounded deflate would have written; the caller only
        // keeps the answer when it is shorter than the input, so a truncated tail is never read.
        int body = Math.Min(raw.Length, output.Length - outPos - 6);
        Buffer.BlockCopy(raw, 0, output, outPos + 2, body);
        int end = outPos + 2 + body;
        uint adler = Adler32(input, inPos, inLen);
        output[end] = (byte)(adler >> 24);
        output[end + 1] = (byte)(adler >> 16);
        output[end + 2] = (byte)(adler >> 8);
        output[end + 3] = (byte)adler;
        return end + 4;
    }

    /// <inheritdoc/>
    public int GetAlgorithm() => Compressors.Deflate;

    /// <inheritdoc/>
    public void Expand(byte[] input, int inPos, int inLen, byte[] output, int outPos, int outLen)
    {
        try
        {
            if (inLen < 6 || input[inPos] != 0x78 || (input[inPos] * 256 + input[inPos + 1]) % 31 != 0)
            {
                throw new InvalidDataException("Not a zlib stream");
            }

            using (var buffer = new MemoryStream(input, inPos + 2, inLen - 6, false))
            using (var inflate = new DeflateStream(buffer, CompressionMode.Decompress))
            {
                int total = 0;
                int read;
                while (total < outLen && (read = inflate.Read(output, outPos + total, outLen - total)) > 0)
                {
                    total += read;
                }

                if (total != outLen)
                {
                    throw new InvalidDataException(total + " " + outLen);
                }
            }

            uint expected = (uint)((input[inPos + inLen - 4] << 24)
                | (input[inPos + inLen - 3] << 16)
                | (input[inPos + inLen - 2] << 8)
                | input[inPos + inLen - 1]);
            if (expected != Adler32(output, outPos, outLen))
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

    private static uint Adler32(byte[] data, int pos, int len)
    {
        const uint mod = 65521;
        uint a = 1;
        uint b = 0;
        for (int i = 0; i < len; i++)
        {
            a = (a + data[pos + i]) % mod;
            b = (b + a) % mod;
        }

        return (b << 16) | a;
    }
}
