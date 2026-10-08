/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.util.MathUtils.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Modified: SecureRandom -> RandomNumberGenerator (the Java SHA1PRNG background-seed
 * workarounds have no .NET equivalent and are dropped); ThreadLocalRandom ->
 * Random.Shared; Integer.numberOfLeadingZeros -> BitOperations.LeadingZeroCount.
 */
using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;

namespace H2Sharp.Util;

/// <summary>
/// This is a utility class with mathematical helper functions.
/// </summary>
public static class MathUtils
{
    private static readonly object SecureRandomLock = new();
    private static RandomNumberGenerator? secureRandom;

    /// <summary>
    /// Round the value up to the next block size. The block size must be a power of two.
    /// As an example, using the block size of 8, the following rounding operations are done:
    /// 0 stays 0; values 1..8 results in 8, 9..16 results in 16, and so on.
    /// </summary>
    /// <param name="x">the value to be rounded</param>
    /// <param name="blockSizePowerOf2">the block size</param>
    /// <returns>the rounded value</returns>
    public static int RoundUpInt(int x, int blockSizePowerOf2)
        => (x + blockSizePowerOf2 - 1) & -blockSizePowerOf2;

    /// <summary>
    /// Round the value up to the next block size. The block size must be a power of two.
    /// As an example, using the block size of 8, the following rounding operations are done:
    /// 0 stays 0; values 1..8 results in 8, 9..16 results in 16, and so on.
    /// </summary>
    /// <param name="x">the value to be rounded</param>
    /// <param name="blockSizePowerOf2">the block size</param>
    /// <returns>the rounded value</returns>
    public static long RoundUpLong(long x, long blockSizePowerOf2)
        => (x + blockSizePowerOf2 - 1) & -blockSizePowerOf2;

    private static RandomNumberGenerator GetSecureRandom()
    {
        if (secureRandom is not null)
        {
            return secureRandom;
        }

        lock (SecureRandomLock)
        {
            // .NET instantiates the OS entropy source lazily on first use, so the Java
            // "generate a seed on a background thread" workaround is unnecessary.
            secureRandom ??= RandomNumberGenerator.Create();
            return secureRandom;
        }
    }

    /// <summary>
    /// Generate a seed value, using as much unpredictable data as possible.
    /// </summary>
    /// <returns>the seed</returns>
    public static byte[] GenerateAlternativeSeed()
    {
        using MemoryStream stream = new();

        // milliseconds and nanoseconds
        WriteInt64(stream, DateTimeOffset.UtcNow.UtcTicks * 100L);
        WriteInt64(stream, (long)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency)));

        // memory
        WriteInt32(stream, Environment.CurrentManagedThreadId);
        GCMemoryInfo memoryInfo = GC.GetGCMemoryInfo();
        WriteInt64(stream, GC.GetTotalMemory(forceFullCollection: false));
        WriteInt64(stream, memoryInfo.TotalAvailableMemoryBytes);
        WriteInt64(stream, memoryInfo.HeapSizeBytes);

        // environment
        try
        {
            string s = string.Join(
                "\n",
                Environment.GetEnvironmentVariables().Keys.Cast<object?>()
                    .Select(k => k + "=")
                    .OrderBy(k => k, StringComparer.Ordinal));
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s);
            WriteInt32(stream, s.Length);
            stream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception e)
        {
            Warn("GenerateAlternativeSeed", e);
        }

        // host name and ip addresses (if any)
        try
        {
            string hostName = Dns.GetHostName();
            byte[] hostBytes = System.Text.Encoding.UTF8.GetBytes(hostName);
            stream.Write(hostBytes, 0, hostBytes.Length);
            foreach (IPAddress address in Dns.GetHostAddresses(hostName))
            {
                byte[] addressBytes = address.GetAddressBytes();
                stream.Write(addressBytes, 0, addressBytes.Length);
            }
        }
        catch
        {
            // on some systems, name resolution is not available
        }

        // timing
        for (int j = 0; j < 16; j++)
        {
            int i = 0;
            long end = DateTime.UtcNow.Ticks;
            while (end == DateTime.UtcNow.Ticks)
            {
                i++;
            }

            WriteInt32(stream, i);
        }

        return stream.ToArray();
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>
    /// Print a message to system output if there was a problem initializing the random
    /// number generator.
    /// </summary>
    /// <param name="s">the message to print</param>
    /// <param name="t">the exception</param>
    internal static void Warn(string s, Exception? t)
    {
        // not a fatal problem, but maybe reduced security
        Console.WriteLine("Warning: " + s);
        if (t is not null)
        {
            Console.WriteLine(t);
        }
    }

    /// <summary>
    /// Get the value that is equal to or higher than this value, and that is a power of two.
    /// </summary>
    /// <param name="x">the original value</param>
    /// <returns>the next power of two value</returns>
    /// <exception cref="ArgumentException">if x is negative or x &gt; 0x40000000</exception>
    public static int NextPowerOf2(int x)
    {
        // Java: x + Integer.MIN_VALUE > (0x4000_0000 + Integer.MIN_VALUE) rejects x > 0x40000000
        if (x > 0x40000000)
        {
            throw new ArgumentException(
                "Argument out of range [0x0-0x40000000]. Argument was: " + x);
        }

        return x <= 1 ? 1 : unchecked((int)(~0u >> BitOperations.LeadingZeroCount((uint)(x - 1))) + 1);
    }

    /// <summary>
    /// Convert a long value to an int value. Values larger than the biggest int value are
    /// converted to the biggest int value, and values smaller than the smallest int value
    /// are converted to the smallest int value.
    /// </summary>
    /// <param name="l">the value to convert</param>
    /// <returns>the converted int value</returns>
    public static int ConvertLongToInt(long l)
    {
        if (l <= int.MinValue)
        {
            return int.MinValue;
        }
        else if (l >= int.MaxValue)
        {
            return int.MaxValue;
        }
        else
        {
            return (int)l;
        }
    }

    /// <summary>
    /// Convert an int value to a short value. Values larger than the biggest short value are
    /// converted to the biggest short value, and values smaller than the smallest short value
    /// are converted to the smallest short value.
    /// </summary>
    /// <param name="i">the value to convert</param>
    /// <returns>the converted short value</returns>
    public static short ConvertIntToShort(int i)
    {
        if (i <= short.MinValue)
        {
            return short.MinValue;
        }
        else if (i >= short.MaxValue)
        {
            return short.MaxValue;
        }
        else
        {
            return (short)i;
        }
    }

    /// <summary>
    /// Get a cryptographically secure pseudo random long value.
    /// </summary>
    /// <returns>the random long value</returns>
    public static long SecureRandomLong()
    {
        Span<byte> buffer = stackalloc byte[8];
        RandomNumberGenerator.Fill(buffer);
        return BinaryPrimitives.ReadInt64BigEndian(buffer);
    }

    /// <summary>
    /// Get a number of pseudo random bytes.
    /// </summary>
    /// <param name="bytes">the target array</param>
    public static void RandomBytes(byte[] bytes) => Random.Shared.NextBytes(bytes);

    /// <summary>
    /// Get a number of cryptographically secure pseudo random bytes.
    /// </summary>
    /// <param name="len">the number of bytes</param>
    /// <returns>the random bytes</returns>
    public static byte[] SecureRandomBytes(int len)
    {
        if (len <= 0)
        {
            len = 1;
        }

        byte[] buff = new byte[len];
        RandomNumberGenerator.Fill(buff);
        return buff;
    }

    /// <summary>
    /// Get a pseudo random int value between 0 (including) and the given value (excluding).
    /// The value is not cryptographically secure.
    /// </summary>
    /// <param name="lowerThan">the value returned will be lower than this value</param>
    /// <returns>the random long value</returns>
    public static int RandomInt(int lowerThan) => Random.Shared.Next(lowerThan);

    /// <summary>
    /// Get a cryptographically secure pseudo random int value between 0 (including) and the
    /// given value (excluding).
    /// </summary>
    /// <param name="lowerThan">the value returned will be lower than this value</param>
    /// <returns>the random long value</returns>
    public static int SecureRandomInt(int lowerThan)
        => (int)(SecureRandomLong() & 0x7FFFFFFFFFFFFFFF) % lowerThan;
}
