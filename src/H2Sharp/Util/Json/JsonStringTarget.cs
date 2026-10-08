/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.util.json.JSONStringTarget.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the reference keeps the nesting in a ByteStack of bytes; a list of bytes does the
 * same job here. There are two entries ever live at a time, and the list is only read at its end.
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace H2Sharp.Util.Json;

/// <summary>
/// JSON string target: writes the value as text.
/// </summary>
public sealed class JsonStringTarget : JsonTarget
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
    /// Encodes a JSON string and appends it to the specified string builder.
    /// </summary>
    /// <param name="builder">the string builder to append to</param>
    /// <param name="s">the string to encode</param>
    /// <param name="asciiPrintableOnly">
    /// whether all non-printable, non-ASCII characters, and <c>'</c> characters should be escaped
    /// </param>
    /// <returns>the specified string builder</returns>
    public static StringBuilder EncodeString(StringBuilder builder, string s, bool asciiPrintableOnly)
    {
        _ = builder.Append('"');
        for (int i = 0, length = s.Length; i < length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '\b':
                    _ = builder.Append("\\b");
                    break;
                case '\t':
                    _ = builder.Append("\\t");
                    break;
                case '\f':
                    _ = builder.Append("\\f");
                    break;
                case '\n':
                    _ = builder.Append("\\n");
                    break;
                case '\r':
                    _ = builder.Append("\\r");
                    break;
                case '"':
                    _ = builder.Append("\\\"");
                    break;
                case '\'':
                    // The apostrophe is legal JSON, so it is only escaped when the caller asked
                    // for ASCII-only output — which is the mode a client that cannot read UTF-8
                    // needs.
                    _ = builder.Append(asciiPrintableOnly ? "\\u0027" : "'");
                    break;
                case '\\':
                    _ = builder.Append("\\\\");
                    break;
                default:
                    if (c < ' ')
                    {
                        _ = builder.Append("\\u00")
                            .Append(Hex[(c >> 4) & 0xf])
                            .Append(Hex[c & 0xf]);
                    }
                    else if (!asciiPrintableOnly || c <= 0x7f)
                    {
                        _ = builder.Append(c);
                    }
                    else
                    {
                        _ = builder.Append("\\u")
                            .Append(Hex[(c >> 12) & 0xf])
                            .Append(Hex[(c >> 8) & 0xf])
                            .Append(Hex[(c >> 4) & 0xf])
                            .Append(Hex[c & 0xf]);
                    }

                    break;
            }
        }

        return builder.Append('"');
    }

    private readonly StringBuilder builder;

    private readonly List<byte> stack;

    private readonly bool asciiPrintableOnly;

    private bool needSeparator;

    private bool afterName;

    /// <summary>
    /// Creates a target that writes everything, including non-ASCII characters as they are.
    /// </summary>
    public JsonStringTarget()
        : this(false)
    {
    }

    /// <summary>
    /// Creates a target that writes everything.
    /// </summary>
    /// <param name="asciiPrintableOnly">
    /// whether all non-printable, non-ASCII characters, and <c>'</c> characters should be escaped
    /// </param>
    public JsonStringTarget(bool asciiPrintableOnly)
    {
        builder = new StringBuilder();
        stack = [];
        this.asciiPrintableOnly = asciiPrintableOnly;
    }

    /// <inheritdoc/>
    public override void StartObject()
    {
        BeforeValue();
        afterName = false;
        stack.Add(Object);
        _ = builder.Append('{');
    }

    /// <inheritdoc/>
    public override void EndObject()
    {
        if (afterName || Pop() != Object)
        {
            throw new InvalidOperationException();
        }

        _ = builder.Append('}');
        AfterValue();
    }

    /// <inheritdoc/>
    public override void StartArray()
    {
        BeforeValue();
        afterName = false;
        stack.Add(Array);
        _ = builder.Append('[');
    }

    /// <inheritdoc/>
    public override void EndArray()
    {
        if (Pop() != Array)
        {
            throw new InvalidOperationException();
        }

        _ = builder.Append(']');
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
        _ = EncodeString(builder, name, asciiPrintableOnly).Append(':');
    }

    /// <inheritdoc/>
    public override void ValueNull()
    {
        BeforeValue();
        _ = builder.Append("null");
        AfterValue();
    }

    /// <inheritdoc/>
    public override void ValueFalse()
    {
        BeforeValue();
        _ = builder.Append("false");
        AfterValue();
    }

    /// <inheritdoc/>
    public override void ValueTrue()
    {
        BeforeValue();
        _ = builder.Append("true");
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
            _ = builder.Append(s, 0, index).Append(s, index + 1, s.Length - index - 1);
        }
        else
        {
            _ = builder.Append(s);
        }

        AfterValue();
    }

    /// <inheritdoc/>
    public override void ValueString(string @string)
    {
        BeforeValue();
        _ = EncodeString(builder, @string, asciiPrintableOnly);
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
            _ = builder.Append(',');
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
    public override object? GetResult() => builder.ToString();

    /// <summary>
    /// The innermost open object or array, or zero when there is none.
    /// </summary>
    /// <returns>the marker</returns>
    /// <remarks>
    /// The reference's <c>peek(-1)</c> returns zero for an empty stack rather than failing, and
    /// every caller wants that: "am I inside an object" is false at the top level, which is a
    /// question rather than a mistake.
    /// </remarks>
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
