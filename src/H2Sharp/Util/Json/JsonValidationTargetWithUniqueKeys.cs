/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.util.json.JSONValidationTargetWithUniqueKeys.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Modified: the reference pushes a HashSet for an object and Collections.emptyList() for an array
 * onto one ArrayDeque<Object> and tells them apart with instanceof. Here a stack of frames says
 * the same thing directly, and it says it of the innermost container, which is the whole point:
 * an array nested inside an object must not be mistaken for the object.
 */
using System;
using System.Collections.Generic;

namespace H2Sharp.Util.Json;

/// <summary>
/// JSON validation target that refuses repeated keys inside an object.
/// </summary>
/// <remarks>
/// Two things here are not what they look like. An array is transparent to key checking: it is a
/// frame with no set at all, so a repeated name inside an array is not an error and only an object
/// has a set. And the duplicate is detected when the **value** finishes, not when the name is read —
/// so <c>{"a":1,"a":2}</c> fails on the second value, while <c>{"a":1,"a":}</c> fails earlier, on the
/// missing value. The difference matters because it decides which of the two errors a malformed
/// document produces.
/// </remarks>
public sealed class JsonValidationTargetWithUniqueKeys : JsonValidationTarget
{
    /// <summary>
    /// One open container: what kind it is, and, for an object, the names it has already seen.
    /// </summary>
    private sealed class Frame
    {
        internal bool IsObject { get; init; }

        /// <summary>Non-null only for an object.</summary>
        internal HashSet<string>? Seen { get; init; }
    }

    private readonly List<Frame> stack = [];

    /// <summary>
    /// The name each open container sits under, or null at the top level. Without it the name a
    /// container was the value of is lost on the way in, and the value that arrives on the way out
    /// has nothing to be added to.
    /// </summary>
    private readonly List<string?> names = [];

    private bool needSeparator;

    private string? memberName;

    private JsonItemType? type;

    /// <summary>
    /// Creates a target.
    /// </summary>
    public JsonValidationTargetWithUniqueKeys()
    {
    }

    /// <inheritdoc/>
    public override void StartObject()
    {
        BeforeValue();
        names.Add(memberName);
        memberName = null;
        stack.Add(new Frame { IsObject = true, Seen = [] });
    }

    /// <inheritdoc/>
    public override void EndObject()
    {
        // A name with no value after it: the document ends in the middle of a member.
        if (memberName is not null || !InnermostIsObject())
        {
            throw new InvalidOperationException();
        }

        stack.RemoveAt(stack.Count - 1);
        memberName = PopName();
        AfterValue(JsonItemType.Object);
    }

    /// <inheritdoc/>
    public override void StartArray()
    {
        BeforeValue();
        names.Add(memberName);
        memberName = null;
        stack.Add(new Frame { IsObject = false });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// No <c>afterName</c> check here, and that is not an oversight. An array has no names, so
    /// nothing can be left hanging inside one.
    /// </remarks>
    public override void EndArray()
    {
        if (stack.Count == 0 || stack[^1].IsObject)
        {
            throw new InvalidOperationException();
        }

        stack.RemoveAt(stack.Count - 1);
        memberName = PopName();
        AfterValue(JsonItemType.Array);
    }

    /// <summary>
    /// The name the container that just closed was the value of.
    /// </summary>
    /// <returns>the name, or null at the top level</returns>
    private string? PopName()
    {
        if (names.Count == 0)
        {
            throw new InvalidOperationException();
        }

        string? name = names[^1];
        names.RemoveAt(names.Count - 1);
        return name;
    }

    /// <inheritdoc/>
    public override void Member(string name)
    {
        // Not inside an object, or the previous name has not taken a value yet.
        if (memberName is not null || !InnermostIsObject())
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

    /// <remarks>
    /// "Is anything nested inside me an object" would be a different question and the wrong one: an
    /// array inside an object is not an object, and asking about the innermost container is what
    /// keeps <c>{"a":[1]}</c> from reading as a member of the outer object.
    /// </remarks>
    private void BeforeValue()
    {
        // Inside an object and no name has been read: two values with nothing between them.
        if (memberName is null && InnermostIsObject())
        {
            throw new InvalidOperationException();
        }

        if (needSeparator)
        {
            if (names.Count == 0)
            {
                throw new InvalidOperationException();
            }

            needSeparator = false;
        }
    }

    /// <remarks>
    /// The name goes into the set <b>here</b>, when the value it belongs to has finished. Reading
    /// the name is not enough: a name whose value never arrives is a different error, and reporting
    /// it as a duplicate would point at the wrong problem.
    /// </remarks>
    private void AfterValue(JsonItemType itemType)
    {
        if (stack.Count == 0)
        {
            type = itemType;
        }
        else if (InnermostIsObject() && !stack[^1].Seen!.Add(memberName ?? string.Empty))
        {
            throw new InvalidOperationException();
        }

        needSeparator = true;
        memberName = null;
    }

    /// <summary>
    /// Whether the innermost open container is an object. An empty stack answers false: at the top
    /// level "am I inside an object" is a question with the answer no.
    /// </summary>
    /// <returns>true for an object</returns>
    private bool InnermostIsObject() => stack.Count > 0 && stack[^1].IsObject;

    /// <inheritdoc/>
    public override bool IsPropertyExpected() => memberName is null && InnermostIsObject();

    /// <inheritdoc/>
    public override bool IsValueSeparatorExpected() => needSeparator;

    /// <inheritdoc/>
    public override object? GetResult()
    {
        if (names.Count != 0 || type is null)
        {
            throw new InvalidOperationException();
        }

        return type;
    }
}
