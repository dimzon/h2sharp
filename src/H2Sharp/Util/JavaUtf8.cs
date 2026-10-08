/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * UTF-8 that answers the way Java's does.
 *
 * The two platforms disagree about malformed input in both directions, and a value that is
 * handed to the user as text has to disagree the same way. Measured against the reference:
 *
 * - Decoding: Java emits one U+FFFD per maximal invalid subpart, and .NET emits one per byte.
 *   For the three bytes of a surrogate encoding Java prints a single U+FFFD and .NET prints
 *   three, so the same bytes render as different text.
 * - Encoding: Java replaces an unpaired surrogate with '?' (0x3F), and .NET writes the three
 *   bytes of U+FFFD instead. A string holding a lone surrogate therefore has different bytes.
 *
 * The decoding rule was measured rather than assumed, because it is not the one Unicode
 * recommends and the difference is visible. Where a sequence goes wrong the reference normally
 * consumes the well formed prefix and puts one replacement character there: E1 80 41 decodes to
 * one U+FFFD and then an 'A', and E0 80 decodes to two, because E0 on its own is already invalid
 * and the 80 starts a new one. The exception is a surrogate written as three bytes: there the
 * reference reads the whole sequence before deciding, so ED A0 80 is one U+FFFD while ED A0 41 is
 * one U+FFFD followed by an 'A', having consumed only the two bytes it managed to read.
 */
using System;
using System.Buffers;
using System.Text;

namespace H2Sharp.Util;

/// <summary>
/// UTF-8 with the reference's answers for input that is not valid UTF-8.
/// </summary>
public static class JavaUtf8
{
    /// <summary>
    /// The character the reference puts in place of text it cannot decode.
    /// </summary>
    private const char Replacement = '�';

    /// <summary>
    /// The byte the reference puts in place of a character it cannot encode.
    /// </summary>
    private const byte ReplacementByte = (byte)'?';

    /// <summary>
    /// Decodes bytes as UTF-8, the way the reference does.
    /// </summary>
    /// <param name="value">the bytes</param>
    /// <returns>the decoded text, with a replacement wherever the input is malformed</returns>
    public static string GetString(byte[] value)
    {
        int length = value.Length;

        // Everything up to the first byte that needs a decision is plain ASCII, and the platform
        // decoder does that part in one vectorised pass. Worth doing: a byte array is usually a
        // string, and the whole point of this class is the rare part.
        int ascii = 0;
        while (ascii < length && value[ascii] < 0x80)
        {
            ascii++;
        }

        if (ascii == length)
        {
            return length == 0 ? string.Empty : Encoding.ASCII.GetString(value, 0, length);
        }

        // One character per byte is an upper bound: a byte that becomes a replacement character
        // becomes exactly one, and a four byte sequence becomes two characters for four bytes. So
        // the buffer is rented at the size of the input, filled without growing, and the result
        // is the only object this allocates. A StringBuilder would have cost the result plus its
        // own chunks, which measured 416 bytes against 152 for a 64 byte array with one two byte
        // character in it.
        char[] destination = ArrayPool<char>.Shared.Rent(length);
        try
        {
            return new string(destination, 0, Decode(value, destination));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(destination);
        }
    }

    /// <summary>
    /// Decodes into a buffer that is known to be long enough, and returns how much of it was
    /// used.
    /// </summary>
    private static int Decode(byte[] value, char[] destination)
    {
        int length = value.Length;
        int n = 0;
        int ascii = 0;
        while (ascii < length && value[ascii] < 0x80)
        {
            ascii++;
        }

        Encoding.ASCII.GetChars(value, 0, ascii, destination, 0);
        n = ascii;
        int i = ascii;
        while (i < length)
        {
            byte lead = value[i];
            if (lead < 0x80)
            {
                destination[n++] = (char)lead;
                i++;
                continue;
            }

            int sequence = SequenceLength(lead, out byte firstLow, out byte firstHigh, out bool readWhole);
            if (sequence == 0)
            {
                // A byte that cannot start anything: 0x80 to 0xC1 and 0xF5 to 0xFF.
                destination[n++] = Replacement;
                i++;
                continue;
            }

            if (i + 1 >= length)
            {
                // Nothing follows the lead byte at all.
                destination[n++] = Replacement;
                i++;
                continue;
            }

            byte first = value[i + 1];
            if (first < firstLow || first > firstHigh)
            {
                // The first byte of the sequence is out of the range the lead byte allows. For an
                // overlong form and for a code point above the last one the reference gives up on
                // the lead byte alone, so the byte after it starts a sequence of its own; for a
                // surrogate it has to read the rest before it can tell.
                if (!readWhole)
                {
                    destination[n++] = Replacement;
                    i++;
                    continue;
                }
            }

            // Take the sequence as far as it goes. Where it stops is one replacement character,
            // and decoding picks up after the bytes that were read.
            int consumed = 2;
            bool complete = true;
            for (int k = 2; k < sequence; k++)
            {
                if (i + k >= length)
                {
                    complete = false;
                    break;
                }

                byte c = value[i + k];
                if (c < 0x80 || c > 0xBF)
                {
                    complete = false;
                    break;
                }

                consumed++;
            }

            if (!complete)
            {
                destination[n++] = Replacement;
                i += consumed;
                continue;
            }

            int codePoint = lead & (0x7F >> sequence);
            for (int k = 1; k < sequence; k++)
            {
                codePoint = (codePoint << 6) | (value[i + k] & 0x3F);
            }

            if (codePoint is >= 0xD800 and <= 0xDFFF)
            {
                // A surrogate is well shaped and still not text: the whole sequence goes, and it
                // is one replacement character rather than one per byte.
                destination[n++] = Replacement;
                i += sequence;
                continue;
            }

            if (codePoint <= 0xFFFF)
            {
                destination[n++] = (char)codePoint;
            }
            else
            {
                int rest = codePoint - 0x10000;
                destination[n++] = (char)(0xD800 + (rest >> 10));
                destination[n++] = (char)(0xDC00 + (rest & 0x3FF));
            }

            i += sequence;
        }

        return n;
    }

    /// <summary>
    /// Encodes text as UTF-8, the way the reference does.
    /// </summary>
    /// <remarks>
    /// Every character needs at most three bytes and a surrogate pair, which is two characters,
    /// needs four, so the counting pass cannot be wrong and the result is exactly as long as it
    /// has to be. A surrogate that has no partner is not text that can be encoded, and the
    /// reference writes a question mark where .NET would write the replacement character.
    /// </remarks>
    /// <param name="value">the text</param>
    /// <returns>the encoded bytes</returns>
    public static byte[] GetBytes(string value)
    {
        int length = value.Length;
        int needed = 0;
        for (int i = 0; i < length; i++)
        {
            char c = value[i];
            if (c < 0x80)
            {
                needed++;
            }
            else if (c < 0x800)
            {
                needed += 2;
            }
            else if (IsPair(value, i))
            {
                needed += 4;
                i++;
            }
            else
            {
                needed += char.IsSurrogate(c) ? 1 : 3;
            }
        }

        byte[] result = new byte[needed];
        int o = 0;
        for (int i = 0; i < length; i++)
        {
            char c = value[i];
            if (c < 0x80)
            {
                result[o++] = (byte)c;
            }
            else if (c < 0x800)
            {
                result[o++] = (byte)(0xC0 | (c >> 6));
                result[o++] = (byte)(0x80 | (c & 0x3F));
            }
            else if (IsPair(value, i))
            {
                int cp = 0x10000 + ((c - 0xD800) << 10) + (value[i + 1] - 0xDC00);
                result[o++] = (byte)(0xF0 | (cp >> 18));
                result[o++] = (byte)(0x80 | ((cp >> 12) & 0x3F));
                result[o++] = (byte)(0x80 | ((cp >> 6) & 0x3F));
                result[o++] = (byte)(0x80 | (cp & 0x3F));
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                result[o++] = ReplacementByte;
            }
            else
            {
                result[o++] = (byte)(0xE0 | (c >> 12));
                result[o++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                result[o++] = (byte)(0x80 | (c & 0x3F));
            }
        }

        return result;
    }

    /// <summary>
    /// Whether the character at this position is a high surrogate with its partner next.
    /// </summary>
    private static bool IsPair(string value, int i)
        => char.IsHighSurrogate(value[i])
            && i + 1 < value.Length
            && char.IsLowSurrogate(value[i + 1]);

    /// <summary>
    /// The number of bytes a sequence starting with this lead byte occupies, or 0 if the byte
    /// cannot start one.
    /// </summary>
    /// <param name="lead">the leading byte</param>
    /// <param name="firstLow">the lowest byte allowed after it</param>
    /// <param name="firstHigh">the highest byte allowed after it</param>
    /// <param name="readWhole">
    /// whether the reference reads the rest of the sequence before rejecting it, which it does
    /// for a surrogate and not for the other two restricted cases
    /// </param>
    /// <returns>the length of the sequence, or 0</returns>
    private static int SequenceLength(byte lead, out byte firstLow, out byte firstHigh, out bool readWhole)
    {
        switch (lead)
        {
            case >= 0xC2 and <= 0xDF:
                firstLow = 0x80;
                firstHigh = 0xBF;
                readWhole = false;
                return 2;
            case 0xE0:
                firstLow = 0xA0;
                firstHigh = 0xBF;
                readWhole = false;
                return 3;
            case >= 0xE1 and <= 0xEC:
            case >= 0xEE and <= 0xEF:
                firstLow = 0x80;
                firstHigh = 0xBF;
                readWhole = false;
                return 3;
            case 0xED:
                // The first byte here is not restricted: ED 80 is an ordinary character and ED A0
                // is a surrogate, and the difference is only visible once the third byte is read.
                firstLow = 0x80;
                firstHigh = 0xBF;
                readWhole = true;
                return 3;
            case 0xF0:
                firstLow = 0x90;
                firstHigh = 0xBF;
                readWhole = false;
                return 4;
            case >= 0xF1 and <= 0xF3:
                firstLow = 0x80;
                firstHigh = 0xBF;
                readWhole = false;
                return 4;
            case 0xF4:
                firstLow = 0x80;
                firstHigh = 0x8F;
                readWhole = false;
                return 4;
            default:
                firstLow = 0;
                firstHigh = 0;
                readWhole = false;
                return 0;
        }
    }
}
