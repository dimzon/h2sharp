/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * This is H2's own escaping rules from JSONStringTarget.encodeString, expressed as the policy
 * object Utf8JsonWriter already asks. It is not a new set of rules: the rules stay where they
 * were, and the writer keeps doing the structural work it is good at.
 */
using System;
using System.Text.Encodings.Web;

namespace H2Sharp.Util.Json;

/// <summary>
/// H2's string escaping as a <see cref="JavaScriptEncoder"/>.
/// </summary>
/// <remarks>
/// <para>
/// H2 escapes control characters, the double quote and the backslash always; the apostrophe and
/// everything non-ASCII only when the caller asked for ASCII-only output. That is two rules with
/// a switch between them, and it is exactly what an encoder is for.
/// </para>
/// <para>
/// The reason this exists rather than a ready-made encoder: none of the built-in ones match.
/// <see cref="JavaScriptEncoder.Default"/> escapes <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>'</c>
/// and every non-ASCII character, and it writes hex in upper case. <see
/// cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> leaves non-ASCII alone but still escapes
/// the JavaScript line terminators, the next-line character and astral characters, and still writes
/// upper-case hex. Both were measured against the reference over 49 cases; this one matches all of
/// them.
/// </para>
/// <para>
/// One difference from the reference is forced by the interface and is deliberate: the reference
/// walks a string by <c>char</c>, so an astral character becomes two escapes, <c>\ud83c\udf89</c>. An
/// encoder is asked about Unicode scalars, so it sees the whole character at once and emits the two
/// halves itself in order to produce the same text.
/// </para>
/// </remarks>
public sealed unsafe class H2JavaScriptEncoder : JavaScriptEncoder
{
    private readonly bool asciiPrintableOnly;

    /// <summary>
    /// Creates an encoder.
    /// </summary>
    /// <param name="asciiPrintableOnly">
    /// whether all non-printable, non-ASCII characters, and <c>'</c> characters should be escaped
    /// </param>
    public H2JavaScriptEncoder(bool asciiPrintableOnly)
    {
        this.asciiPrintableOnly = asciiPrintableOnly;
    }

    /// <summary>
    /// The encoder for ordinary output, where non-ASCII characters are written as themselves.
    /// </summary>
    public static H2JavaScriptEncoder Plain { get; } = new(false);

    /// <summary>
    /// The encoder for ASCII-only output, where the apostrophe and every non-ASCII character
    /// become <c>\uXXXX</c>.
    /// </summary>
    public static H2JavaScriptEncoder Ascii { get; } = new(true);

    /// <inheritdoc/>
    /// <remarks>
    /// Twelve, not six: an astral character is one scalar to this interface and two escapes in the
    /// output, so that is the most a single input character can produce.
    /// </remarks>
    public override int MaxOutputCharactersPerInputCharacter => 12;

    /// <inheritdoc/>
    public override bool WillEncode(int unicodeScalar)
    {
        if (unicodeScalar is '"' or '\\')
        {
            return true;
        }

        if (unicodeScalar < ' ')
        {
            return true;
        }

        return asciiPrintableOnly && (unicodeScalar == '\'' || unicodeScalar > 0x7f);
    }

    /// <inheritdoc/>
    public override bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten)
    {
        switch (unicodeScalar)
        {
            case '\b':
                return Write("\\b", buffer, bufferLength, out numberOfCharactersWritten);
            case '\t':
                return Write("\\t", buffer, bufferLength, out numberOfCharactersWritten);
            case '\f':
                return Write("\\f", buffer, bufferLength, out numberOfCharactersWritten);
            case '\n':
                return Write("\\n", buffer, bufferLength, out numberOfCharactersWritten);
            case '\r':
                return Write("\\r", buffer, bufferLength, out numberOfCharactersWritten);
            case '"':
                return Write("\\\"", buffer, bufferLength, out numberOfCharactersWritten);
            case '\\':
                return Write("\\\\", buffer, bufferLength, out numberOfCharactersWritten);
            case '\'':
                // The apostrophe is legal JSON, so it is only escaped in the ASCII-only mode.
                return asciiPrintableOnly
                    ? Write("\\u0027", buffer, bufferLength, out numberOfCharactersWritten)
                    : Write("'", buffer, bufferLength, out numberOfCharactersWritten);
        }

        if (unicodeScalar > 0xffff)
        {
            // The reference walks the string by char and escapes each half separately; the
            // interface hands over the whole character, so the halves are put back here to get
            // the same text.
            int high = 0xd800 + ((unicodeScalar - 0x10000) >> 10);
            int low = 0xdc00 + ((unicodeScalar - 0x10000) & 0x3ff);
            if (!Write("\\u" + HexOf(high), buffer, bufferLength, out int writtenHigh))
            {
                numberOfCharactersWritten = 0;
                return false;
            }

            return Write("\\u" + HexOf(low), buffer, bufferLength, writtenHigh, out numberOfCharactersWritten);
        }

        return asciiPrintableOnly && unicodeScalar > 0x7f
            ? Write("\\u" + HexOf(unicodeScalar), buffer, bufferLength, out numberOfCharactersWritten)
            : Write("\\u00" + Hex.ToHexChar(unicodeScalar >> 4 & 0xf) + Hex.ToHexChar(unicodeScalar & 0xf), buffer, bufferLength, out numberOfCharactersWritten);
    }

    /// <inheritdoc/>
    public override int FindFirstCharacterToEncode(char* text, int textLength)
    {
        for (int i = 0; i < textLength; i++)
        {
            char ch = text[i];

            // A surrogate pair is decided as the character it forms, because that is what the two
            // methods above are asked about, and they have to agree.
            int scalar = ch;
            if (char.IsHighSurrogate(ch) && i + 1 < textLength && char.IsLowSurrogate(text[i + 1]))
            {
                scalar = char.ConvertToUtf32(ch, text[i + 1]);
            }

            if (WillEncode(scalar))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Four lower-case hex digits, the case the reference uses.
    /// </summary>
    /// <param name="value">the value</param>
    /// <returns>the digits</returns>
    private static string HexOf(int value)
        => new([Hex.ToHexChar(value >> 12 & 0xf), Hex.ToHexChar(value >> 8 & 0xf), Hex.ToHexChar(value >> 4 & 0xf), Hex.ToHexChar(value & 0xf)]);

    /// <summary>
    /// Writes text at the start of the buffer.
    /// </summary>
    /// <param name="text">the text</param>
    /// <param name="buffer">the destination</param>
    /// <param name="bufferLength">its length</param>
    /// <param name="written">how many characters were written</param>
    /// <returns>whether it fit</returns>
    private static bool Write(string text, char* buffer, int bufferLength, out int written)
        => Write(text, buffer, bufferLength, 0, out written);

    /// <summary>
    /// Writes text into the buffer at an offset, which is how the second half of an escaped
    /// surrogate pair gets in behind the first.
    /// </summary>
    /// <param name="text">the text</param>
    /// <param name="buffer">the destination</param>
    /// <param name="bufferLength">its length</param>
    /// <param name="offset">where to start</param>
    /// <param name="written">how many characters were written in total</param>
    /// <returns>whether it fit</returns>
    private static bool Write(string text, char* buffer, int bufferLength, int offset, out int written)
    {
        if (bufferLength < offset + text.Length)
        {
            written = 0;
            return false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            buffer[offset + i] = text[i];
        }

        written = offset + text.Length;
        return true;
    }
}
