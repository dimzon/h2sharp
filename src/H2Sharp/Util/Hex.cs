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
using System.Text;

namespace H2Sharp.Util;

/// <summary>
/// Hexadecimal formatting helpers.
/// </summary>
public static class Hex
{
    /// <summary>
    /// The lowercase hex digits, matching H2's <c>HEX</c> table.
    /// </summary>
    private static readonly char[] HexDigits = "0123456789abcdef".ToCharArray();

    /// <summary>
    /// Appends a lowercase hex representation of the given bytes.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">bytes to append; each byte is treated as unsigned</param>
    /// <returns>the specified string builder</returns>
    public static StringBuilder AppendBytesHex(StringBuilder builder, ReadOnlySpan<byte> value)
    {
        foreach (byte b in value)
        {
            builder.Append(HexDigits[b >> 4]).Append(HexDigits[b & 0xF]);
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
        for (int i = bytes * 8; i > 0;)
        {
            i -= 4;
            builder.Append(HexDigits[(int)((x >> i) & 0xF)]);
            i -= 4;
            builder.Append(HexDigits[(int)((x >> i) & 0xF)]);
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
        StringBuilder builder = new(bytes.Length * 2);
        return AppendBytesHex(builder, bytes).ToString();
    }
}