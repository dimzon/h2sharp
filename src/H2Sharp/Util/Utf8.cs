/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * UTF-8 byte counting without decoding. The count is in UTF-16 code units, which is what
 * string.Length and Java String.length() answer: one unit per code point, plus one more
 * per four-byte sequence (surrogate pair).
 */
using System;

namespace H2Sharp.Util;

internal static class Utf8
{
    /// <summary>
    /// The number of UTF-16 code units the bytes decode to.
    /// </summary>
    /// <param name="bytes">well-formed UTF-8</param>
    /// <returns>the code unit count</returns>
    public static int CodeUnitCount(ReadOnlySpan<byte> bytes)
    {
        int units = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            if ((b & 0xC0) != 0x80)
            {
                // The start of a code point; a four-byte sequence is one code point but two
                // UTF-16 units.
                units++;
                if ((b & 0xF8) == 0xF0)
                {
                    units++;
                }
            }
        }

        return units;
    }
}
