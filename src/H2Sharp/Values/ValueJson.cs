/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.value.ValueJson.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.IO;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Message;
using H2Sharp.Util;
using H2Sharp.Util.Json;
using H2Sharp.Values;

namespace H2Sharp.Values;

/// <summary>
/// Implementation of the JSON data type.
/// </summary>
/// <remarks>
/// The value is the normalized bytes and nothing else: a JSON column holds text that has already been
/// through the parser, so whitespace and formatting are gone and two documents that mean the same
/// thing are the same value. Everything else here is either a shortcut for the three documents short
/// enough to be worth keeping, or a way back into the tree when something has to look inside.
/// </remarks>
public sealed class ValueJson : ValueBytesBase
{
    private static readonly byte[] NullBytes = "null"u8.ToArray();

    private static readonly byte[] TrueBytes = "true"u8.ToArray();

    private static readonly byte[] FalseBytes = "false"u8.ToArray();

    /// <summary>
    /// <c>null</c> JSON value.
    /// </summary>
    /// <remarks>
    /// <c>new</c> because <see cref="Value.Null"/> is the SQL NULL and this is the four-byte JSON
    /// document <c>null</c>. They are the same word and different things, and shadowing would let one
    /// stand in for the other by accident.
    /// </remarks>
    public static new readonly ValueJson Null = new(NullBytes);

    /// <summary>
    /// <c>true</c> JSON value.
    /// </summary>
    public static readonly ValueJson True = new(TrueBytes);

    /// <summary>
    /// <c>false</c> JSON value.
    /// </summary>
    public static readonly ValueJson False = new(FalseBytes);

    /// <summary>
    /// <c>0</c> JSON value.
    /// </summary>
    public static readonly ValueJson Zero = new([(byte)'0']);

    /// <summary>
    /// The tree, cached. Parsing a document is not cheap and this is a cache, not a second copy of
    /// the value.
    /// </summary>
    /// <remarks>
    /// The reference holds this in a <c>SoftReference</c>, and so does the cache in
    /// <see cref="Value"/> — with the same note attached there, which is worth repeating here. .NET has
    /// no public soft reference: a weak reference lets the collector drop this "whenever" rather than
    /// "when memory is wanted", and a strong reference would make every JSON value ever looked inside
    /// stay alive as a tree for as long as the database lives. Neither matches, and neither needs to.
    /// The entry here is at most one tree per value, reachable only from that value, and re-parsing
    /// it costs what the first parse cost — so a strong field is bounded by the values the caller is
    /// already holding, and losing the entry would only ever have cost the same work again.
    /// </remarks>
    private JsonValue? decomposition;

    private bool hasDecomposition;

    private ValueJson(byte[] value)
        : base(value)
    {
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
    {
        // The ASCII rendering, and not the stored bytes: a document that stored non-ASCII as itself
        // has to be printed as escapes, or the SQL literal it sits in would depend on the encoding
        // of whoever runs it.
        string s = JsonBytesSource.Parse(value, new JsonStringTarget(true));
        _ = builder.Append("JSON '").Append(s).Append('\'');
        return builder;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The bytes decoded as UTF-8, and that is the text of the value: the normalized bytes *are* the
    /// document. Note this is not the rendering in <see cref="GetSql"/> — there the document goes
    /// through the ASCII target, because it is about to become a SQL literal.
    /// </remarks>
    public override string? GetString() => Encoding.UTF8.GetString(value);

    /// <inheritdoc/>
    public override TypeInfo GetTypeInfo() => TypeInfo.TypeJson;

    /// <inheritdoc/>
    public override int GetValueType() => Value.Json;

    /// <summary>
    /// Returns JSON item type.
    /// </summary>
    /// <returns>JSON item type</returns>
    /// <remarks>
    /// Read off the first byte without looking at the rest, so it answers for a malformed document
    /// too. That is deliberate: this is asked about a stored value, and the stored value is whatever
    /// was normalized into it.
    /// </remarks>
    public JsonItemType GetItemType()
        => value[0] switch
        {
            (byte)'[' => JsonItemType.Array,
            (byte)'{' => JsonItemType.Object,
            _ => JsonItemType.Scalar,
        };

    /// <summary>
    /// Returns decomposed value.
    /// </summary>
    /// <returns>decomposed value</returns>
    public JsonValue GetDecomposition()
    {
        if (hasDecomposition)
        {
            return decomposition!;
        }

        decomposition = JsonBytesSource.Parse(value, new JsonValueTarget());
        hasDecomposition = true;
        return decomposition;
    }

    /// <summary>
    /// Returns JSON value with the specified content.
    /// </summary>
    /// <param name="s">JSON representation, will be normalized</param>
    /// <returns>JSON value</returns>
    /// <exception cref="H2SharpException">on invalid JSON</exception>
    public static ValueJson FromJson(string s)
    {
        byte[] bytes;
        try
        {
            bytes = JsonStringSource.Normalize(s);
        }
        catch (Exception)
        {
            // Any failure is a conversion error, and the offending text goes into the message,
            // shortened: a document that is megabytes long would otherwise be copied into an
            // exception message and logged.
            throw DbException.Get(ErrorCode.DataConversionError1, s.Length > 80 ? s[..80] + "..." : s);
        }

        return GetInternal(bytes);
    }

    /// <summary>
    /// Returns JSON value with the specified content.
    /// </summary>
    /// <param name="bytes">JSON representation, will be normalized</param>
    /// <returns>JSON value</returns>
    /// <exception cref="H2SharpException">on invalid JSON</exception>
    public static ValueJson FromJson(byte[] bytes)
    {
        try
        {
            bytes = JsonBytesSource.Normalize(bytes);
        }
        catch (Exception)
        {
            StringBuilder message = new();
            _ = message.Append("X'");
            if (bytes.Length > 40)
            {
                _ = message.Append(StringUtils.ConvertBytesToHex(bytes, 40)).Append("...");
            }
            else
            {
                _ = message.Append(StringUtils.ConvertBytesToHex(bytes));
            }

            throw DbException.Get(ErrorCode.DataConversionError1, message.Append('\'').ToString());
        }

        return GetInternal(bytes);
    }

    /// <summary>
    /// Returns JSON value with the specified content.
    /// </summary>
    /// <param name="value">JSON</param>
    /// <returns>JSON value</returns>
    public static ValueJson FromJson(JsonValue value)
    {
        if (value is JsonNull)
        {
            return Null;
        }

        if (value is JsonBoolean boolean)
        {
            return boolean.GetBoolean() ? True : False;
        }

        if (value is JsonNumber number
            && number.GetBigDecimal().Equals(BigDecimal.Zero))
        {
            return Zero;
        }

        JsonByteArrayTarget target = new();
        value.AddTo(target);
        ValueJson result = new((byte[])target.GetResult()!);

        // The tree the caller handed over is already the decomposition, so hold on to it rather
        // than parsing the bytes that were just written from it.
        result.decomposition = value;
        result.hasDecomposition = true;
        return result;
    }

    /// <summary>
    /// Returns JSON value with the specified boolean content.
    /// </summary>
    /// <param name="bool">boolean value</param>
    /// <returns>JSON value</returns>
    public static ValueJson Get(bool @bool) => @bool ? True : False;

    /// <summary>
    /// Returns JSON value with the specified numeric content.
    /// </summary>
    /// <param name="number">integer value</param>
    /// <returns>JSON value</returns>
    public static ValueJson Get(int number) => number != 0 ? GetNumber(number.ToString()) : Zero;

    /// <summary>
    /// Returns JSON value with the specified numeric content.
    /// </summary>
    /// <param name="number">long value</param>
    /// <returns>JSON value</returns>
    public static ValueJson Get(long number) => number != 0L ? GetNumber(number.ToString()) : Zero;

    /// <summary>
    /// Returns JSON value with the specified numeric content.
    /// </summary>
    /// <param name="number">big decimal value</param>
    /// <returns>JSON value</returns>
    public static ValueJson Get(BigDecimal number)
    {
        // Zero with a scale is not the same document as zero: 0.00 prints differently and means a
        // different type of thing, so only the unscaled zero is the shared instance.
        if (number.Signum() == 0 && number.Scale == 0)
        {
            return Zero;
        }

        string s = number.ToString();

        // A Java decimal writes an exponent with a plus, and JSON's grammar does not have one.
        int index = s.IndexOf('E', StringComparison.Ordinal);
        if (index >= 0 && s[++index] == '+')
        {
            s = string.Concat(s.AsSpan(0, index), s.AsSpan(index + 1));
        }

        return GetNumber(s);
    }

    /// <summary>
    /// Returns JSON value with the specified string content.
    /// </summary>
    /// <param name="string">string value</param>
    /// <returns>JSON value</returns>
    public static ValueJson Get(string @string)
    {
        // Sized exactly: escapes would overflow a length-plus-two guess and grow the stream.
        using MemoryStream stream = new(JsonByteArrayTarget.EncodedLength(@string));
        return new ValueJson(JsonByteArrayTarget.EncodeString(stream, @string).ToArray());
    }

    /// <summary>
    /// Returns JSON value with the specified content.
    /// </summary>
    /// <param name="bytes">normalized JSON representation</param>
    /// <returns>JSON value</returns>
    public static ValueJson GetInternal(byte[] bytes)
        => bytes.Length switch
        {
            // The four documents short enough to be worth keeping. Everything else is a new value,
            // and the comparison is on the bytes because that is the whole of what is stored.
            1 => bytes[0] == (byte)'0' ? Zero : new ValueJson(bytes),
            4 => Match(bytes, TrueBytes, True)
                ?? Match(bytes, NullBytes, Null)
                ?? new ValueJson(bytes),
            5 => Match(bytes, FalseBytes, False) ?? new ValueJson(bytes),
            _ => new ValueJson(bytes),
        };

    private static ValueJson? Match(byte[] bytes, byte[] candidate, ValueJson known)
    {
        for (int i = 0; i < candidate.Length; i++)
        {
            if (bytes[i] != candidate[i])
            {
                return null;
            }
        }

        return known;
    }

    private static ValueJson GetNumber(string s) => new(Encoding.Latin1.GetBytes(s));

    /// <inheritdoc/>
    public override int GetMemory() => value.Length + 96;
}
