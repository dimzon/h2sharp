/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.util.json.JSONStringSource.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Globalization;
using System.Text;

namespace H2Sharp.Util.Json;

/// <summary>
/// JSON text source that reads from a string.
/// </summary>
public sealed class JsonStringSource : JsonTextSource
{
    /// <summary>
    /// Parses a string to a specified target.
    /// </summary>
    /// <typeparam name="R">the type of the result</typeparam>
    /// <param name="string">source</param>
    /// <param name="target">target</param>
    /// <returns>the result of the target</returns>
    public static R Parse<R>(string @string, JsonTarget<R> target)
    {
        JsonStringSource source = new(@string, target);
        source.Parse();
        return target.Result!;
    }

    /// <summary>
    /// Converts a string into normalized JSON representation.
    /// </summary>
    /// <param name="string">source representation</param>
    /// <returns>normalized representation</returns>
    public static byte[] Normalize(string @string) => Parse(@string, new JsonByteArrayTarget());

    private readonly string text;

    private readonly int length;

    private int index;

    private JsonStringSource(string @string, JsonTarget target)
        : base(target)
    {
        this.text = @string;
        length = @string.Length;
        if (length == 0)
        {
            throw new ArgumentException();
        }

        // Ignore BOM. Exactly one: a second one is content.
        if (@string[0] == '\uFEFF')
        {
            index++;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only four characters are whitespace here. A form feed, a vertical tab and a non-breaking
    /// space are content, and a document that has one in the wrong place is a document that fails.
    /// </remarks>
    protected override int NextCharAfterWhitespace()
    {
        int i = index;
        while (i < length)
        {
            char ch = text[i++];
            switch (ch)
            {
                case '\t':
                case '\n':
                case '\r':
                case ' ':
                    break;
                default:
                    index = i;
                    return ch;
            }
        }

        return EndOfInput;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The keyword's first character has been read already, so only the rest is compared. The
    /// length is not checked here: a short tail simply does not match.
    /// </remarks>
    protected override void ReadKeyword1(string keyword)
    {
        int l = keyword.Length - 1;
        if (!text.AsSpan(index, l).SequenceEqual(keyword.AsSpan(1)))
        {
            throw new ArgumentException();
        }

        index += l;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The text between the digits is handed to <see cref="BigDecimal"/> untouched, which is what
    /// keeps the scale: "1.50" stays "1.50" rather than becoming 1.5. Leading zeros are accepted
    /// here, which JSON forbids and the reference does too.
    /// </remarks>
    protected override void ParseNumber(bool positive)
    {
        int i = index;
        int start = i - 1;
        i = SkipInt(i, positive);
        if (i < length)
        {
            char ch = text[i];
            if (ch == '.')
            {
                i = SkipInt(i + 1, false);
                if (i >= length)
                {
                    goto Done;
                }

                ch = text[i];
            }

            if (ch is 'E' or 'e')
            {
                if (++i >= length)
                {
                    throw new ArgumentException();
                }

                ch = text[i];
                if (ch is '+' or '-')
                {
                    i++;
                }

                i = SkipInt(i, false);
            }
        }

    Done:
        // The span, not a substring: the number has been measured but not moved, and a JSON
        // document is mostly numbers. The whole text goes along for the failure message, which is
        // the same text the reference would have reported.
        Target.ValueNumber(BigDecimal.Parse(text.AsSpan(start, i - start), text));
        index = i;
    }

    /// <summary>
    /// Consume digits. A run of none is an error, which is what rejects "1." and "1.e5".
    /// </summary>
    /// <param name="index">where to start</param>
    /// <param name="hasInt">whether digits have been seen so far</param>
    /// <returns>the position after the digits</returns>
    private int SkipInt(int index, bool hasInt)
    {
        while (index < length)
        {
            char ch = text[index];
            if (ch is >= '0' and <= '9')
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
    protected override int NextChar()
    {
        if (index >= length)
        {
            throw new ArgumentException();
        }

        return text[index++];
    }

    /// <inheritdoc/>
    protected override char ReadHex()
    {
        if (index + 3 >= length)
        {
            throw new ArgumentException();
        }

        if (!ushort.TryParse(
                text.AsSpan(index, 4),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out ushort code))
        {
            throw new ArgumentException();
        }

        index += 4;
        return (char)code;
    }
}
