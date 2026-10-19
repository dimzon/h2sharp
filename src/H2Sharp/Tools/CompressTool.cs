/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.tools.CompressTool.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Only the compression of a byte array is ported here; the stream wrappers of the reference
 * (BZip2 and Kanzi through reflection, GZIP, ZIP, DEFLATE and LZF) belong to the backup tooling
 * and are not needed by COMPRESS()/EXPAND(). The framing and the variable-size integers are the
 * reference's, because they are the format.
 */
using System;
using System.Buffers.Binary;
using System.Globalization;

using H2Sharp.Api;
using H2Sharp.Compression;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Tools;

/// <summary>
/// A tool to losslessly compress data, and expand the compressed data again.
/// </summary>
public sealed class CompressTool
{
    private const int MaxBufferSize = 3 * Constants.IoBufferSizeCompress;

    private byte[]? buffer;

    private CompressTool()
    {
        // don't allow construction
    }

    private byte[] GetBuffer(int min)
    {
        if (min > MaxBufferSize)
        {
            return Utils.NewBytes(min);
        }

        if (buffer == null || buffer.Length < min)
        {
            buffer = Utils.NewBytes(min);
        }

        return buffer;
    }

    /// <summary>
    /// Get a new instance. Each instance uses a separate buffer.
    /// </summary>
    /// <returns>a new instance</returns>
    public static CompressTool GetInstance() => new();

    /// <summary>
    /// Compressed the data using the specified algorithm. If no algorithm is supplied, LZF is used.
    /// </summary>
    /// <param name="input">the byte array with the original data</param>
    /// <param name="algorithm">the algorithm (LZF, DEFLATE), or null</param>
    /// <returns>the compressed data</returns>
    public byte[] Compress(byte[] input, string? algorithm)
    {
        int len = input.Length;
        if (input.Length < 5)
        {
            algorithm = "NO";
        }

        Compressor compressor = GetCompressor(algorithm);
        byte[] buff = GetBuffer((len < 100 ? len + 100 : len) * 2);
        int newLen = Compress(input, input.Length, compressor, buff);
        return Utils.CopyBytes(buff, newLen);
    }

    private static int Compress(byte[] input, int len, Compressor compressor, byte[] output)
    {
        output[0] = (byte)compressor.GetAlgorithm();
        int start = 1 + WriteVariableInt(output, 1, len);
        int newLen = compressor.Compress(input, 0, len, output, start);
        if (newLen > len + start || newLen <= 0)
        {
            output[0] = Compressors.No;
            Array.Copy(input, 0, output, start, len);
            newLen = len + start;
        }

        return newLen;
    }

    /// <summary>
    /// Expands the compressed data.
    /// </summary>
    /// <param name="input">the compressed data</param>
    /// <returns>the uncompressed data</returns>
    public byte[] Expand(byte[] input)
    {
        if (input.Length == 0)
        {
            throw DbException.Get(ErrorCode.CompressionError);
        }

        int algorithm = input[0];
        Compressor compressor = GetCompressor(algorithm);
        try
        {
            int len = ReadVariableInt(input, 1);
            int start = 1 + GetVariableIntLength(len);
            byte[] buff = Utils.NewBytes(len);
            compressor.Expand(input, start, input.Length - start, buff, 0, len);
            return buff;
        }
        catch (Exception e)
        {
            throw DbException.Get(ErrorCode.CompressionError, e);
        }
    }

    /// <summary>
    /// INTERNAL
    /// </summary>
    /// <param name="input">compressed data</param>
    /// <param name="output">uncompressed result</param>
    /// <param name="outPos">the offset in the output array</param>
    public static void Expand(byte[] input, byte[] output, int outPos)
    {
        int algorithm = input[0];
        Compressor compressor = GetCompressor(algorithm);
        try
        {
            int len = ReadVariableInt(input, 1);
            int start = 1 + GetVariableIntLength(len);
            compressor.Expand(input, start, input.Length - start, output, outPos, len);
        }
        catch (Exception e)
        {
            throw DbException.Get(ErrorCode.CompressionError, e);
        }
    }

    /// <summary>
    /// Read a variable size integer using Rice coding.
    /// </summary>
    /// <param name="buff">the buffer</param>
    /// <param name="pos">the position</param>
    /// <returns>the integer</returns>
    public static int ReadVariableInt(byte[] buff, int pos)
    {
        int x = buff[pos++] & 0xff;
        if (x < 0x80)
        {
            return x;
        }

        if (x < 0xc0)
        {
            return ((x & 0x3f) << 8) + (buff[pos] & 0xff);
        }

        if (x < 0xe0)
        {
            return ((x & 0x1f) << 16) + ((buff[pos++] & 0xff) << 8) + (buff[pos] & 0xff);
        }

        if (x < 0xf0)
        {
            return ((x & 0xf) << 24) + ((buff[pos++] & 0xff) << 16) + ((buff[pos++] & 0xff) << 8)
                + (buff[pos] & 0xff);
        }

        return BinaryPrimitives.ReadInt32BigEndian(buff.AsSpan(pos));
    }

    /// <summary>
    /// Write a variable size integer using Rice coding. Negative values need 5 bytes.
    /// </summary>
    /// <param name="buff">the buffer</param>
    /// <param name="pos">the position</param>
    /// <param name="x">the value</param>
    /// <returns>the number of bytes written (0-5)</returns>
    public static int WriteVariableInt(byte[] buff, int pos, int x)
    {
        if (x < 0)
        {
            buff[pos++] = 0xf0;
            BinaryPrimitives.WriteInt32BigEndian(buff.AsSpan(pos), x);
            return 5;
        }
        else if (x < 0x80)
        {
            buff[pos] = (byte)x;
            return 1;
        }
        else if (x < 0x4000)
        {
            buff[pos++] = (byte)(0x80 | (x >> 8));
            buff[pos] = (byte)x;
            return 2;
        }
        else if (x < 0x20_0000)
        {
            buff[pos++] = (byte)(0xc0 | (x >> 16));
            buff[pos++] = (byte)(x >> 8);
            buff[pos] = (byte)x;
            return 3;
        }
        else if (x < 0x1000_0000)
        {
            BinaryPrimitives.WriteInt32BigEndian(buff.AsSpan(pos), x | unchecked((int)0xe000_0000));
            return 4;
        }
        else
        {
            buff[pos++] = 0xf0;
            BinaryPrimitives.WriteInt32BigEndian(buff.AsSpan(pos), x);
            return 5;
        }
    }

    /// <summary>
    /// Get a variable size integer length using Rice coding. Negative values need 5 bytes.
    /// </summary>
    /// <param name="x">the value</param>
    /// <returns>the number of bytes needed (0-5)</returns>
    public static int GetVariableIntLength(int x)
    {
        if (x < 0)
        {
            return 5;
        }
        else if (x < 0x80)
        {
            return 1;
        }
        else if (x < 0x4000)
        {
            return 2;
        }
        else if (x < 0x20_0000)
        {
            return 3;
        }
        else if (x < 0x1000_0000)
        {
            return 4;
        }
        else
        {
            return 5;
        }
    }

    private static Compressor GetCompressor(string? algorithm)
    {
        if (algorithm == null)
        {
            algorithm = "LZF";
        }

        int idx = algorithm.IndexOf(' ');
        string? options = null;
        if (idx > 0)
        {
            options = algorithm[(idx + 1)..];
            algorithm = algorithm[..idx];
        }

        int a = GetCompressAlgorithm(algorithm);
        Compressor compressor = GetCompressor(a);
        compressor.SetOptions(options!);
        return compressor;
    }

    /// <summary>
    /// INTERNAL
    /// </summary>
    /// <param name="algorithm">to translate into index</param>
    /// <returns>index of the specified algorithm</returns>
    private static int GetCompressAlgorithm(string algorithm)
    {
        algorithm = StringUtils.ToUpperEnglish(algorithm);
        if ("NO".Equals(algorithm, StringComparison.Ordinal))
        {
            return Compressors.No;
        }
        else if ("LZF".Equals(algorithm, StringComparison.Ordinal))
        {
            return Compressors.LZF;
        }
        else if ("DEFLATE".Equals(algorithm, StringComparison.Ordinal))
        {
            return Compressors.Deflate;
        }
        else
        {
            throw DbException.Get(ErrorCode.UnsupportedCompressionAlgorithm1, algorithm);
        }
    }

    private static Compressor GetCompressor(int algorithm)
    {
        switch (algorithm)
        {
            case Compressors.No:
                return new CompressNo();
            case Compressors.LZF:
                return new CompressLZF();
            case Compressors.Deflate:
                return new CompressDeflate();
            default:
                throw DbException.Get(
                    ErrorCode.UnsupportedCompressionAlgorithm1,
                    algorithm.ToString(CultureInfo.InvariantCulture));
        }
    }
}
