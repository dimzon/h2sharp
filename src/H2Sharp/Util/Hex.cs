/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from the hex helpers of org.h2.util.StringUtils.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Modified: extracted from StringUtils so that the message catalogue (Message/ErrorMessages)
 * can hex-encode quoted parameters without depending on the rest of StringUtils.
 * StringUtils delegates here; do not re-add private copies.
 */
using System;
using System.Diagnostics;
using System.Text;

namespace H2Sharp.Util;

/// <summary>
/// Hexadecimal formatting helpers.
/// </summary>
public static class Hex
{
    /// <summary>
    /// The lowercase hex digits, matching H2's <c>HEX</c> table. A UTF-8 literal, so the
    /// lookup is a byte read with no array bounds machinery per character.
    /// </summary>
    private static ReadOnlySpan<byte> Digits => "0123456789abcdef"u8;

    /// <summary>
    /// The largest number of characters written by one stack buffer, that is 64 bytes.
    /// Chosen so the buffer stays inside the 128-byte stack budget that is safe to
    /// request in a method with other locals.
    /// </summary>
    private const int MaxStackChars = 128;

    /// <summary>
    /// Appends exactly four lowercase hex digits, i.e. one UTF-16 code unit in
    /// <c>\ u X X X X</c> escape form. This is the shape
    /// <c>StringUtils.appendHex(cp, 2)</c> produces and it is used by the Java literal
    /// encoder, so it is worth a single span append instead of two appends per digit.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">value to append; only the low 16 bits are used</param>
    /// <returns>the specified string builder</returns>
    public static StringBuilder AppendHex4(StringBuilder builder, int value)
    {
        Span<char> buffer = stackalloc char[4];
        buffer[0] = (char)Digits[(value >> 12) & 0xF];
        buffer[1] = (char)Digits[(value >> 8) & 0xF];
        buffer[2] = (char)Digits[(value >> 4) & 0xF];
        buffer[3] = (char)Digits[value & 0xF];
        return builder.Append(buffer);
    }

    /// <summary>
    /// Appends a lowercase hex representation of the given bytes.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">bytes to append; each byte is treated as unsigned</param>
    /// <returns>the specified string builder</returns>
    public static StringBuilder AppendBytesHex(StringBuilder builder, ReadOnlySpan<byte> value)
    {
        // Hex output is the hottest path in the whole engine (every value written to a
        // page goes through it), so it is written through a stack buffer in bulk rather
        // than two StringBuilder.Append(char) calls per byte. That removes the bounds
        // check and the virtual call from the inner loop and lets the builder grow once
        // per 64 bytes instead of once per byte.
        Span<char> buffer = stackalloc char[MaxStackChars];
        while (!value.IsEmpty)
        {
            int chunk = Math.Min(value.Length, MaxStackChars / 2);
            WriteBytesHex(buffer[..(chunk * 2)], value[..chunk]);
            builder.Append(buffer[..(chunk * 2)]);
            value = value[chunk..];
        }

        return builder;
    }

    /// <summary>
    /// Appends the specified number of trailing bytes of an unsigned long value to a
    /// specified string builder.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="x">value to append</param>
    /// <param name="bytes">number of bytes to append</param>
    /// <returns>the specified string builder</returns>
    public static StringBuilder AppendHex(StringBuilder builder, long x, int bytes)
    {
        int digits = bytes * 2;
        if (digits <= MaxStackChars)
        {
            Span<char> buffer = stackalloc char[MaxStackChars];
            WriteHex(buffer[..digits], x);
            return builder.Append(buffer[..digits]);
        }

        for (int i = bytes * 8; i > 0;)
        {
            i -= 4;
            builder.Append((char)Digits[(int)((x >> i) & 0xF)]);
            i -= 4;
            builder.Append((char)Digits[(int)((x >> i) & 0xF)]);
        }

        return builder;
    }

    /// <summary>
    /// Converts a byte array to a lowercase hex string.
    /// </summary>
    /// <param name="bytes">the bytes</param>
    /// <returns>the hex string</returns>
    public static string BytesToHex(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        // One allocation, the result string itself. The StringBuilder version this
        // replaces cost the builder, its internal char[] chunk and its own growth.
        int length = bytes.Length * 2;
        char[]? heap = length <= MaxStackChars ? null : new char[length];
        Span<char> chars = heap is null ? stackalloc char[MaxStackChars] : heap;

        WriteBytesHex(chars[..length], bytes);
        return new string(chars[..length]);
    }

    /// <summary>
    /// Converts a byte array to a lowercase hex string. Preferred over the span overload
    /// for large inputs: it fills the result string directly, so it allocates the string
    /// and nothing else, while the span overload needs a scratch buffer above
    /// <see cref="MaxStackChars"/> because a <c>ReadOnlySpan</c> cannot be used as the
    /// state of <see cref="string.Create{TState}"/>.
    /// </summary>
    /// <param name="bytes">the bytes</param>
    /// <returns>the hex string</returns>
    public static string BytesToHex(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        return string.Create(bytes.Length * 2, bytes, static (chars, source) =>
        {
            WriteBytesHex(chars, source);
        });
    }

    private static void WriteBytesHex(Span<char> destination, ReadOnlySpan<byte> source)
    {
        Debug.Assert(destination.Length == source.Length * 2);

        for (int i = 0; i < source.Length; i++)
        {
            byte b = source[i];
            destination[i * 2] = (char)Digits[b >> 4];
            destination[(i * 2) + 1] = (char)Digits[b & 0xF];
        }
    }

    private static void WriteHex(Span<char> destination, long x)
    {
        Debug.Assert(destination.Length % 2 == 0);

        for (int i = destination.Length - 1; i >= 0; i--)
        {
            destination[i] = (char)Digits[(int)(x & 0xF)];
            x >>= 4;
        }
    }
}
