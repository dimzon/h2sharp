/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.util.json.JSONByteArrayTarget.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Message;

namespace H2Sharp.Util.Json;

/// <summary>
/// JSON byte array target: writes the value as UTF-8 bytes.
/// </summary>
/// <remarks>
/// This is the shape a stored JSON value has, so unlike <see cref="JsonStringTarget"/> it is not a
/// rendering of the value for reading: it is the value. Two consequences follow from that and are
/// the reason this is not built on <see cref="JsonStjTarget"/>.
/// <para>
/// It has no ASCII-only mode. The apostrophe is a legal JSON character and needs no escape in bytes,
/// so the second column of the text target has no counterpart here, and asking for one would be
/// inventing a setting the reference does not have.
/// </para>
/// <para>
/// An unpaired surrogate is refused rather than replaced. The bytes cannot hold it, and the two ways
/// of dealing with that — substituting U+FFFD, which changes the string silently, or failing — are
/// not the same choice, and the reference chose to fail. A writer that quietly substitutes would
/// corrupt a JSON column without a trace.
/// </para>
/// </remarks>
public sealed class JsonByteArrayTarget : JsonTarget<byte[]>
{
    /// <summary>
    /// The hex characters.
    /// </summary>
    private const string Hex = "0123456789abcdef";

    /// <summary>
    /// A JSON object.
    /// </summary>
    private const byte Object = 1;

    /// <summary>
    /// A JSON array.
    /// </summary>
    private const byte Array = 2;

    /// <summary>
    /// How many bytes are gathered before the buffer is handed to the stream.
    /// </summary>
    /// <remarks>
    /// One <c>WriteByte</c> per character is a virtual call per character, and a JSON string is
    /// mostly ordinary ASCII. Gathering the runs and writing them once is the difference between a
    /// virtual call per character and one per buffer.
    /// </remarks>
    private const int BufferSize = 256;

    /// <summary>
    /// The capacity a target starts with when the caller has no better guess.
    /// </summary>
    /// <remarks>
    /// A stream that starts empty and doubles its way to a document size copies itself once per
    /// doubling, and that copying showed up as the largest single cost in the profile.
    /// </remarks>
    internal const int DefaultCapacity = 1024;

    /// <summary>
    /// Encodes a JSON string and appends it to the specified stream.
    /// </summary>
    /// <param name="stream">the stream to append to</param>
    /// <param name="s">the string to encode</param>
    /// <returns>the specified stream</returns>
    /// <exception cref="H2SharpException">the string contains an unpaired surrogate</exception>
    public static MemoryStream EncodeString(MemoryStream stream, string s)
    {
        Span<byte> buffer = stackalloc byte[BufferSize];
        int buffered = 0;

        buffer[buffered++] = (byte)'"';
        for (int i = 0, length = s.Length; i < length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '\b':
                    Flush(stream, buffer, ref buffered);
                    WriteAscii(stream, "\\b");
                    break;
                case '\t':
                    Flush(stream, buffer, ref buffered);
                    WriteAscii(stream, "\\t");
                    break;
                case '\f':
                    Flush(stream, buffer, ref buffered);
                    WriteAscii(stream, "\\f");
                    break;
                case '\n':
                    Flush(stream, buffer, ref buffered);
                    WriteAscii(stream, "\\n");
                    break;
                case '\r':
                    Flush(stream, buffer, ref buffered);
                    WriteAscii(stream, "\\r");
                    break;
                case '"':
                    Flush(stream, buffer, ref buffered);
                    WriteAscii(stream, "\\\"");
                    break;
                case '\\':
                    Flush(stream, buffer, ref buffered);
                    WriteAscii(stream, "\\\\");
                    break;
                default:
                    if (c < ' ')
                    {
                        // The only escapes left are the control characters, and they always have
                        // four digits after the backslash.
                        Flush(stream, buffer, ref buffered);
                        WriteAscii(stream, "\\u00");
                        stream.WriteByte((byte)Hex[(c >> 4) & 0xf]);
                        stream.WriteByte((byte)Hex[c & 0xf]);
                    }
                    else if (c < 0x80)
                    {
                        buffer[buffered++] = (byte)c;
                        if (buffered == buffer.Length)
                        {
                            Flush(stream, buffer, ref buffered);
                        }
                    }
                    else if (c < 0x800)
                    {
                        Flush(stream, buffer, ref buffered);
                        stream.WriteByte((byte)(0xc0 | c >> 6));
                        stream.WriteByte((byte)(0x80 | c & 0x3f));
                    }
                    else if (!char.IsSurrogate(c))
                    {
                        Flush(stream, buffer, ref buffered);
                        stream.WriteByte((byte)(0xe0 | c >> 12));
                        stream.WriteByte((byte)(0x80 | c >> 6 & 0x3f));
                        stream.WriteByte((byte)(0x80 | c & 0x3f));
                    }
                    else
                    {
                        // The reference builds UTF-8 by hand rather than calling an encoder, and
                        // the reason shows up right here: a lone surrogate is not encodable and
                        // there is nothing a standard encoder would do that is worth copying.
                        char c2;
                        if (!char.IsHighSurrogate(c) || ++i >= length || !char.IsLowSurrogate(c2 = s[i]))
                        {
                            throw DbException.Get(ErrorCode.DataConversionError1, Truncate(s));
                        }

                        int uc = char.ConvertToUtf32(c, c2);
                        Flush(stream, buffer, ref buffered);
                        stream.WriteByte((byte)(0xf0 | uc >> 18));
                        stream.WriteByte((byte)(0x80 | uc >> 12 & 0x3f));
                        stream.WriteByte((byte)(0x80 | uc >> 6 & 0x3f));
                        stream.WriteByte((byte)(0x80 | uc & 0x3f));
                    }

                    break;
            }
        }

        Flush(stream, buffer, ref buffered);
        stream.WriteByte((byte)'"');
        return stream;
    }

    /// <summary>
    /// A long string does not fit in an error message, and the reference cuts it. The cut is worth
    /// keeping: without it the message would carry the whole string, which for a JSON document can
    /// be megabytes.
    /// </summary>
    /// <param name="s">the string</param>
    /// <returns>the string, or its beginning</returns>
    private static string Truncate(string s) => s.Length > 80 ? s[..80] + "..." : s;

    /// <summary>
    /// Hands the gathered characters to the stream.
    /// </summary>
    /// <param name="stream">the stream</param>
    /// <param name="buffer">the gather buffer</param>
    /// <param name="buffered">how much of it is in use, reset here</param>
    private static void Flush(MemoryStream stream, Span<byte> buffer, ref int buffered)
    {
        if (buffered > 0)
        {
            stream.Write(buffer[..buffered]);
            buffered = 0;
        }
    }

    private static void WriteAscii(MemoryStream stream, string ascii)
    {
        foreach (char ch in ascii)
        {
            stream.WriteByte((byte)ch);
        }
    }

    private readonly MemoryStream stream;

    private readonly List<byte> stack;

    private bool needSeparator;

    private bool afterName;

    /// <summary>
    /// Creates a new target.
    /// </summary>
    public JsonByteArrayTarget()
        : this(DefaultCapacity)
    {
    }

    /// <summary>
    /// Creates a new target that expects roughly so many bytes.
    /// </summary>
    /// <param name="initialCapacity">
    /// the expected size. Normalizing a document produces something close to what went in, so
    /// passing the input length skips the doubling entirely
    /// </param>
    public JsonByteArrayTarget(int initialCapacity)
    {
        stream = new MemoryStream(initialCapacity > 0 ? initialCapacity : DefaultCapacity);
        stack = [];
    }

    /// <inheritdoc/>
    public override void StartObject()
    {
        BeforeValue();
        afterName = false;
        stack.Add(Object);
        stream.WriteByte((byte)'{');
    }

    /// <inheritdoc/>
    public override void EndObject()
    {
        if (afterName || Pop() != Object)
        {
            throw new InvalidOperationException();
        }

        stream.WriteByte((byte)'}');
        AfterValue();
    }

    /// <inheritdoc/>
    public override void StartArray()
    {
        BeforeValue();
        afterName = false;
        stack.Add(Array);
        stream.WriteByte((byte)'[');
    }

    /// <inheritdoc/>
    public override void EndArray()
    {
        if (Pop() != Array)
        {
            throw new InvalidOperationException();
        }

        stream.WriteByte((byte)']');
        AfterValue();
    }

    /// <inheritdoc/>
    public override void Member(string name)
    {
        if (afterName || Peek() != Object)
        {
            throw new InvalidOperationException();
        }

        afterName = true;
        BeforeValue();
        _ = EncodeString(stream, name);
        stream.WriteByte((byte)':');
    }

    /// <inheritdoc/>
    public override void ValueNull()
    {
        BeforeValue();
        WriteAscii(stream, "null");
        AfterValue();
    }

    /// <inheritdoc/>
    public override void ValueFalse()
    {
        BeforeValue();
        WriteAscii(stream, "false");
        AfterValue();
    }

    /// <inheritdoc/>
    public override void ValueTrue()
    {
        BeforeValue();
        WriteAscii(stream, "true");
        AfterValue();
    }

    /// <inheritdoc/>
    public override void ValueNumber(BigDecimal number)
    {
        BeforeValue();
        string s = number.ToString();

        // A Java decimal writes an exponent with a plus, and JSON's grammar does not have one.
        // Dropping it is the whole difference between the two renderings.
        int index = s.IndexOf('E', StringComparison.Ordinal);
        if (index >= 0 && s[++index] == '+')
        {
            s = string.Concat(s.AsSpan(0, index), s.AsSpan(index + 1));
        }

        WriteAscii(stream, s);
        AfterValue();
    }

    /// <inheritdoc/>
    public override void ValueString(string @string)
    {
        BeforeValue();
        _ = EncodeString(stream, @string);
        AfterValue();
    }

    private void BeforeValue()
    {
        if (!afterName && Peek() == Object)
        {
            // Inside an object and not after a name: two values with no name between them.
            throw new InvalidOperationException();
        }

        if (needSeparator)
        {
            if (stack.Count == 0)
            {
                throw new InvalidOperationException();
            }

            needSeparator = false;
            stream.WriteByte((byte)',');
        }
    }

    private void AfterValue()
    {
        needSeparator = true;
        afterName = false;
    }

    /// <inheritdoc/>
    public override bool IsPropertyExpected() => !afterName && Peek() == Object;

    /// <inheritdoc/>
    public override bool IsValueSeparatorExpected() => needSeparator;

    /// <inheritdoc/>
    /// <remarks>
    /// An unfinished container is an error, and so is no output at all: the reference answers both
    /// with the same exception, and an empty result would be a valid-looking empty document.
    /// </remarks>
    protected override byte[]? GetTypedResult()
    {
        if (stack.Count != 0 || stream.Length == 0)
        {
            throw new InvalidOperationException();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// The innermost open object or array, or zero when there is none. The reference's
    /// <c>peek(-1)</c> returns zero for an empty stack rather than failing, and every caller wants
    /// that: "am I inside an object" is false at the top level, which is a question.
    /// </summary>
    /// <returns>the marker</returns>
    private byte Peek() => stack.Count == 0 ? (byte)0 : stack[^1];

    private byte Pop()
    {
        if (stack.Count == 0)
        {
            throw new InvalidOperationException();
        }

        byte last = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return last;
    }
}
