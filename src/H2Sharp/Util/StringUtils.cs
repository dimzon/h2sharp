/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.util.StringUtils.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: full port. The notable decisions are:
 *
 * - toUpperEnglish/toLowerEnglish use Locale.ENGLISH, which is
 *   ToUpperInvariant()/ToLowerInvariant() in .NET, NOT the current culture.
 * - startsWithIgnoringCase uses java.text.Collator at PRIMARY strength, which has no
 *   .NET equivalent. It is implemented with an invariant culture-aware comparison and
 *   verified against the reference JVM; see StringUtilsOracleTests.
 * - The SoftReference<softCache> and AtomicReferenceArray<TO_UPPER_CACHE> caches use
 *   weak/atomic types with no direct counterpart; they are plain fields with
 *   Interlocked access. These are pure caches, so a weaker memory model is safe.
 * - appendHex/convertBytesToHex live in H2Sharp.Util.Hex because Message.ErrorMessages
 *   needs them without pulling in this type.
 * - URLEncoder is not System.Web.HttpUtility or Uri.EscapeDataString: Java's
 *   form-urlencoded variant maps a space to '+' and leaves . - * _ alone. That is
 *   implemented directly in UrlEncode so that SQL URLs stay byte-identical.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using H2Sharp.Api;
using H2Sharp.Message;

namespace H2Sharp.Util;

/// <summary>
/// A few String-related utility functions.
/// </summary>
public static class StringUtils
{
    /// <summary>
    /// The uppercase cache holds 1024 buckets of two strings per bucket.
    /// </summary>
    private const int ToUpperCacheLength = 2 * 1024;

    private const int ToUpperCacheMaxEntryLength = 64;

    /// <summary>
    /// Largest char buffer this class puts on the stack. 128 chars keeps every helper
    /// below well inside the conservative per-frame budget; anything larger falls back
    /// to a heap array so a long pad or a wide value cannot blow the stack.
    /// </summary>
    private const int MaxStackChars = 128;

    /// <summary>
    /// Lowest code point that can appear as a key in
    /// <see cref="CaseFolding.Lower(int)"/>. The pre-check in
    /// <see cref="HasLowerCaseFoldingOverrides"/> uses it to skip the table entirely for
    /// the overwhelmingly common ASCII case, so it has to stay in step with the generated
    /// table: too high and a real override is silently ignored, which is exactly what
    /// happened when this was a hand-written "0x0130 or 0x0304" check and the table grew
    /// Unicode 16 entries such as U+1C89 -> U+1C8A. CaseFoldingOverrideThresholdsAreExact
    /// fails if the table ever grows a key below it.
    /// </summary>
    internal const int LowerOverrideThreshold = 0x0130;

    private static readonly string?[][] ToUpperCache = new string?[ToUpperCacheLength][];

    /// <summary>
    /// Weakly referenced cache of interned values, recreated at most every 5 seconds.
    /// </summary>
    private static string?[]? softCache;

    private static long softCacheCreatedNs;

    /// <summary>
    /// Lookup table for hex digits; -1 marks a character that is not a hex digit.
    /// Sized to <c>'f' + 1</c> like H2's, so characters above 'f' fall off the end and
    /// are caught by the bounds check H2 relies on.
    /// </summary>
    private static readonly int[] HexDecode = BuildHexDecode();

    private static int[] BuildHexDecode()
    {
        int[] table = new int['f' + 1];
        Array.Fill(table, -1);
        for (int i = 0; i <= 9; i++)
        {
            table[i + '0'] = i;
        }

        for (int i = 0; i <= 5; i++)
        {
            table[i + 'a'] = i + 10;
            table[i + 'A'] = i + 10;
        }

        return table;
    }

    private static string?[]? GetCache()
    {
        string?[]? cache = Volatile.Read(ref softCache);
        if (cache is not null)
        {
            return cache;
        }

        // create a new cache at most every 5 seconds so that out of memory exceptions
        // are not delayed
        long time = Utils.CurrentNanoTime();
        if (softCacheCreatedNs != 0
            && time - softCacheCreatedNs < 5_000_000_000L)
        {
            return null;
        }

        try
        {
            cache = new string?[Engine.SysProperties.ObjectCacheSize];
            Volatile.Write(ref softCache, cache);
            return cache;
        }
        finally
        {
            Interlocked.Exchange(ref softCacheCreatedNs, Utils.CurrentNanoTime());
        }
    }

    /// <summary>
    /// Convert a string to uppercase using the English locale.
    /// </summary>
    /// <remarks>
    /// Java uses the Unicode <em>full</em> case mapping, .NET the <em>simple</em> one,
    /// so the differences are applied from H2Sharp.Util.CaseFolding on top of
    /// <c>ToUpperInvariant</c>. That table is generated from the reference JVM.
    /// </remarks>
    /// <param name="s">the text to convert</param>
    /// <returns>the uppercase text</returns>
    public static string ToUpperEnglish(string s)
    {
        if (s.Length > ToUpperCacheMaxEntryLength)
        {
            return ToUpperEnglishCore(s);
        }

        int index = s.GetHashCode() & (ToUpperCacheLength - 1);

        // A benign race is acceptable here: this is a pure cache, and Java uses an
        // AtomicReferenceArray only to avoid allocating on contention. C# cannot pass
        // an array element by ref, so the pair is stored as one immutable value.
        string?[]? entry = ToUpperCache[index];
        if (entry is not null && entry[0] == s)
        {
            return entry[1]!;
        }

        string s2 = ToUpperEnglishCore(s);
        ToUpperCache[index] = [s, s2];
        return s2;
    }

    private static string ToUpperEnglishCore(string s)
    {
        if (!HasCaseFoldingOverrides(s))
        {
            // Identifier normalisation runs over already-uppercase names constantly, so
            // the common "nothing changes" case must not allocate. ToUpperInvariant()
            // always returns a fresh string; detecting the no-op first lets the original
            // instance flow through instead.
            return UpperInvariantIfChanged(s);
        }

        // Exact capacity: each override contributes its expansion length instead of the
        // guess s.Length + 8, so the builder never reallocates while filling.
        StringBuilder builder = new(s.Length + UpperExtraLength(s));
        for (int i = 0; i < s.Length; i++)
        {
            string? extra = CaseFolding.Upper(s[i]);
            if (extra is null)
            {
                builder.Append(char.ToUpperInvariant(s[i]));
            }
            else
            {
                builder.Append(extra);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns <c>s.ToUpperInvariant()</c>, or <paramref name="s"/> itself when the
    /// invariant mapping is the identity. Strings are immutable, so returning the same
    /// instance is indistinguishable from returning an equal copy, and it keeps the
    /// gen0 allocation out of the hot identifier path.
    /// </summary>
    private static string UpperInvariantIfChanged(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < 0x80)
            {
                if (c is >= 'a' and <= 'z')
                {
                    return s.ToUpperInvariant();
                }

                continue;
            }

            if (char.ToUpperInvariant(c) != c)
            {
                return s.ToUpperInvariant();
            }
        }

        return s;
    }

    /// <summary>
    /// The number of characters the uppercase overrides add on top of the input length.
    /// </summary>
    private static int UpperExtraLength(string s)
    {
        int extra = 0;
        for (int i = 0; i < s.Length; i++)
        {
            string? mapping = CaseFolding.Upper(s[i]);
            if (mapping is not null)
            {
                extra += mapping.Length - 1;
            }
        }

        return extra;
    }

    private static string LowerInvariantIfChanged(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < 0x80)
            {
                if (c is >= 'A' and <= 'Z')
                {
                    return s.ToLowerInvariant();
                }

                continue;
            }

            if (char.ToLowerInvariant(c) != c)
            {
                return s.ToLowerInvariant();
            }
        }

        return s;
    }

    /// <summary>
    /// Convert a string to lowercase using the English locale.
    /// </summary>
    /// <remarks>
    /// See <see cref="ToUpperEnglish"/>: Java applies the full case mapping, which
    /// maps U+0130 to "i" plus a combining dot above where .NET maps it to U+0131.
    /// </remarks>
    /// <param name="s">the text to convert</param>
    /// <returns>the lowercase text</returns>
    public static string ToLowerEnglish(string s)
    {
        if (!HasLowerCaseFoldingOverrides(s))
        {
            // See UpperInvariantIfChanged: the identity case must not allocate.
            return LowerInvariantIfChanged(s);
        }

        int extra = 0;
        for (int i = 0; i < s.Length; i++)
        {
            string? mapping = CaseFolding.Lower(s[i]);
            if (mapping is not null)
            {
                extra += mapping.Length - 1;
            }
        }

        StringBuilder builder = new(s.Length + extra);
        for (int i = 0; i < s.Length; i++)
        {
            string? extraMapping = CaseFolding.Lower(s[i]);
            if (extraMapping is null)
            {
                builder.Append(char.ToLowerInvariant(s[i]));
            }
            else
            {
                builder.Append(extraMapping);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Cheap pre-check so that the overwhelmingly common ASCII path avoids building a
    /// StringBuilder. The lowest code point with a full-mapping override is U+00DF
    /// (LATIN SMALL LETTER SHARP S), so anything below that is safe.
    /// </summary>
    private static bool HasCaseFoldingOverrides(string s)
    {
        foreach (char c in s)
        {
            if (c >= 0x00DF)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasLowerCaseFoldingOverrides(string s)
    {
        foreach (char c in s)
        {
            if (c >= LowerOverrideThreshold)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Convert a string to a SQL literal. Null is converted to NULL. The text is
    /// enclosed in single quotes. If there are any special characters, the
    /// Unicode character string literal is used.
    /// </summary>
    /// <param name="s">the text to convert</param>
    /// <returns>the SQL literal</returns>
    public static string? QuoteStringSql(string? s)
        => s is null ? "NULL" : QuoteStringSql(new StringBuilder(s.Length + 2), s).ToString();

    /// <summary>
    /// Convert a string to a SQL character string literal. Null is converted to
    /// NULL. If there are any special characters, the Unicode character string
    /// literal is used.
    /// </summary>
    /// <param name="builder">string builder to append result to</param>
    /// <param name="s">the text to convert</param>
    /// <returns>the specified string builder</returns>
    public static StringBuilder QuoteStringSql(StringBuilder builder, string? s)
        => s is null
            ? builder.Append("NULL")
            : QuoteIdentifierOrLiteral(builder, s, '\'');

    /// <summary>
    /// Decodes a Unicode SQL string.
    /// </summary>
    /// <param name="s">the string to decode</param>
    /// <param name="uencode">the code point of the UENCODE character, or '\\'</param>
    /// <returns>the decoded string</returns>
    public static string DecodeUnicodeStringSql(string s, int uencode)
    {
        int l = s.Length;
        StringBuilder builder = new(l);
        for (int i = 0; i < l;)
        {
            int cp = ReadCodePoint(s, ref i);
            if (cp == uencode)
            {
                if (i >= l)
                {
                    throw GetFormatException(s, i);
                }

                // Java reads the following code point WITHOUT advancing i here; only
                // the doubled-uencode branch advances. The digits that follow are then
                // parsed starting at i, which is why s.charAt(i) is tested for '+'.
                int at = i;
                int second = ReadCodePointAt(s, at);
                if (second == uencode)
                {
                    i = at + CodePointLengthAt(s, at);
                }
                else
                {
                    if (at + 4 > l)
                    {
                        throw GetFormatException(s, at);
                    }

                    char ch = s[at];
                    int end;
                    try
                    {
                        if (ch == '+')
                        {
                            if (at + 7 > l)
                            {
                                throw GetFormatException(s, at);
                            }

                            end = at + 7;
                            cp = ParseUnsignedInt(s, at + 1, end, 16);
                        }
                        else
                        {
                            end = at + 4;
                            cp = ParseUnsignedInt(s, at, end, 16);
                        }
                    }
                    catch (FormatException)
                    {
                        throw GetFormatException(s, at);
                    }

                    i = end;
                }
            }

            AppendCodePoint(builder, cp);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Convert a string to a Java literal using the correct escape sequences.
    /// The literal is not enclosed in double quotes. The result can be used in
    /// properties files or in Java source code.
    /// </summary>
    /// <param name="s">the text to convert</param>
    /// <returns>the Java representation</returns>
    public static string JavaEncode(string s)
    {
        // Escape sequences expand the text, so sizing the builder at s.Length makes it
        // reallocate and copy its chunk once or twice. One cheap counting pass gives an
        // exact capacity and turns the whole call into a single result allocation.
        StringBuilder buff = new(JavaEncodedLength(s));
        JavaEncode(s, buff, forSql: false);
        return buff.ToString();
    }

    /// <summary>
    /// The exact length of <c>JavaEncode(s)</c>, so the builder can be allocated once.
    /// </summary>
    /// <param name="s">the text to convert</param>
    /// <returns>the number of characters the encoded text has</returns>
    private static int JavaEncodedLength(string s)
    {
        int length = 0;
        for (int i = 0; i < s.Length; i++)
        {
            switch (s[i])
            {
                case '\t':
                case '\n':
                case '\f':
                case '\r':
                case '"':
                case '\'':
                case '\\':
                    length += 2;
                    break;

                default:
                    length += s[i] is >= ' ' and < (char)0x80 ? 1 : 6;
                    break;
            }
        }

        return length;
    }

    /// <summary>
    /// Convert a string to a Java literal using the correct escape sequences.
    /// The literal is not enclosed in double quotes. The result can be used in
    /// properties files or in Java source code.
    /// </summary>
    /// <param name="s">the text to convert</param>
    /// <param name="buff">the Java representation to append to</param>
    /// <param name="forSql">true if we embed this inside a STRINGDECODE SQL command</param>
    public static void JavaEncode(string s, StringBuilder buff, bool forSql)
    {
        int length = s.Length;
        int runStart = 0;

        for (int i = 0; i < length; i++)
        {
            char c = s[i];
            if (c is >= ' ' and < (char)0x80 && c is not ('\t' or '\n' or '\f' or '\r'
                or '"' or '\'' or '\\'))
            {
                // Plain ASCII: stay in the run and let the span append below copy it in
                // one go instead of one Append(char) call per character.
                continue;
            }

            if (i > runStart)
            {
                buff.Append(s.AsSpan(runStart, i - runStart));
            }

            runStart = i + 1;
            switch (c)
            {
                case '\t':
                    // HT horizontal tab
                    buff.Append("\\t");
                    break;

                case '\n':
                    // LF linefeed
                    buff.Append("\\n");
                    break;

                case '\f':
                    // FF form feed
                    buff.Append("\\f");
                    break;

                case '\r':
                    // CR carriage return
                    buff.Append("\\r");
                    break;

                case '"':
                    // double quote
                    buff.Append("\\\"");
                    break;

                case '\'':
                    // quote. H2 appends one apostrophe when forSql is set and then
                    // unconditionally appends another, so the result is always ''.
                    if (forSql)
                    {
                        buff.Append('\'');
                    }

                    buff.Append('\'');
                    break;

                case '\\':
                    // backslash
                    buff.Append("\\\\");
                    break;

                default:
                    if (c >= ' ' && c < 0x80)
                    {
                        buff.Append(c);
                    }
                    else
                    {
                        // not supported in properties files
                        Hex.AppendHex4(buff.Append("\\u"), c);
                    }

                    break;
            }
        }

        if (length > runStart)
        {
            buff.Append(s.AsSpan(runStart, length - runStart));
        }
    }

    /// <summary>
    /// Add an asterisk ('[*]') at the given position. This format is used to
    /// show where parsing failed in a statement.
    /// </summary>
    /// <param name="s">the text</param>
    /// <param name="index">the position</param>
    /// <returns>the text with asterisk</returns>
    public static string? AddAsterisk(string? s, int index)
    {
        if (s is not null)
        {
            int len = s.Length;
            index = Math.Min(index, len);
            return new StringBuilder(len + 3)
                .Append(s, 0, index)
                .Append("[*]")
                .Append(s, index, len - index)
                .ToString();
        }

        return s;
    }

    private static H2SharpException GetFormatException(string s, int i)
        => DbException.Get(ErrorCode.StringFormatError1, AddAsterisk(s, i));

    /// <summary>
    /// Decode a text that is encoded as a Java string literal. The Java
    /// properties file format and Java source code format is supported.
    /// </summary>
    /// <param name="s">the encoded string</param>
    /// <returns>the string</returns>
    public static string JavaDecode(string s)
    {
        // The overwhelmingly common input has no escapes at all, and the result is then
        // the input. Returning it directly keeps the whole call allocation free instead
        // of building a StringBuilder plus its chunk plus the result string.
        if (s.IndexOf('\\') < 0)
        {
            return s;
        }

        int length = s.Length;

        // Every escape consumes at least two characters and produces exactly one, so
        // s.Length minus the number of escapes is a tight upper bound on the result. It
        // matters because a StringBuilder allocates its chunk eagerly at the capacity it
        // is given, so the naive s.Length guess wastes the difference twice over.
        int escapes = 0;
        foreach (char c in s)
        {
            if (c == '\\')
            {
                escapes++;
            }
        }

        StringBuilder buff = new(length - escapes);
        int runStart = 0;
        for (int i = 0; i < length; i++)
        {
            char c = s[i];
            if (c != '\\')
            {
                continue;
            }

            // Flush the literal characters before the escape so they are copied with a
            // single span append rather than one Append(char) call each.
            if (i > runStart)
            {
                buff.Append(s.AsSpan(runStart, i - runStart));
            }

            if (i + 1 >= s.Length)
            {
                throw GetFormatException(s, i);
            }

            c = s[++i];
            switch (c)
            {
                case 't':
                    buff.Append('\t');
                    break;

                case 'r':
                    buff.Append('\r');
                    break;

                case 'n':
                    buff.Append('\n');
                    break;

                case 'b':
                    buff.Append('\b');
                    break;

                case 'f':
                    buff.Append('\f');
                    break;

                case '#':
                    // for properties files
                    buff.Append('#');
                    break;

                case '=':
                    // for properties files
                    buff.Append('=');
                    break;

                case ':':
                    // for properties files
                    buff.Append(':');
                    break;

                case '"':
                    buff.Append('"');
                    break;

                case '\\':
                    buff.Append('\\');
                    break;

                case 'u':
                    {
                        if (i + 4 >= length)
                        {
                            throw GetFormatException(s, i);
                        }

                        if (!int.TryParse(
                                s.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                out int code))
                        {
                            throw GetFormatException(s, i);
                        }

                        i += 4;
                        buff.Append((char)code);
                        break;
                    }

                default:
                    // Java uses Integer.parseInt(.., 8): the escape is octal, so
                    // "\101" is 'A' (65), not 101.
                    if (c is >= '0' and <= '9' && i + 2 < length)
                    {
                        int octal;
                        try
                        {
                            octal = ParseUnsignedInt(s, i, i + 3, 8);
                        }
                        catch (Exception e) when (e is FormatException or OverflowException)
                        {
                            throw GetFormatException(s, i);
                        }

                        i += 2;
                        buff.Append((char)octal);
                    }
                    else
                    {
                        throw GetFormatException(s, i);
                    }

                    break;
            }

            // Set after the switch: the octal and \u branches advance i past the digits
            // they consumed, and the next literal run starts right after them.
            runStart = i + 1;
        }

        if (length > runStart)
        {
            buff.Append(s.AsSpan(runStart, length - runStart));
        }

        return buff.ToString();
    }

    /// <summary>
    /// Convert a string to the Java literal and enclose it with double quotes.
    /// Null will result in "null" (without double quotes).
    /// </summary>
    /// <param name="s">the text to convert</param>
    /// <returns>the Java representation</returns>
    public static string? QuoteJavaString(string? s)
    {
        if (s is null)
        {
            return "null";
        }

        StringBuilder builder = new StringBuilder(s.Length + 2).Append('"');
        JavaEncode(s, builder, forSql: false);
        return builder.Append('"').ToString();
    }

    /// <summary>
    /// Convert a string array to the Java source code that represents this
    /// array. Null will be converted to 'null'.
    /// </summary>
    /// <param name="array">the string array</param>
    /// <returns>the Java source code (including new String[]{})</returns>
    public static string? QuoteJavaStringArray(string?[]? array)
    {
        if (array is null)
        {
            return "null";
        }

        StringBuilder buff = new("new String[]{");
        for (int i = 0; i < array.Length; i++)
        {
            if (i > 0)
            {
                buff.Append(", ");
            }

            buff.Append(QuoteJavaString(array[i]));
        }

        return buff.Append('}').ToString();
    }

    /// <summary>
    /// Convert an int array to the Java source code that represents this array.
    /// Null will be converted to 'null'.
    /// </summary>
    /// <param name="array">the int array</param>
    /// <returns>the Java source code (including new int[]{})</returns>
    public static string? QuoteJavaIntArray(int[]? array)
    {
        if (array is null)
        {
            return "null";
        }

        StringBuilder builder = new("new int[]{");
        for (int i = 0; i < array.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(array[i]);
        }

        return builder.Append('}').ToString();
    }

    /// <summary>
    /// Encode the string as a URL, using the same rules as
    /// java.net.URLEncoder: application/x-www-form-urlencoded, so a space becomes
    /// '+' and <c>.</c>, <c>-</c>, <c>*</c> and <c>_</c> are left alone.
    /// </summary>
    /// <param name="s">the string to encode</param>
    /// <returns>the encoded string</returns>
    /// <summary>
    /// UTF-8 as java.lang.String.getBytes(StandardCharsets.UTF_8) produces it: an
    /// unpaired surrogate becomes a single '?' rather than U+FFFD, which is what
    /// .NET's default replacement produces. The percent-escape of that byte is user
    /// visible in generated URLs, so H2Sharp must match.
    /// </summary>
    private static readonly Encoding UrlEncoding = Encoding.GetEncoding(
        "utf-8",
        new EncoderReplacementFallback("?"),
        new DecoderReplacementFallback("?"));

    private static char HexDigit(int nibble) => (char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);

    /// <summary>
    /// Encode the string as a URL.
    /// </summary>
    /// <param name="s">the string to encode</param>
    /// <returns>the encoded string</returns>
    public static string UrlEncode(string s)
    {
        byte[] bytes = UrlEncoding.GetBytes(s);
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        // One pass to size, one pass to write. The guess-based StringBuilder this
        // replaces reallocated for URLs with many escapes, which is the common case for
        // a query string.
        int length = 0;
        foreach (byte b in bytes)
        {
            length += UrlEncodedCharCount(b);
        }

        char[]? heap = length <= MaxStackChars ? null : new char[length];
        Span<char> buffer = heap is null ? stackalloc char[MaxStackChars] : heap;
        buffer = buffer[..length];

        int position = 0;
        foreach (byte b in bytes)
        {
            char c = (char)b;
            if ((c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9')
                || c == '.' || c == '-' || c == '*' || c == '_')
            {
                buffer[position++] = c;
            }
            else if (c == ' ')
            {
                buffer[position++] = '+';
            }
            else
            {
                // java.net.URLEncoder emits uppercase percent-escapes; Hex.AppendHex
                // is lowercase because that is what H2 uses elsewhere.
                buffer[position++] = '%';
                buffer[position++] = HexDigit(b >> 4);
                buffer[position++] = HexDigit(b & 0xF);
            }
        }

        Debug.Assert(position == length, "UrlEncode length mismatch");
        return new string(buffer);
    }

    /// <summary>
    /// How many characters a byte contributes to <see cref="UrlEncode"/>: one for an
    /// unreserved byte or a space, three for a percent-escape.
    /// </summary>
    private static int UrlEncodedCharCount(byte b)
    {
        char c = (char)b;
        return (c >= 'a' && c <= 'z')
            || (c >= 'A' && c <= 'Z')
            || (c >= '0' && c <= '9')
            || c == '.' || c == '-' || c == '*' || c == '_' || c == ' '
            ? 1
            : 3;
    }

    /// <summary>
    /// Decode the URL to a string.
    /// </summary>
    /// <param name="encoded">the encoded URL</param>
    /// <returns>the decoded string</returns>
    public static string UrlDecode(string encoded)
    {
        int length = encoded.Length;
        byte[] buff = new byte[length];
        int j = 0;
        for (int i = 0; i < length; i++)
        {
            char ch = encoded[i];
            if (ch == '+')
            {
                buff[j++] = (byte)' ';
            }
            else if (ch == '%')
            {
                if (i + 2 >= length)
                {
                    throw new ArgumentException("Truncated escape decoding " + encoded);
                }

                buff[j++] = (byte)ParseUnsignedInt(encoded, i + 1, i + 3, 16);
                i += 2;
            }
            else if (ch <= 127 && ch >= ' ')
            {
                buff[j++] = (byte)ch;
            }
            else
            {
                throw new ArgumentException("Unexpected char " + (int)ch + " decoding " + encoded);
            }
        }

        return Encoding.UTF8.GetString(buff, 0, j);
    }

    /// <summary>
    /// Split a string into an array of strings using the given separator. A null
    /// string will result in a null array, and an empty string in a zero element
    /// array. A backslash escapes the next character.
    /// </summary>
    /// <param name="s">the string to split</param>
    /// <param name="separatorChar">the separator character</param>
    /// <param name="trim">whether each element should be trimmed</param>
    /// <returns>the array list</returns>
    public static string[]? ArraySplit(string? s, char separatorChar, bool trim)
    {
        if (s is null)
        {
            return null;
        }

        int length = s.Length;
        if (length == 0)
        {
            return [];
        }

        // Count the separators first so the result array can be filled directly. The
        // previous List<string> plus ToArray allocated the list, its backing array and
        // the final array, and grew the first two while scanning.
        // The counting pass must mirror the parse exactly, including the order of the
        // two tests and the escape skip: an escaped separator is not a separator, and
        // when the separator *is* a backslash the first test wins and nothing is skipped.
        int count = 1;
        for (int i = 0; i < length; i++)
        {
            char c = s[i];
            if (c == separatorChar)
            {
                count++;
            }
            else if (c == '\\' && i < length - 1)
            {
                i++;
            }
        }

        string[] result = new string[count];
        int index = 0;
        StringBuilder buff = new(length / count + 16);
        int runStart = 0;
        for (int i = 0; i < length; i++)
        {
            char c = s[i];
            if (c == separatorChar)
            {
                if (i > runStart)
                {
                    buff.Append(s.AsSpan(runStart, i - runStart));
                }

                string e = buff.ToString();
                result[index++] = trim ? e.Trim() : e;
                buff.Clear();
                runStart = i + 1;
            }
            else if (c == '\\' && i < length - 1)
            {
                // The escaped character is a literal, and the backslash is dropped, so
                // the run has to be flushed before it.
                if (i > runStart)
                {
                    buff.Append(s.AsSpan(runStart, i - runStart));
                }

                runStart = i + 1;
                buff.Append(s[++i]);
                runStart = i + 1;
            }
        }

        if (length > runStart)
        {
            buff.Append(s.AsSpan(runStart, length - runStart));
        }

        string last = buff.ToString();
        result[index] = trim ? last.Trim() : last;
        return result;
    }

    /// <summary>
    /// Combine an array of strings to one array using the given separator
    /// character. A backslash and the separator character and escaped using a
    /// backslash.
    /// </summary>
    /// <param name="list">the string array</param>
    /// <param name="separatorChar">the separator character</param>
    /// <returns>the combined string</returns>
    public static string ArrayCombine(string?[] list, char separatorChar)
    {
        int length = ArrayCombinedLength(list, separatorChar);
        if (length <= MaxStackChars)
        {
            Span<char> stack = stackalloc char[MaxStackChars];
            WriteArrayCombined(stack[..length], list, separatorChar);
            return new string(stack[..length]);
        }

        // Above the stack budget, let the runtime allocate the result string itself. The
        // tuple state costs one small object, which is far less than the scratch array the
        // span path would need for a long list.
        return string.Create(
            length,
            (List: list, Separator: separatorChar),
            static (chars, state) => WriteArrayCombined(chars, state.List, state.Separator));
    }

    /// <summary>
    /// The exact length of <see cref="ArrayCombine"/>: one separator between elements plus
    /// one extra character for every escaped character inside an element.
    /// </summary>
    private static int ArrayCombinedLength(string?[] list, char separatorChar)
    {
        int length = list.Length > 0 ? list.Length - 1 : 0;
        foreach (string? element in list)
        {
            if (element is null)
            {
                continue;
            }

            length += element.Length;
            foreach (char c in element)
            {
                if (c == '\\' || c == separatorChar)
                {
                    length++;
                }
            }
        }

        return length;
    }

    private static void WriteArrayCombined(
        Span<char> destination, string?[] list, char separatorChar)
    {
        int position = 0;
        for (int i = 0; i < list.Length; i++)
        {
            if (i > 0)
            {
                destination[position++] = separatorChar;
            }

            string? s = list[i];
            if (s is null)
            {
                continue;
            }

            foreach (char c in s)
            {
                if (c == '\\' || c == separatorChar)
                {
                    destination[position++] = '\\';
                }

                destination[position++] = c;
            }
        }

        Debug.Assert(position == destination.Length, "ArrayCombine length mismatch");
    }

    /// <summary>
    /// Creates an XML attribute of the form name="value".
    /// A single space is prepended to the name, so that multiple attributes can be
    /// concatenated.
    /// </summary>
    /// <param name="name">the attribute name</param>
    /// <param name="value">the attribute value</param>
    /// <returns>the attribute</returns>
    public static string XmlAttr(string name, string value)
        => " " + name + "=\"" + XmlText(value) + "\"";

    /// <summary>
    /// Create an XML node with optional attributes and content.
    /// The data is indented with 4 spaces if it contains a newline character.
    /// </summary>
    /// <param name="name">the element name</param>
    /// <param name="attributes">the attributes (might be null)</param>
    /// <param name="content">the content (might be null)</param>
    /// <returns>the node</returns>
    public static string XmlNode(string name, string? attributes, string? content)
        => XmlNode(name, attributes, content, indent: true);

    /// <summary>
    /// Create an XML node with optional attributes and content. The data is
    /// indented with 4 spaces if it contains a newline character and the indent
    /// parameter is set to true.
    /// </summary>
    /// <param name="name">the element name</param>
    /// <param name="attributes">the attributes (might be null)</param>
    /// <param name="content">the content (might be null)</param>
    /// <param name="indent">whether to indent the content if it contains a newline</param>
    /// <returns>the node</returns>
    public static string XmlNode(string name, string? attributes, string? content, bool indent)
    {
        StringBuilder builder = new();
        builder.Append('<').Append(name);
        if (attributes is not null)
        {
            builder.Append(attributes);
        }

        if (content is null)
        {
            return builder.Append("/>\n").ToString();
        }

        builder.Append('>');
        if (indent && content.Contains('\n'))
        {
            builder.Append('\n');
            Indent(builder, content, 4, newline: true);
        }
        else
        {
            builder.Append(content);
        }

        return builder.Append("</").Append(name).Append(">\n").ToString();
    }

    /// <summary>
    /// Indents a string with spaces and appends it to a specified builder.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="s">the string</param>
    /// <param name="spaces">the number of spaces</param>
    /// <param name="newline">append a newline if there is none</param>
    /// <returns>the specified string builder</returns>
    public static StringBuilder Indent(StringBuilder builder, string s, int spaces, bool newline)
    {
        for (int i = 0, length = s.Length; i < length;)
        {
            for (int j = 0; j < spaces; j++)
            {
                builder.Append(' ');
            }

            int n = s.IndexOf('\n', i);
            n = n < 0 ? length : n + 1;
            builder.Append(s, i, n - i);
            i = n;
        }

        if (newline && !s.EndsWith('\n'))
        {
            builder.Append('\n');
        }

        return builder;
    }

    /// <summary>
    /// Escapes a comment. If the data contains '--', it is converted to '- -'.
    /// The data is indented with 4 spaces if it contains a newline character.
    /// </summary>
    /// <param name="data">the comment text</param>
    /// <returns>&lt;!-- data --&gt;</returns>
    public static string XmlComment(string data)
    {
        int idx = 0;
        while (true)
        {
            idx = data.IndexOf("--", idx, StringComparison.Ordinal);
            if (idx < 0)
            {
                break;
            }

            data = data[..(idx + 1)] + " " + data[(idx + 1)..];
        }

        // must have a space at the beginning and at the end, otherwise the data must
        // not contain '-' as the first/last character
        if (data.Contains('\n'))
        {
            StringBuilder builder = new StringBuilder(data.Length + 18).Append("<!--\n");
            return Indent(builder, data, 4, newline: true).Append("-->\n").ToString();
        }

        return "<!-- " + data + " -->\n";
    }

    /// <summary>
    /// Converts the data to a CDATA element.
    /// If the data contains ']]&gt;', it is escaped as a text element.
    /// </summary>
    /// <param name="data">the text data</param>
    /// <returns>&lt;![CDATA[data]]&gt;</returns>
    public static string XmlCData(string data)
    {
        if (data.Contains("]]>", StringComparison.Ordinal))
        {
            return XmlText(data);
        }

        bool newline = data.EndsWith('\n');
        data = "<![CDATA[" + data + "]]>";
        return newline ? data + "\n" : data;
    }

    /// <summary>
    /// Returns &lt;?xml version="1.0"?&gt;
    /// </summary>
    /// <returns>&lt;?xml version="1.0"?&gt;</returns>
    public static string XmlStartDoc() => "<?xml version=\"1.0\"?>\n";

    /// <summary>
    /// Escapes an XML text element.
    /// </summary>
    /// <param name="text">the text data</param>
    /// <returns>the escaped text</returns>
    public static string XmlText(string text) => XmlText(text, escapeNewline: false);

    /// <summary>
    /// Escapes an XML text element.
    /// </summary>
    /// <param name="text">the text data</param>
    /// <param name="escapeNewline">whether to escape newlines</param>
    /// <returns>the escaped text</returns>
    public static string XmlText(string text, bool escapeNewline)
    {
        int length = text.Length;
        StringBuilder buff = new(length);
        for (int i = 0; i < length; i++)
        {
            char ch = text[i];
            switch (ch)
            {
                case '<':
                    buff.Append("&lt;");
                    break;

                case '>':
                    buff.Append("&gt;");
                    break;

                case '&':
                    buff.Append("&amp;");
                    break;

                case '\'':
                    // &apos; is not valid in HTML
                    buff.Append("&#39;");
                    break;

                case '"':
                    buff.Append("&quot;");
                    break;

                case '\r':
                case '\n':
                    if (escapeNewline)
                    {
                        buff.Append("&#x").Append(((int)ch).ToString("x", CultureInfo.InvariantCulture))
                            .Append(';');
                    }
                    else
                    {
                        buff.Append(ch);
                    }

                    break;

                case '\t':
                    buff.Append(ch);
                    break;

                default:
                    if (ch < ' ' || ch > 127)
                    {
                        buff.Append("&#x").Append(((int)ch).ToString("x", CultureInfo.InvariantCulture))
                            .Append(';');
                    }
                    else
                    {
                        buff.Append(ch);
                    }

                    break;
            }
        }

        return buff.ToString();
    }

    /// <summary>
    /// Replace all occurrences of the before string with the after string. Unlike
    /// <c>string.Replace</c> this method reads before and after as plain strings and
    /// if before is empty this method returns the original string.
    /// </summary>
    /// <param name="s">the string</param>
    /// <param name="before">the old text</param>
    /// <param name="after">the new text</param>
    /// <returns>the string with the before string replaced</returns>
    public static string ReplaceAll(string s, string before, string after)
    {
        int next = s.IndexOf(before, StringComparison.Ordinal);
        if (next < 0 || before.Length == 0)
        {
            return s;
        }

        StringBuilder buff = new(s.Length - before.Length + after.Length);
        int index = 0;
        while (true)
        {
            buff.Append(s, index, next - index).Append(after);
            index = next + before.Length;
            next = s.IndexOf(before, index, StringComparison.Ordinal);
            if (next < 0)
            {
                buff.Append(s, index, s.Length - index);
                break;
            }
        }

        return buff.ToString();
    }

    /// <summary>
    /// Enclose a string with double quotes. A double quote inside the string is
    /// escaped using a double quote.
    /// </summary>
    /// <param name="s">the text</param>
    /// <returns>the double-quoted text</returns>
    public static string QuoteIdentifier(string s)
        => QuoteIdentifierOrLiteral(new StringBuilder(s.Length + 2), s, '"').ToString();

    /// <summary>
    /// Enclose a string with double quotes and append it to the specified
    /// string builder. A double quote inside the string is escaped using a
    /// double quote.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="s">the text</param>
    /// <returns>the specified builder</returns>
    public static StringBuilder QuoteIdentifier(StringBuilder builder, string s)
        => QuoteIdentifierOrLiteral(builder, s, '"');

    private static StringBuilder QuoteIdentifierOrLiteral(StringBuilder builder, string s, char q)
    {
        int l = s.Length;

        // The common case is a string with nothing to escape in it, and there the loop below does
        // one code point read and one append call per character. In a CPU profile that quoting was
        // the single most expensive thing the value layer did, which is not where quoting belongs.
        // A single pass says whether the fast case applies, and the text then goes in one append.
        // The test is the same condition the loop itself applies: a character below a space or
        // above 127 needs the U& form, and the quote character has to be doubled. A surrogate is
        // above 127, so a string holding one cannot slip through here.
        bool plain = true;
        for (int i = 0; i < l; i++)
        {
            char c = s[i];
            if (c < ' ' || c > 127 || c == q)
            {
                plain = false;
                break;
            }
        }

        if (plain)
        {
            return builder.Append(q).Append(s.AsSpan(0, l)).Append(q);
        }

        int builderLength = builder.Length;
        builder.Append(q);
        for (int i = 0; i < l;)
        {
            int cp = ReadCodePoint(s, ref i);
            if (cp < ' ' || cp > 127)
            {
                // need to start from the beginning
                builder.Length = builderLength;
                builder.Append("U&").Append(q);
                for (i = 0; i < l;)
                {
                    cp = ReadCodePoint(s, ref i);
                    if (cp >= ' ' && cp < 127)
                    {
                        char ch = (char)cp;
                        if (ch == q || ch == '\\')
                        {
                            builder.Append(ch);
                        }

                        builder.Append(ch);
                    }
                    else if (cp <= 0xFFFF)
                    {
                        Hex.AppendHex(builder.Append('\\'), cp, 2);
                    }
                    else
                    {
                        Hex.AppendHex(builder.Append("\\+"), cp, 3);
                    }
                }

                break;
            }

            if (cp == q)
            {
                builder.Append(q);
            }

            builder.Append((char)cp);
        }

        return builder.Append(q);
    }

    /// <summary>
    /// Compares two strings the way Java's <c>String.compareTo</c> does.
    /// </summary>
    /// <remarks>
    /// Java returns the difference of the first two differing UTF-16 code units, and the
    /// difference of the lengths when one string is a prefix of the other; .NET's
    /// <see cref="string.CompareOrdinal(string, string)"/> returns only a sign. The sign is all
    /// a comparison needs, but the magnitude is what the reference hands out, and it can be
    /// observed: an empty string against <c>abc</c> is -3 in Java and -1 here.
    /// </remarks>
    /// <param name="a">the first string</param>
    /// <param name="b">the second string</param>
    /// <returns>
    /// the difference of the first differing code units, or of the lengths if one string is a
    /// prefix of the other
    /// </returns>
    public static int CompareJava(string a, string b)
    {
        int shared = Math.Min(a.Length, b.Length);
        for (int i = 0; i < shared; i++)
        {
            char ca = a[i], cb = b[i];
            if (ca != cb)
            {
                return ca - cb;
            }
        }

        return a.Length - b.Length;
    }
    /// <summary>
    /// Check if a String is null or empty.
    /// </summary>
    /// <param name="s">the string to check</param>
    /// <returns>true if it is null or empty</returns>
    public static bool IsNullOrEmpty([NotNullWhen(false)] string? s) => string.IsNullOrEmpty(s);

    /// <summary>
    /// Pad a string. This method is used for the SQL function RPAD and LPAD.
    /// </summary>
    /// <param name="string">the original string</param>
    /// <param name="n">the target length</param>
    /// <param name="padding">the padding string</param>
    /// <param name="right">true if the padding should be appended at the end</param>
    /// <returns>the padded string</returns>
    public static string Pad(string @string, int n, string? padding, bool right)
    {
        if (n < 0)
        {
            n = 0;
        }

        if (n < @string.Length)
        {
            return @string[..n];
        }

        if (n == @string.Length)
        {
            return @string;
        }

        int paddingChar;
        if (padding is null || padding.Length == 0)
        {
            paddingChar = ' ';
        }
        else
        {
            int index = 0;
            paddingChar = ReadCodePoint(padding, ref index);
        }

        // Build into an exactly sized buffer instead of a StringBuilder: the padding is
        // a single repeated code point, so it is a fill rather than one Append call
        // per character, and the result needs no intermediate growth.
        int count = n - @string.Length;
        Span<char> pad = stackalloc char[2];
        int unit;
        if (paddingChar > 0xFFFF)
        {
            count >>= 1;
            unit = 2;
            int value = paddingChar - 0x10000;
            pad[0] = (char)(0xD800 + (value >> 10));
            pad[1] = (char)(0xDC00 + (value & 0x3FF));
        }
        else
        {
            unit = 1;
            pad[0] = (char)paddingChar;
        }

        int total = @string.Length + (count * unit);
        char[]? heap = total <= MaxStackChars ? null : new char[total];
        Span<char> buffer = heap is null ? stackalloc char[MaxStackChars] : heap;
        buffer = buffer[..total];

        int position = 0;
        if (right)
        {
            @string.AsSpan().CopyTo(buffer);
            position = @string.Length;
        }

        ReadOnlySpan<char> filler = pad[..unit];
        for (int i = 0; i < count; i++)
        {
            filler.CopyTo(buffer[position..]);
            position += unit;
        }

        if (!right)
        {
            @string.AsSpan().CopyTo(buffer[position..]);
        }

        return new string(buffer);
    }

    /// <summary>
    /// Create a new char array and copy all the data. If the size of the array is
    /// zero, the same array is returned.
    /// </summary>
    /// <param name="chars">the char array (might be null)</param>
    /// <returns>a new char array</returns>
    public static char[]? CloneCharArray(char[]? chars)
    {
        if (chars is null)
        {
            return null;
        }

        int len = chars.Length;
        if (len == 0)
        {
            return chars;
        }

        return (char[])chars.Clone();
    }

    /// <summary>
    /// Trim a character from a string.
    /// </summary>
    /// <param name="s">the string</param>
    /// <param name="leading">if leading characters should be removed</param>
    /// <param name="trailing">if trailing characters should be removed</param>
    /// <param name="characters">what to remove, or null for a space</param>
    /// <returns>the trimmed string</returns>
    public static string Trim(string s, bool leading, bool trailing, string? characters)
    {
        if (characters is null || characters.Length == 0)
        {
            return Trim(s, leading, trailing, ' ');
        }

        int length = characters.Length;
        if (length == 1)
        {
            return Trim(s, leading, trailing, characters[0]);
        }

        int count = CodePointCount(characters, 0, length);
        Func<int, bool> test;
        if (count <= 2)
        {
            int index = 0;
            int cp = ReadCodePoint(characters, ref index);
            if (count > 1)
            {
                int cp2 = ReadCodePoint(characters, ref index);
                if (cp != cp2)
                {
                    test = value => value == cp || value == cp2;
                }
                else
                {
                    test = value => value == cp;
                }
            }
            else
            {
                test = value => value == cp;
            }
        }
        else
        {
            HashSet<int> set = [];
            for (int i = 0; i < characters.Length;)
            {
                set.Add(ReadCodePoint(characters, ref i));
            }

            test = set.Contains;
        }

        return Trim(s, leading, trailing, test);
    }

    private static string Trim(string s, bool leading, bool trailing, Func<int, bool> test)
    {
        int begin = 0, end = s.Length;
        if (leading)
        {
            while (begin < end && test(ReadCodePointAt(s, begin)))
            {
                begin += CodePointLengthAt(s, begin);
            }
        }

        if (trailing)
        {
            while (end > begin && test(ReadCodePointBefore(s, end)))
            {
                end -= CodePointLengthAt(s, end);
            }
        }

        // substring() returns self if start == 0 && end == length()
        return s[begin..end];
    }

    /// <summary>
    /// Trim a character from a string.
    /// </summary>
    /// <param name="s">the string</param>
    /// <param name="leading">if leading characters should be removed</param>
    /// <param name="trailing">if trailing characters should be removed</param>
    /// <param name="character">what to remove</param>
    /// <returns>the trimmed string</returns>
    public static string Trim(string s, bool leading, bool trailing, char character)
    {
        int begin = 0, end = s.Length;
        if (leading)
        {
            while (begin < end && s[begin] == character)
            {
                begin++;
            }
        }

        if (trailing)
        {
            while (end > begin && s[end - 1] == character)
            {
                end--;
            }
        }

        // substring() returns self if start == 0 && end == length()
        return s[begin..end];
    }

    /// <summary>
    /// Trim whitespace from a substring. Equivalent of
    /// <c>substring(beginIndex).trim()</c>.
    /// </summary>
    /// <param name="s">the string</param>
    /// <param name="beginIndex">start index of the substring (inclusive)</param>
    /// <returns>trimmed substring</returns>
    public static string TrimSubstring(string s, int beginIndex)
        => TrimSubstring(s, beginIndex, s.Length);

    /// <summary>
    /// Trim whitespace from a substring. Equivalent of
    /// <c>substring(beginIndex, endIndex).trim()</c>.
    /// </summary>
    /// <param name="s">the string</param>
    /// <param name="beginIndex">start index of the substring (inclusive)</param>
    /// <param name="endIndex">end index of the substring (exclusive)</param>
    /// <returns>trimmed substring</returns>
    public static string TrimSubstring(string s, int beginIndex, int endIndex)
    {
        while (beginIndex < endIndex && s[beginIndex] <= ' ')
        {
            beginIndex++;
        }

        while (beginIndex < endIndex && s[endIndex - 1] <= ' ')
        {
            endIndex--;
        }

        return s[beginIndex..endIndex];
    }

    /// <summary>
    /// Trim whitespace from a substring and append it to a specified string
    /// builder. Equivalent of
    /// <c>builder.append(substring(beginIndex, endIndex).trim())</c>.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="s">the string</param>
    /// <param name="beginIndex">start index of the substring (inclusive)</param>
    /// <param name="endIndex">end index of the substring (exclusive)</param>
    /// <returns>the specified builder</returns>
    public static StringBuilder TrimSubstring(StringBuilder builder, string s, int beginIndex, int endIndex)
    {
        while (beginIndex < endIndex && s[beginIndex] <= ' ')
        {
            beginIndex++;
        }

        while (beginIndex < endIndex && s[endIndex - 1] <= ' ')
        {
            endIndex--;
        }

        return builder.Append(s, beginIndex, endIndex - beginIndex);
    }

    /// <summary>
    /// Truncates the specified string to the specified length. This method, unlike
    /// <c>Substring</c>, doesn't break Unicode code points. If the specified length
    /// in characters breaks a valid pair of surrogates, the whole pair is not included
    /// into the result.
    /// </summary>
    /// <param name="s">the string to truncate</param>
    /// <param name="maximumLength">the maximum length in characters</param>
    /// <returns>the truncated string</returns>
    public static string TruncateString(string s, int maximumLength)
    {
        if (s.Length > maximumLength)
        {
            s = maximumLength > 0
                ? s[..(IsSurrogatePair(s[maximumLength - 1], s[maximumLength])
                    ? maximumLength - 1
                    : maximumLength)]
                : "";
        }

        return s;
    }

    /// <summary>
    /// Get the string from the cache if possible. If the string has not been found,
    /// it is added to the cache. If there is such a string in the cache, that one is
    /// returned.
    /// </summary>
    /// <param name="s">the original string</param>
    /// <returns>a string with the same content, if possible from the cache</returns>
    public static string Cache(string? s)
    {
        if (!Engine.SysProperties.ObjectCache)
        {
            return s!;
        }

        if (s is null)
        {
            return null!;
        }

        if (s.Length == 0)
        {
            return "";
        }

        string?[]? cache = GetCache();
        if (cache is not null)
        {
            int index = s.GetHashCode() & (Engine.SysProperties.ObjectCacheSize - 1);
            string? cached = cache[index];
            if (s == cached)
            {
                return cached;
            }

            cache[index] = s;
        }

        return s;
    }

    /// <summary>
    /// Clear the cache. This method is used for testing.
    /// </summary>
    public static void ClearCache() => Volatile.Write(ref softCache, null);

    /// <summary>
    /// Parses an unsigned 31-bit integer. Neither - nor + signs are allowed.
    /// </summary>
    /// <param name="s">string to parse</param>
    /// <param name="start">the beginning index, inclusive</param>
    /// <param name="end">the ending index, exclusive</param>
    /// <returns>the unsigned int not greater than int.MaxValue</returns>
    public static int ParseUInt31(string s, int start, int end)
    {
        if (end > s.Length || start < 0 || start > end)
        {
            throw new IndexOutOfRangeException();
        }

        if (start == end)
        {
            throw new FormatException("");
        }

        int result = 0;
        for (int i = start; i < end; i++)
        {
            char ch = s[i];

            // Ensure that the character is valid and that multiplication by 10 will be
            // performed without overflow
            if (ch is < '0' or > '9' || result > 214_748_364)
            {
                throw new FormatException(s[start..end]);
            }

            result = result * 10 + ch - '0';
            if (result < 0)
            {
                // Overflow
                throw new FormatException(s[start..end]);
            }
        }

        return result;
    }

    /// <summary>
    /// Convert a hex encoded string to a byte array.
    /// </summary>
    /// <param name="s">the hex encoded string</param>
    /// <returns>the byte array</returns>
    public static byte[] ConvertHexToBytes(string s)
    {
        int len = s.Length;
        if (len % 2 != 0)
        {
            throw DbException.Get(ErrorCode.HexStringOdd1, s);
        }

        len /= 2;
        byte[] buff = new byte[len];
        int mask = 0;
        int[] hex = HexDecode;
        try
        {
            for (int i = 0; i < len; i++)
            {
                int d = hex[s[i + i]] << 4 | hex[s[i + i + 1]];
                mask |= d;
                buff[i] = (byte)d;
            }
        }
        catch (IndexOutOfRangeException)
        {
            throw DbException.Get(ErrorCode.HexStringWrong1, s);
        }

        if ((mask & ~255) != 0)
        {
            throw DbException.Get(ErrorCode.HexStringWrong1, s);
        }

        return buff;
    }

    /// <summary>
    /// Parses a hex encoded string with possible space separators and appends
    /// the decoded binary string to the specified output stream.
    /// </summary>
    /// <param name="baos">the output stream, or null</param>
    /// <param name="s">the hex encoded string</param>
    /// <param name="start">the start index</param>
    /// <param name="end">the end index, exclusive</param>
    /// <returns>the specified output stream or a new output stream</returns>
    public static MemoryStream ConvertHexWithSpacesToBytes(
        MemoryStream? baos, string s, int start, int end)
    {
        baos ??= new MemoryStream((end - start) >>> 1);
        int mask = 0;
        int[] hex = HexDecode;
        int i = start;
        while (true)
        {
            // Skip spaces before the first digit. Running out of input here is a clean
            // end of input, not an error: the trailing mask check below decides.
            int c1;
            while (true)
            {
                if (i >= end)
                {
                    goto done;
                }

                c1 = s[i++];
                if (c1 != ' ')
                {
                    break;
                }
            }

            int c2;
            while (true)
            {
                if (i >= end)
                {
                    if (((mask | hex[c1]) & ~255) != 0)
                    {
                        throw GetHexStringException(ErrorCode.HexStringWrong1, s, start, end);
                    }

                    throw GetHexStringException(ErrorCode.HexStringOdd1, s, start, end);
                }

                c2 = s[i++];
                if (c2 != ' ')
                {
                    break;
                }
            }

            int d;
            try
            {
                d = hex[c1] << 4 | hex[c2];
                mask |= d;
                baos.WriteByte((byte)d);
            }
            catch (IndexOutOfRangeException)
            {
                throw GetHexStringException(ErrorCode.HexStringWrong1, s, start, end);
            }
        }

    done:
        if ((mask & ~255) != 0)
        {
            throw GetHexStringException(ErrorCode.HexStringWrong1, s, start, end);
        }

        return baos;
    }

    private static H2SharpException GetHexStringException(int code, string s, int start, int end)
        => DbException.Get(code, s[start..end]);

    /// <summary>
    /// Convert a byte array to a hex encoded string.
    /// </summary>
    /// <param name="value">the byte array</param>
    /// <returns>the hex encoded string</returns>
    public static string ConvertBytesToHex(byte[] value) => ConvertBytesToHex(value, value.Length);

    /// <summary>
    /// Convert a byte array to a hex encoded string.
    /// </summary>
    /// <param name="value">the byte array</param>
    /// <param name="len">the number of bytes to encode</param>
    /// <returns>the hex encoded string</returns>
    public static string ConvertBytesToHex(byte[] value, int len)
    {
        StringBuilder builder = new(len * 2);
        return ConvertBytesToHex(builder, value, len).ToString();
    }

    /// <summary>
    /// Convert a byte array to a hex encoded string and append it to a specified
    /// string builder.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the byte array</param>
    /// <returns>the hex encoded string</returns>
    public static StringBuilder ConvertBytesToHex(StringBuilder builder, byte[] value)
        => ConvertBytesToHex(builder, value, value.Length);

    /// <summary>
    /// Convert a byte array to a hex encoded string and append it to a specified
    /// string builder.
    /// </summary>
    /// <param name="builder">string builder to append to</param>
    /// <param name="value">the byte array</param>
    /// <param name="len">the number of bytes to encode</param>
    /// <returns>the hex encoded string</returns>
    public static StringBuilder ConvertBytesToHex(StringBuilder builder, byte[] value, int len)
        => Hex.AppendBytesHex(builder, value.AsSpan(0, len));

    /// <summary>
    /// Check if this string is a decimal number.
    /// </summary>
    /// <param name="s">the string</param>
    /// <returns>true if it is</returns>
    public static bool IsNumber(string s)
    {
        int l = s.Length;
        if (l == 0)
        {
            return false;
        }

        for (int i = 0; i < l; i++)
        {
            if (!char.IsDigit(s[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Check if the specified string is empty or contains only whitespace.
    /// </summary>
    /// <param name="s">the string</param>
    /// <returns>whether the specified string is empty or contains only whitespace</returns>
    public static bool IsWhitespaceOrEmpty(string s)
    {
        for (int i = 0, l = s.Length; i < l; i++)
        {
            if (s[i] > ' ')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Append a zero-padded number from 00 to 99 to a string builder.
    /// </summary>
    /// <param name="builder">the string builder</param>
    /// <param name="positiveValue">the number to append</param>
    /// <returns>the specified string builder</returns>
    public static StringBuilder AppendTwoDigits(StringBuilder builder, int positiveValue)
    {
        if (positiveValue < 10)
        {
            builder.Append('0');
        }

        return builder.Append(positiveValue);
    }

    /// <summary>
    /// Append a zero-padded number to a string builder.
    /// </summary>
    /// <param name="builder">the string builder</param>
    /// <param name="length">the number of characters to append</param>
    /// <param name="positiveValue">the number to append</param>
    /// <returns>the specified string builder</returns>
    public static StringBuilder AppendZeroPadded(StringBuilder builder, int length, int positiveValue)
    {
        string s = positiveValue.ToString(CultureInfo.InvariantCulture);
        length -= s.Length;
        for (; length > 0; length--)
        {
            builder.Append('0');
        }

        return builder.Append(s);
    }

    /// <summary>
    /// Appends the specified string or its part to the specified builder with
    /// a maximum builder length limit.
    /// </summary>
    /// <param name="builder">the string builder</param>
    /// <param name="s">the string to append</param>
    /// <param name="length">the length limit</param>
    /// <returns>the specified string builder</returns>
    public static StringBuilder AppendToLength(StringBuilder builder, string s, int length)
    {
        int builderLength = builder.Length;
        if (builderLength < length)
        {
            int need = length - builderLength;
            if (need >= s.Length)
            {
                builder.Append(s);
            }
            else
            {
                builder.Append(s, 0, need);
            }
        }

        return builder;
    }

    /// <summary>
    /// Escape table or schema patterns used for DatabaseMetaData functions.
    /// </summary>
    /// <param name="pattern">the pattern</param>
    /// <returns>the escaped pattern</returns>
    public static string? EscapeMetaDataPattern(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return pattern;
        }

        return ReplaceAll(pattern!, "\\", "\\\\");
    }

    /// <summary>
    /// Case-sensitive check if a text starts with a prefix.
    /// It only calls <c>string.StartsWith</c> and is only here for API consistency.
    /// </summary>
    /// <param name="text">the full text starting with a prefix</param>
    /// <param name="prefix">the full text starting with a prefix</param>
    /// <returns>true only if text starts with the prefix</returns>
    public static bool StartsWith(string text, string prefix)
        => text.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>
    /// Case-insensitive check if a text starts with a prefix.
    /// </summary>
    /// <remarks>
    /// H2 uses <c>Collator.getInstance().setStrength(Collator.PRIMARY)</c>, i.e. an
    /// ICU/CLDR collation that ignores case <i>and</i> accents, and it is
    /// locale-dependent by construction: the reference JVM in the differential
    /// tests answers "а"≈"А" under ru-RU and "not equal" under en-US/root, because
    /// that difference lives in the locale tailoring, not in the algorithm.
    /// <para>
    /// The closest BCL equivalent is <see cref="CompareOptions.IgnoreCase"/> plus
    /// <see cref="CompareOptions.IgnoreNonSpace"/>, applied through the current
    /// culture so the tailoring matches. <see cref="PrimaryComparer"/> caches the
    /// per-culture <see cref="CompareInfo"/>. One divergence is unavoidable and is
    /// pinned by StartsWithIgnoringCase_KnownFullWidthDivergence: ICU gives fullwidth
    /// letters primary weights distinct from their ASCII counterparts, while .NET's
    /// linguistic comparison folds them together.
    /// </para>
    /// </remarks>
    /// <param name="text">the full text starting with a prefix</param>
    /// <param name="prefix">the full text starting with a prefix</param>
    /// <returns>true only if text starts with the prefix</returns>
    public static bool StartsWithIgnoringCase(string text, string prefix)
    {
        if (text.Length < prefix.Length)
        {
            return false;
        }

        return PrimaryComparer.Compare(text, 0, prefix, 0, prefix.Length) == 0;
    }

    /// <summary>
    /// Reads one code point starting at <paramref name="i"/> and advances it past
    /// that code point. Mirrors java.lang.String.codePointAt: a valid surrogate pair
    /// yields the astral code point and advances two characters, an unpaired
    /// surrogate yields the surrogate code unit itself and advances one character.
    /// </summary>
    internal static int ReadCodePoint(string s, ref int i)
    {
        int cp = s[i];
        if (i + 1 < s.Length)
        {
            int next = s[i + 1];
            if (cp >= 0xD800 && cp <= 0xDBFF && next >= 0xDC00 && next <= 0xDFFF)
            {
                i += 2;
                return 0x10000 + ((cp - 0xD800) << 10) + (next - 0xDC00);
            }
        }

        i += 1;
        return cp;
    }

    /// <summary>
    /// Appends a code point, which may need a surrogate pair.
    /// </summary>
    internal static void AppendCodePoint(StringBuilder builder, int cp)
    {
        if (cp is > 0xFFFF and <= 0x10FFFF)
        {
            int v = cp - 0x10000;
            builder.Append((char)(0xD800 + (v >> 10)));
            builder.Append((char)(0xDC00 + (v & 0x3FF)));
        }
        else
        {
            builder.Append((char)cp);
        }
    }

    private static int CodePointCount(string s, int beginIndex, int end)
    {
        int count = 0;
        for (int i = beginIndex; i < end;)
        {
            ReadCodePoint(s, ref i);
            count++;
        }

        return count;
    }

    private static int ReadCodePointAt(string s, int index)
    {
        int i = index;
        return ReadCodePoint(s, ref i);
    }

    private static int ReadCodePointBefore(string s, int index)
    {
        if (index > 1
            && (int)s[index - 1] is >= 0xDC00 and <= 0xDFFF
            && (int)s[index - 2] is >= 0xD800 and <= 0xDBFF)
        {
            return 0x10000 + ((s[index - 2] - 0xD800) << 10) + (s[index - 1] - 0xDC00);
        }

        return s[index - 1];
    }

    private static int CodePointLengthAt(string s, int index)
        => index < s.Length && (int)s[index] is >= 0xD800 and <= 0xDBFF ? 2 : 1;

    private static bool IsSurrogatePair(char first, char second)
        => (int)first is >= 0xD800 and <= 0xDBFF && (int)second is >= 0xDC00 and <= 0xDFFF;

    private static int ParseUnsignedInt(string s, int start, int end, int radix)
    {
        long value = 0;
        for (int i = start; i < end; i++)
        {
            // The table stops at 'f', and the reference asks Integer.parseUnsignedInt, which
            // refuses any character that is not a digit and reports it as a format error. Without
            // the length test a character outside the table is an index out of range instead,
            // and the caller turns that into nothing at all.
            char c = s[i];
            int digit = c < HexDecode.Length ? HexDecode[c] : -1;
            if (digit < 0)
            {
                throw new FormatException();
            }

            value = value * radix + digit;
            if (value > uint.MaxValue)
            {
                throw new OverflowException();
            }
        }

        return unchecked((int)value);
    }
}
/// <summary>
/// Approximates <c>java.text.Collator</c> at <c>Collator.PRIMARY</c> strength for
/// <see cref="StringUtils.StartsWithIgnoringCase"/>.
/// </summary>
/// <remarks>
/// PRIMARY strength discards case and accents, which maps onto
/// <see cref="CompareOptions.IgnoreCase"/> combined with
/// <see cref="CompareOptions.IgnoreNonSpace"/>. ICU applies the tailoring of the
/// default <c>FORMAT</c> locale, so the comparer is resolved from
/// <see cref="CultureInfo.CurrentCulture"/> and cached per culture name rather than
/// being hard-coded to the invariant culture.
/// <para>
/// Caching is a single volatile slot on purpose: the comparer is consulted at most a
/// handful of times per identifier comparison, and a <see cref="ConcurrentDictionary"/>
/// would allocate a node per culture and pay a lookup hash for every call. The slot is
/// replaced atomically so a concurrent swap cannot publish a partially built comparer.
/// </para>
/// </remarks>
internal static class PrimaryComparer
{
    private const CompareOptions Options =
        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private static readonly CultureInfo FallbackCulture = CultureInfo.InvariantCulture;

    private static CompareInfo? cached;
    private static string? cachedName;

    /// <summary>
    /// Compares two spans with PRIMARY-strength semantics, i.e. ignoring case and
    /// non-spacing marks.
    /// </summary>
    /// <param name="left">the left text</param>
    /// <param name="leftStart">the offset of the left text</param>
    /// <param name="right">the right text</param>
    /// <param name="rightStart">the offset of the right text</param>
    /// <param name="length">the number of characters to compare</param>
    /// <returns>a negative value, zero or a positive value as usual</returns>
    internal static int Compare(string left, int leftStart, string right, int rightStart, int length)
    {
        return Resolved.Compare(
            left.AsSpan(leftStart, length), right.AsSpan(rightStart, length), Options);
    }

    private static CompareInfo Resolved
    {
        get
        {
            CultureInfo culture = CultureInfo.CurrentCulture;
            string name = culture.Name;

            // Fast path: same string instance as last time avoids the Name lookup.
            if (ReferenceEquals(name, cachedName) || name == cachedName)
            {
                return cached!;
            }

            CompareInfo info = culture.CompareInfo;
            // Publish the name first: a reader that observes it is guaranteed to see a
            // fully constructed CompareInfo, because CompareInfo is immutable and the
            // assignment below cannot be reordered before the name by the volatile write.
            cachedName = name;
            Volatile.Write(ref cached, info);
            return info;
        }
    }
}
