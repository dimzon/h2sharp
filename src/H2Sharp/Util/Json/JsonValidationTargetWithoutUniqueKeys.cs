/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.util.json.JSONValidationTargetWithoutUniqueKeys.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the reference keeps a ByteStack of markers; a list of bytes does the same job.
 */
using System;
using System.Collections.Generic;

namespace H2Sharp.Util.Json;

/// <summary>
/// JSON validation target that allows repeated keys.
/// </summary>
public sealed class JsonValidationTargetWithoutUniqueKeys : JsonValidationTarget
{
    /// <summary>
    /// A JSON object.
    /// </summary>
    private const byte Object = 1;

    /// <summary>
    /// A JSON array.
    /// </summary>
    private const byte Array = 2;

    /// <summary>
    /// What the document turned out to be, once its outermost container has closed.
    /// </summary>
    private JsonItemType? type;

    private readonly List<byte> stack;

    private bool needSeparator;

    private bool afterName;

    /// <summary>
    /// Creates a target.
    /// </summary>
    public JsonValidationTargetWithoutUniqueKeys()
    {
        stack = [];
    }

    /// <inheritdoc/>
    public override void StartObject()
    {
        BeforeValue();
        afterName = false;
        stack.Add(Object);
    }

    /// <inheritdoc/>
    public override void EndObject()
    {
        if (afterName || Pop() != Object)
        {
            throw new InvalidOperationException();
        }

        AfterValue(JsonItemType.Object);
    }

    /// <inheritdoc/>
    public override void StartArray()
    {
        BeforeValue();
        afterName = false;
        stack.Add(Array);
    }

    /// <inheritdoc/>
    public override void EndArray()
    {
        if (Pop() != Array)
        {
            throw new InvalidOperationException();
        }

        AfterValue(JsonItemType.Array);
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
    }

    /// <inheritdoc/>
    public override void ValueNull()
    {
        BeforeValue();
        AfterValue(JsonItemType.Scalar);
    }

    /// <inheritdoc/>
    public override void ValueFalse()
    {
        BeforeValue();
        AfterValue(JsonItemType.Scalar);
    }

    /// <inheritdoc/>
    public override void ValueTrue()
    {
        BeforeValue();
        AfterValue(JsonItemType.Scalar);
    }

    /// <inheritdoc/>
    public override void ValueNumber(BigDecimal number)
    {
        BeforeValue();
        AfterValue(JsonItemType.Scalar);
    }

    /// <inheritdoc/>
    public override void ValueString(string @string)
    {
        BeforeValue();
        AfterValue(JsonItemType.Scalar);
    }

    private void BeforeValue()
    {
        if (!afterName && Peek() == Object)
        {
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

    /// <remarks>
    /// The type is remembered only while the stack is empty, which is the moment the outermost
    /// container has closed. A nested value must not overwrite what the document as a whole is.
    /// </remarks>
    private void AfterValue(JsonItemType itemType)
    {
        needSeparator = true;
        afterName = false;
        if (stack.Count == 0)
        {
            type = itemType;
        }
    }

    /// <inheritdoc/>
    public override bool IsPropertyExpected() => !afterName && Peek() == Object;

    /// <inheritdoc/>
    public override bool IsValueSeparatorExpected() => needSeparator;

    /// <inheritdoc/>
    /// <remarks>
    /// An unfinished container is an error, and so is nothing at all: the reference answers both the
    /// same way, and a null result would read as "a valid document of no particular kind".
    /// </remarks>
    public override object? GetResult()
    {
        if (stack.Count != 0 || type is null)
        {
            throw new InvalidOperationException();
        }

        return type;
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
