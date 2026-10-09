/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.util.json.JSONValueTarget.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the reference pushes containers onto one ArrayDeque and tells them apart with
 * instanceof, twice. Here the open containers are a list of the two kinds, and the last entry says
 * which it is, so the type tests have nothing left to do.
 */
using System;
using System.Collections.Generic;

namespace H2Sharp.Util.Json;

/// <summary>
/// JSON target that builds the value tree, and is the only target that does: everything else
/// writes text, bytes, or checks structure and forgets.
/// </summary>
/// <remarks>
/// Repeated keys are kept, and that is the point. A JSON object here is a list of name and value
/// pairs, not a dictionary, so <c>{"a":1,"a":2}</c> survives the round trip instead of losing a
/// member to a map. A caller that wants the first match asks for it, and a caller that wants to
/// reject duplicates uses a validation target instead of finding out afterwards which one this is.
/// </remarks>
public sealed class JsonValueTarget : JsonTarget<JsonValue>
{
    /// <summary>
    /// The open containers, outermost first.
    /// </summary>
    private readonly List<JsonValue> stack = [];

    /// <summary>
    /// The name a container sits under, or null at the top level. A parallel stack, because a
    /// container that closes has to hand its name back to whatever contained it.
    /// </summary>
    private readonly List<string?> names = [];

    private bool needSeparator;

    private string? memberName;

    private JsonValue? result;

    /// <summary>
    /// Creates a target.
    /// </summary>
    public JsonValueTarget()
    {
    }

    /// <inheritdoc/>
    public override void StartObject()
    {
        BeforeValue();
        names.Add(memberName);
        memberName = null;
        stack.Add(new JsonObject());
    }

    /// <inheritdoc/>
    public override void EndObject()
    {
        // A name with no value after it: the document ends in the middle of a member.
        if (memberName is not null || !IsTop(JsonContainer.Object))
        {
            throw new InvalidOperationException();
        }

        JsonObject closed = (JsonObject)Pop();
        if (names.Count == 0)
        {
            throw new InvalidOperationException();
        }

        memberName = names[^1];
        names.RemoveAt(names.Count - 1);
        AfterValue(closed);
    }

    /// <inheritdoc/>
    public override void StartArray()
    {
        BeforeValue();
        names.Add(memberName);
        memberName = null;
        stack.Add(new JsonArray());
    }

    /// <inheritdoc/>
    /// <remarks>
    /// No <c>afterName</c> check, and that is not an oversight: an array has no names, so nothing
    /// can be left hanging inside one.
    /// </remarks>
    public override void EndArray()
    {
        if (!IsTop(JsonContainer.Array))
        {
            throw new InvalidOperationException();
        }

        JsonArray closed = (JsonArray)Pop();
        if (names.Count == 0)
        {
            throw new InvalidOperationException();
        }

        memberName = names[^1];
        names.RemoveAt(names.Count - 1);
        AfterValue(closed);
    }

    /// <inheritdoc/>
    public override void Member(string name)
    {
        // Not inside an object, or the previous name has not taken a value yet.
        if (memberName is not null || !IsTop(JsonContainer.Object))
        {
            throw new InvalidOperationException();
        }

        memberName = name;
        BeforeValue();
    }

    /// <inheritdoc/>
    public override void ValueNull()
    {
        BeforeValue();
        AfterValue(JsonNull.Null);
    }

    /// <inheritdoc/>
    public override void ValueFalse()
    {
        BeforeValue();
        AfterValue(JsonBoolean.False);
    }

    /// <inheritdoc/>
    public override void ValueTrue()
    {
        BeforeValue();
        AfterValue(JsonBoolean.True);
    }

    /// <inheritdoc/>
    public override void ValueNumber(BigDecimal number)
    {
        BeforeValue();
        AfterValue(new JsonNumber(number));
    }

    /// <inheritdoc/>
    public override void ValueString(string @string)
    {
        BeforeValue();
        AfterValue(new JsonString(@string));
    }

    private void BeforeValue()
    {
        // Inside an object and no name has been read: two values with nothing between them.
        if (memberName is null && IsTop(JsonContainer.Object))
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
    /// An empty stack is the moment the document is complete, so that is where the result goes. A
    /// value nested inside anything else is added to its parent instead.
    /// </remarks>
    private void AfterValue(JsonValue value)
    {
        if (stack.Count == 0)
        {
            result = value;
        }
        else if (IsTop(JsonContainer.Object))
        {
            ((JsonObject)stack[^1]).AddMember(memberName!, value);
        }
        else
        {
            ((JsonArray)stack[^1]).AddElement(value);
        }

        needSeparator = true;
        memberName = null;
    }

    /// <inheritdoc/>
    public override bool IsPropertyExpected() => memberName is null && IsTop(JsonContainer.Object);

    /// <inheritdoc/>
    public override bool IsValueSeparatorExpected() => needSeparator;

    /// <inheritdoc/>
    protected override JsonValue? GetTypedResult()
    {
        if (stack.Count != 0 || result is null)
        {
            throw new InvalidOperationException();
        }

        return result;
    }

    /// <summary>
    /// Whether the innermost open container is of the given kind. An empty stack answers false: at
    /// the top level "am I inside an object" is a question with the answer no.
    /// </summary>
    private bool IsTop(JsonContainer kind)
        => stack.Count > 0 && kind == (stack[^1] is JsonObject ? JsonContainer.Object : JsonContainer.Array);

    private JsonValue Pop()
    {
        JsonValue last = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return last;
    }

    /// <summary>
    /// Which kind of container is open. The reference asks this twice per value with
    /// <c>instanceof</c>; here it is one comparison on the last entry of a stack.
    /// </summary>
    private enum JsonContainer
    {
        Object,
        Array,
    }
}
