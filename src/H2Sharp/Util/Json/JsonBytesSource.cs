/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.util.json.JSONBytesSource.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;

namespace H2Sharp.Util.Json;

/// <summary>
/// JSON byte array source: reads from bytes, guessing the encoding from the first few of them.
/// </summary>
/// <remarks>
/// <para>
/// The guessing is not a real encoding detector and should not be mistaken for one. It looks at
/// whether the leading bytes are zero, because that is what UTF-16 and UTF-32 look like from the
/// outside, and it commits on that alone: a document that happens to start with two zero bytes is
/// read as UTF-16 whatever it actually is. A byte order mark is honoured, a lone one is not required,
/// and the detection only ever runs on the first four bytes.
/// </para>
/// <para>
/// The consequence is worth stating plainly, because it decides where this class may be used. When a
/// charset is recognised the bytes are decoded and handed to <see cref="JsonStringSource"/> — which
/// means the whole UTF-8 validator, the overlong and continuation checks and all, are gone for that
/// path. Malformed UTF-16 does not fail; it comes back as replacement characters. That is the
/// reference behaviour and it is what this reproduces, but it also means a JSON column holding
/// UTF-16 can hold text that no longer round-trips, and the storage format is bytes, so the question
/// of who writes those bytes is not academic.
/// </para>
/// <para>
/// Without a recognised charset the bytes are validated as they are read, strictly, and that is the
/// path a UTF-8 document takes.
/// </para>
/// </remarks>
public sealed class JsonBytesSource : JsonTextSource
{
    /// <summary>
    /// Parses source bytes to a specified target.
    /// </summary>
    /// <typeparam name="R">the type of the result</typeparam>
    /// <param name="bytes">source</param>
    /// <param name="target">target</param>
    /// <returns>the result of the target</returns>
    public static R Parse<R>(byte[] bytes, JsonTarget<R> target)
    {
        Encoding? charset = DetectCharset(bytes);
        if (charset is null)
        {
            new JsonBytesSource(bytes, target).Parse();
        }
        else
        {
            new JsonStringSource(charset.GetString(bytes), target).ParseText();
        }

        return target.Result!;
    }

    /// <summary>
    /// Converts bytes into normalized JSON representation.
    /// </summary>
    /// <param name="bytes">source representation</param>
    /// <returns>normalized representation</returns>
    public static byte[] Normalize(byte[] bytes) => Parse(bytes, new JsonByteArrayTarget());

    /// <summary>
    /// UTF-32 in either order. .NET has no named instances for the two orders, and its
    /// <see cref="Encoding.GetEncoding(int)"/> codes for them are the other way round from what the
    /// names say, so both are built explicitly rather than looked up by number.
    /// </summary>
    private static readonly Encoding Utf32LittleEndian = new UnicodeEncoding(
        bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: false);

    private static readonly Encoding Utf32BigEndian = new UnicodeEncoding(
        bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: false);

    /// <summary>
    /// The encoding the first bytes suggest, or null when they suggest UTF-8, which is read and
    /// validated directly.
    /// </summary>
    /// <remarks>
    /// Translated from the reference's switch on a signed byte, where the interesting values are the
    /// negatives: 0xFE as a sign byte, and 0xFF followed by 0xFE. The rules, in the reference's order:
    /// a byte order mark is decisive; a leading zero with a non-zero next byte is UTF-16 big endian;
    /// zero-zero-nonzero is UTF-32 big endian, and 0xFE 0xFF there is its byte order mark; anything
    /// else with a zero second byte is UTF-16 or UTF-32 little endian. Two bytes are enough when
    /// there are only two.
    /// </remarks>
    /// <param name="bytes">the source</param>
    /// <returns>the encoding, or null</returns>
    private static Encoding? DetectCharset(byte[] bytes)
    {
        int length = bytes.Length;
        if (length >= 4)
        {
            byte b0 = bytes[0];
            byte b1 = bytes[1];
            byte b2 = bytes[2];
            byte b3 = bytes[3];
            if (b0 == 0xfe)
            {
                return b1 == 0xff ? Encoding.BigEndianUnicode : null;
            }

            if (b0 == 0xff)
            {
                if (b1 == 0xfe)
                {
                    return b2 == 0 && b3 == 0 ? Utf32LittleEndian : Encoding.Unicode;
                }

                return null;
            }

            if (b0 == 0)
            {
                if (b1 != 0)
                {
                    return Encoding.BigEndianUnicode;
                }

                return b2 == 0 && b3 != 0 || b2 == 0xfe && b3 == 0xff
                    ? Utf32BigEndian
                    : null;
            }

            if (b1 == 0)
            {
                return b2 == 0 && b3 == 0 ? Utf32LittleEndian : Encoding.Unicode;
            }

            return null;
        }

        if (length >= 2)
        {
            byte b0 = bytes[0];
            byte b1 = bytes[1];
            if (b0 != 0)
            {
                return b1 == 0 ? Encoding.Unicode : null;
            }

            return b1 != 0 ? Encoding.BigEndianUnicode : null;
        }

        return null;
    }

    private readonly byte[] bytes;

    private readonly int length;

    private int index;

    private JsonBytesSource(byte[] bytes, JsonTarget target)
        : base(target)
    {
        this.bytes = bytes;
        length = bytes.Length;

        // Ignore BOM. The reference reads the first character to see whether it is one, and puts
        // the index back if it was not; the same move here, for the same reason: an empty array
        // runs out of characters and that is an error, not a document of nothing.
        if (NextChar() != '\uFEFF')
        {
            index = 0;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A byte below zero is not whitespace and not the start of a UTF-8 sequence: outside a string it
    /// is an error. Inside a string it is the beginning of a multi-byte character, which
    /// <see cref="NextChar"/> assembles. Confusing the two cases here is how a document that is not
    /// UTF-8 at all would slip through.
    /// </remarks>
    protected override int NextCharAfterWhitespace()
    {
        int i = index;
        while (i < length)
        {
            byte ch = bytes[i++];
            switch (ch)
            {
                case (byte)'\t':
                case (byte)'\n':
                case (byte)'\r':
                case (byte)' ':
                    break;
                default:
                    if (ch < 0)
                    {
                        throw new ArgumentException();
                    }

                    index = i;
                    return ch;
            }
        }

        return EndOfInput;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The length is checked first here, unlike in the string source, where a span comparison simply
    /// fails on a short tail.
    /// </remarks>
    protected override void ReadKeyword1(string keyword)
    {
        int l = keyword.Length - 1;
        if (index + l > length)
        {
            throw new ArgumentException();
        }

        for (int i = index, j = 1; j <= l; i++, j++)
        {
            if (bytes[i] != keyword[j])
            {
                throw new ArgumentException();
            }
        }

        index += l;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The number is read back as Latin-1 rather than UTF-8, which is the same bytes for digits and
    /// is what keeps the decimal exact.
    /// </remarks>
    protected override void ParseNumber(bool positive)
    {
        int i = index;
        int start = i - 1;
        i = SkipInt(i, positive);
        if (i < length)
        {
            byte ch = bytes[i];
            if (ch == (byte)'.')
            {
                i = SkipInt(i + 1, false);
                if (i >= length)
                {
                    goto Done;
                }

                ch = bytes[i];
            }

            if (ch is (byte)'E' or (byte)'e')
            {
                if (++i >= length)
                {
                    throw new ArgumentException();
                }

                ch = bytes[i];
                if (ch is (byte)'+' or (byte)'-')
                {
                    i++;
                }

                i = SkipInt(i, false);
            }
        }

    Done:
        Target.ValueNumber(BigDecimal.Parse(
            Encoding.Latin1.GetString(bytes.AsSpan(start, i - start)),
            Encoding.Latin1.GetString(bytes.AsSpan(start, i - start))));
        index = i;
    }

    private int SkipInt(int index, bool hasInt)
    {
        while (index < length)
        {
            byte ch = bytes[index];
            if (ch is >= (byte)'0' and <= (byte)'9')
            {
                hasInt = true;
                index++;
            }
            else
            {
                break;
            }
        }

        if (!hasInt)
        {
            throw new ArgumentException();
        }

        return index;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// UTF-8 is assembled here, by hand, and checked: an overlong encoding, a continuation byte in
    /// the wrong place, a four-byte sequence that would encode something below U+10000, and one that
    /// would encode more than U+10FFFF are all rejected. A standard decoder would have to be told to
    /// throw, and the reference does not use one.
    /// </remarks>
    protected override int NextChar()
    {
        if (index >= length)
        {
            throw new ArgumentException();
        }

        int b1 = bytes[index++] & 0xff;
        if (b1 < 0x80)
        {
            return b1;
        }

        if (b1 < 0xe0)
        {
            if (index >= length)
            {
                throw new ArgumentException();
            }

            int b2 = bytes[index++] & 0xff;
            int value = ((b1 & 0x1f) << 6) + (b2 & 0x3f);
            if (value < 0x80 || (b2 & 0xc0) != 0x80)
            {
                throw new ArgumentException();
            }

            return value;
        }

        if (b1 < 0xf0)
        {
            if (index + 1 >= length)
            {
                throw new ArgumentException();
            }

            int b2 = bytes[index++] & 0xff;
            int b3 = bytes[index++] & 0xff;
            int value = ((b1 & 0x0f) << 12) + ((b2 & 0x3f) << 6) + (b3 & 0x3f);
            if (value < 0x800 || (b2 & 0xc0) != 0x80 || (b3 & 0xc0) != 0x80)
            {
                throw new ArgumentException();
            }

            return value;
        }

        if (index + 2 >= length)
        {
            throw new ArgumentException();
        }

        int c2 = bytes[index++] & 0xff;
        int c3 = bytes[index++] & 0xff;
        int c4 = bytes[index++] & 0xff;
        int scalar = ((b1 & 0x0f) << 18) + ((c2 & 0x3f) << 12) + ((c3 & 0x3f) << 6) + (c4 & 0x3f);
        if (scalar < 0x10000 || scalar > 0x10FFFF
            || (c2 & 0xc0) != 0x80 || (c3 & 0xc0) != 0x80 || (c4 & 0xc0) != 0x80)
        {
            throw new ArgumentException();
        }

        return scalar;
    }

    /// <inheritdoc/>
    protected override char ReadHex()
    {
        if (index + 3 >= length)
        {
            throw new ArgumentException();
        }

        if (!ushort.TryParse(
                Encoding.Latin1.GetString(bytes.AsSpan(index, 4)),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out ushort code))
        {
            throw new ArgumentException();
        }

        index += 4;
        return (char)code;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        // Decoded as UTF-8 on purpose, and lossy: this is what the reference does, and on bytes that
        // are not UTF-8 the diagnostic shows replacement characters rather than the truth. A test
        // that compared this output would be comparing the decoder's opinion, so none does.
        return Encoding.UTF8.GetString(bytes.AsSpan(0, index)) + "[*]"
            + Encoding.UTF8.GetString(bytes.AsSpan(index));
    }
}
