/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.CharsetCollator.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The charset collator compares the encoded bytes, so there are no platform
 * collation weights involved and the port is exact. Getting there took two
 * measurements of .NET's encoder, because both of its defaults differ from the
 * JDK's and both differences are silent.
 *
 * 1. Signed bytes. Java's Arrays.compare(byte[], byte[]) compares bytes as
 * signed values, and the corpus is full of bytes above 0x7F (any non-ASCII text
 * in UTF-8), so comparing them unsigned would invert the order of every
 * non-ASCII string against every ASCII one. The subtraction below is on sbyte.
 *
 * 2. Replacement. .NET does not replace an unencodable character with '?'. For
 * Latin-1 the default encoder fallback is a CLDR fold of the character onto its
 * ASCII lookalike: U+0130 encodes as 'I' (0x49), U+2019 as ''' (0x27) and
 * U+2122 as 'T' (0x54), where the JDK writes '?' for all three. Those are
 * plausible bytes, so a collation over ISO-8859-1 sorts non-Latin text by a
 * fold nobody asked for and nobody can see. And the replacement is counted per
 * UTF-16 code unit, where the JDK counts per code point, so an astral character
 * costs '?' twice on this side and once on the reference's.
 *
 * Both are handled below rather than configured away: the encoding is built with
 * an exception fallback so that "cannot be represented" is an answer instead of
 * a guess, and ToBytes walks code points, writing the JDK's single '?' for one
 * the encoding cannot represent. Encoding.GetEncoding(name, EncoderFallback,
 * DecoderFallback) already gives '?' and the code-point counting is the rest.
 *
 * Case folding is ToUpperEnglish, not ToUpperInvariant. Up to SECONDARY
 * strength the reference upper-cases with Locale.ROOT, which applies the full
 * Unicode mapping ("ß" becomes "SS"); the invariant upper case in .NET applies
 * the simple one and leaves "ß" alone. Util/CaseFolding.cs is generated from
 * the reference JVM, so ToUpperEnglish reproduces the ROOT mapping.
 *
 * Two encodings are deliberately absent from the corpus, not from the code.
 * "UTF-16" in Java encodes with a byte-order mark and .NET never emits one, so
 * the key bytes would differ while every comparison still agreed; the LE/BE
 * halves are tested instead. Everything else the corpus can name is in it,
 * unpaired surrogates included — they are in it because they are the input
 * that brings out the charset's own replacement.
 */
using System;
using System.Collections.Generic;
using System.Text;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// The charset collator sorts strings according to the order in the given charset.
/// </summary>
public sealed class CharsetCollator
{
    /// <summary>Same as java.text.Collator.PRIMARY.</summary>
    public const int Primary = 0;

    /// <summary>Same as java.text.Collator.SECONDARY.</summary>
    public const int Secondary = 1;

    /// <summary>Same as java.text.Collator.TERTIARY.</summary>
    public const int Tertiary = 2;

    /// <summary>Same as java.text.Collator.IDENTICAL.</summary>
    public const int Identical = 3;

    private readonly Encoding encoding;
    private readonly byte[] replacement;
    private int strength = Tertiary;

    /// <summary>U+FFFD, what the JDK's UTF-16 encoders replace an unencodable unit with.</summary>
    private const string ReplacementChar = "�";

    /// <summary>
    /// Resolves a charset name the way <c>Charset.forName</c> does, and then makes the
    /// encoding answer the question this class needs answered.
    /// </summary>
    /// <remarks>
    /// The exception fallback is deliberate and load-bearing: with .NET's own default the
    /// encoder invents a replacement instead of failing, and for Latin-1 the byte it invents
    /// is a fold onto an ASCII lookalike. Making failure the answer is what lets ToBytes tell
    /// "cannot be represented" from "happens to encode to the same byte".
    /// </remarks>
    /// <param name="charsetName">the charset name</param>
    /// <returns>the encoding</returns>
    private static Encoding GetEncoding(string charsetName)
    {
        return Encoding.GetEncoding(
            charsetName,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ReplacementFallback);
    }

    /// <summary>
    /// The bytes the reference writes for a character the charset cannot represent.
    /// </summary>
    /// <remarks>
    /// The replacement belongs to the charset, not to the failure, and the JDK keeps it in
    /// <c>Charset.newEncoder().replacement()</c>: '?' for the single-byte charsets and for
    /// UTF-8, and U+FFFD encoded for UTF-16, whose replacement is two bytes. Measured, not
    /// assumed — a lone surrogate is in the corpus, and it is the input that brings this out,
    /// because it is the one character no charset here can represent. .NET's
    /// ReplacementFallback is "?" for every built-in encoding, so the UTF-16 case is the one
    /// that has to be decided here.
    /// </remarks>
    private static byte[] GetReplacement(string charsetName, Encoding encoding)
    {
        return charsetName.StartsWith("UTF-16", StringComparison.OrdinalIgnoreCase)
            ? encoding.GetBytes(ReplacementChar)
            : [(byte)'?'];
    }

    /// <summary>
    /// Creates a collator for the named charset.
    /// </summary>
    /// <remarks>
    /// The reference is handed an already-resolved <c>Charset</c>; here the name is what a
    /// collation has and resolving it is this class's own first step, which is also where the
    /// replacement bytes come from — a name is the only place the UTF-16 special case is
    /// visible, since .NET's Encoding does not report its own JDK replacement.
    /// </remarks>
    /// <param name="charsetName">the charset name, as in <c>COLLATE CHARSET_...</c></param>
    public CharsetCollator(string charsetName)
    {
        encoding = GetEncoding(charsetName);
        replacement = GetReplacement(charsetName, encoding);
    }

    /// <summary>
    /// Gets the charset this collator orders by.
    /// </summary>
    public Encoding Charset => encoding;

    /// <summary>
    /// Gets or sets the collation strength, one of <see cref="Primary"/> to
    /// <see cref="Identical"/>. The reference hands H2's own strength straight
    /// into Collator.setStrength, so the numbers here are the JDK ones.
    /// </summary>
    public int Strength
    {
        get => strength;
        set
        {
            if (value < Primary || value > Identical)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            strength = value;
        }
    }

    /// <summary>
    /// Compares two strings by their encoded bytes.
    /// </summary>
    /// <param name="source">the first string</param>
    /// <param name="target">the second string</param>
    /// <returns>the same signed difference java.util.Arrays.compare returns</returns>
    public int Compare(string source, string target)
    {
        return CompareBytes(ToBytes(source), ToBytes(target));
    }

    /// <summary>
    /// Compares two character spans by their encoded bytes, without making strings out of
    /// them. This is what a per-character comparison goes through: two one-character
    /// substrings and a cache lookup per character used to stand here.
    /// </summary>
    /// <param name="source">the first characters</param>
    /// <param name="target">the second characters</param>
    /// <returns>the same signed difference java.util.Arrays.compare returns</returns>
    public int Compare(ReadOnlySpan<char> source, ReadOnlySpan<char> target)
    {
        return CompareBytes(ToBytesCore(Fold(source)), ToBytesCore(Fold(target)));
    }

    /// <summary>
    /// The case fold this strength compares with, as a span. A folded string temporary lives
    /// to the end of the calling expression, which is where the span is read.
    /// </summary>
    private ReadOnlySpan<char> Fold(ReadOnlySpan<char> source)
        => strength <= Secondary ? StringUtils.ToUpperEnglish(source.ToString()).AsSpan() : source;

    /// <summary>
    /// Compares two byte spans the way java.util.Arrays.compare(byte[], byte[])
    /// does: signed bytes first, then the length difference.
    /// </summary>
    /// <remarks>
    /// Spans only: the body stays a signed loop, because an unsigned comparison answers
    /// the opposite for every non-ASCII byte.
    /// </remarks>
    internal static int CompareBytes(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            int d = (sbyte)a[i] - (sbyte)b[i];
            if (d != 0)
            {
                return d;
            }
        }

        return a.Length - b.Length;
    }

    /// <summary>
    /// Converts the source to bytes, using the character set.
    /// </summary>
    /// <remarks>
    /// Code points, not chars, are the unit here, because that is the unit the JDK's encoder
    /// counts in: one unencodable code point is one '?', and an astral character is one code
    /// point spread over two chars. Runs of representable characters are handed to the
    /// encoding in one piece, so the common case is a single call.
    /// </remarks>
    /// <param name="source">the source</param>
    /// <returns>the bytes</returns>
    internal byte[] ToBytes(string source)
    {
        if (strength <= Secondary)
        {
            // TODO perform case-insensitive comparison properly (carried over from the reference).
            source = StringUtils.ToUpperEnglish(source);
        }

        return ToBytesCore(source);
    }

    /// <summary>
    /// The span core behind <see cref="ToBytes(string)"/> and the span comparison: the same
    /// walk, with the fold done by the caller.
    /// </summary>
    /// <param name="source">the already-folded characters</param>
    /// <returns>the bytes</returns>
    private byte[] ToBytesCore(ReadOnlySpan<char> source)
    {
        List<byte>? bytes = null;
        int start = 0;
        int i = 0;
        while (i < source.Length)
        {
            Rune.DecodeFromUtf16(source[i..], out Rune rune, out int consumed);
            if (IsRepresentable(rune, source[i]))
            {
                i += consumed;
                continue;
            }

            if (bytes is null)
            {
                // GetMaxByteCount, not GetByteCount: this encoding fails rather than replacing,
                // so asking it to measure a string that contains a character it cannot encode
                // would throw while sizing the buffer that answer goes into.
                bytes = new List<byte>(encoding.GetMaxByteCount(source.Length));
            }

            if (i > start)
            {
                Append(bytes, source[start..i]);
            }

            bytes.AddRange(replacement);
            start = i + consumed;
            i = start;
        }

        if (bytes is null)
        {
            return EncodeRun(source);
        }

        if (i > start)
        {
            Append(bytes, source[start..i]);
        }

        return bytes.ToArray();
    }

    /// <summary>
    /// Encodes one representable run in a single call: the exact size is safe to ask for,
    /// because a run the walk hands over never contains a character the encoding refuses.
    /// </summary>
    private byte[] EncodeRun(ReadOnlySpan<char> run)
    {
        byte[] encoded = new byte[encoding.GetByteCount(run)];
        int written = encoding.GetBytes(run, encoded);
        return written == encoded.Length ? encoded : encoded[..written];
    }

    private void Append(List<byte> bytes, ReadOnlySpan<char> run)
    {
        byte[] encoded = EncodeRun(run);
        bytes.AddRange(encoded);
    }

    /// <summary>U+FFFD as a code point, which is what an unpaired surrogate decodes to.</summary>
    private const int ReplacementRune = 0xFFFD;

    /// <summary>
    /// Whether the encoding can represent the code point. A surrogate that decodes to the
    /// replacement rune is an unpaired surrogate, which no charset here can represent even
    /// when the text itself is the replacement rune.
    /// </summary>
    private bool IsRepresentable(Rune rune, char firstChar)
    {
        if (rune.Value == ReplacementRune && firstChar != ReplacementRune)
        {
            return false;
        }

        Span<char> buffer = stackalloc char[2];
        int written = rune.EncodeToUtf16(buffer);
        try
        {
            return encoding.GetByteCount(buffer[..written]) >= 0;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the collation key for the given string.
    /// </summary>
    /// <param name="source">the string</param>
    /// <returns>the collation key</returns>
    public CharsetCollationKey GetCollationKey(string source)
    {
        return new CharsetCollationKey(source, ToBytes(source));
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return 255;
    }

    /// <summary>
    /// The collation key of a string: its encoded bytes.
    /// </summary>
    public sealed class CharsetCollationKey
    {
        private readonly string source;
        private readonly byte[] bytes;

        internal CharsetCollationKey(string source, byte[] bytes)
        {
            this.source = source;
            this.bytes = bytes;
        }

        /// <summary>
        /// Gets the string this is a key for.
        /// </summary>
        public string Source => source;

        /// <summary>
        /// Compares this key with another key.
        /// </summary>
        /// <param name="target">the other key</param>
        /// <returns>the same signed difference java.util.Arrays.compare returns</returns>
        public int CompareTo(CharsetCollationKey target)
        {
            return CompareBytes(bytes, target.bytes);
        }

        /// <summary>
        /// Converts this key to bytes.
        /// </summary>
        /// <returns>the encoded bytes</returns>
        public byte[] ToByteArray()
        {
            return bytes;
        }
    }
}
