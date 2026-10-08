/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.util.json.JSONStringTarget.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the structural work goes to Utf8JsonWriter. The reference hand-wrote the braces,
 * the commas and the escaping; here the writer does the first two, and H2JavaScriptEncoder does
 * the third, because those are the parts with no reason to be re-implemented. What is left is what
 * the writer cannot do: a number has to be written from the text BigDecimal renders, because the
 * writer's own number handling has never heard of a 30-digit decimal with a scale of 8.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace H2Sharp.Util.Json;

/// <summary>
/// JSON string target that writes through <see cref="Utf8JsonWriter"/>.
/// </summary>
/// <remarks>
/// Two things are worth knowing about why this exists next to <see cref="JsonStringTarget"/>.
/// First, the escaping is not ours to choose: <see cref="H2JavaScriptEncoder"/> holds the rules and
/// the writer asks it about every character, so the two renderings here and there cannot drift.
/// Second, duplicate keys survive, because the writer is a stream and simply writes each name in
/// the order it is given. The reference keeps them too, and an object model built on a dictionary
/// would have quietly dropped them.
/// </remarks>
public sealed class JsonStjTarget : JsonTarget
{
    /// <summary>
    /// A JSON object.
    /// </summary>
    private const byte Object = 1;

    /// <summary>
    /// A JSON array.
    /// </summary>
    private const byte Array = 2;

    private readonly List<byte> stack;

    private readonly bool asciiPrintableOnly;

    private readonly MemoryStream stream;

    private readonly Utf8JsonWriter writer;

    private bool needSeparator;

    private bool afterName;

    /// <summary>
    /// Creates a target that writes everything, including non-ASCII characters as they are.
    /// </summary>
    public JsonStjTarget()
        : this(false)
    {
    }

    /// <summary>
    /// Creates a target that writes everything.
    /// </summary>
    /// <param name="asciiPrintableOnly">
    /// whether all non-printable, non-ASCII characters, and <c>'</c> characters should be escaped
    /// </param>
    public JsonStjTarget(bool asciiPrintableOnly)
    {
        stack = [];
        this.asciiPrintableOnly = asciiPrintableOnly;
        stream = new MemoryStream();
        JsonWriterOptions options = new()
        {
            Indented = false,
            SkipValidation = true,
            Encoder = new H2JavaScriptEncoder(asciiPrintableOnly),
        };
        writer = new Utf8JsonWriter(stream, options);
    }

    /// <inheritdoc/>
    public override void StartObject()
    {
        BeforeValue();
        afterName = false;
        stack.Add(Object);
        writer.WriteStartObject();
    }

    /// <inheritdoc/>
    public override void EndObject()
    {
        if (afterName || Pop() != Object)
        {
            throw new InvalidOperationException();
        }

        writer.WriteEndObject();
        AfterValue();
    }

    /// <inheritdoc/>
    public override void StartArray()
    {
        BeforeValue();
        afterName = false;
        stack.Add(Array);
        writer.WriteStartArray();
    }

    /// <inheritdoc/>
    public override void EndArray()
    {
        if (Pop() != Array)
        {
            throw new InvalidOperationException();
        }

        writer.WriteEndArray();
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
        writer.WritePropertyName(name);
    }

    /// <inheritdoc/>
    public override void ValueNull()
    {
        BeforeValue();
        writer.WriteNullValue();
        AfterValue();
    }

    /// <inheritdoc/>
    public override void ValueFalse()
    {
        BeforeValue();
        writer.WriteBooleanValue(false);
        AfterValue();
    }

    /// <inheritdoc/>
    public override void ValueTrue()
    {
        BeforeValue();
        writer.WriteBooleanValue(true);
        AfterValue();
    }

    /// <inheritdoc/>
    public override void ValueNumber(BigDecimal number)
    {
        BeforeValue();

        // A Java decimal writes an exponent with a plus, and JSON's grammar does not have one.
        // Dropping it is the whole difference between the two renderings.
        string s = number.ToString();
        int index = s.IndexOf('E', StringComparison.Ordinal);
        if (index >= 0 && s[++index] == '+')
        {
            s = string.Concat(s.AsSpan(0, index), s.AsSpan(index + 1));
        }

        // The raw text, because the writer's own number handling would turn a 30-digit decimal
        // with a scale into a double and back, and the scale is part of the value.
        writer.WriteRawValue(s, skipInputValidation: true);
        AfterValue();
    }

    /// <inheritdoc/>
    public override void ValueString(string @string)
    {
        BeforeValue();
        writer.WriteStringValue(@string);
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
    public override object? GetResult()
    {
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
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
