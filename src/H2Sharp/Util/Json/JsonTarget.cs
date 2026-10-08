/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.util.json.JSONTarget.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */

namespace H2Sharp.Util.Json;

/// <summary>
/// Abstract JSON output target: everything a JSON value can be, written one call at a time.
/// </summary>
/// <remarks>
/// The last two members are questions the parser asks rather than things it tells. A target that
/// is only being told a value does not care about them, and one that is being parsed needs both:
/// "is a name expected" is what tells an object from an array, and "is a comma expected" is what
/// tells the parser that the previous value has ended.
/// </remarks>
public abstract class JsonTarget
{
    /// <summary>
    /// Start of an object.
    /// </summary>
    public abstract void StartObject();

    /// <summary>
    /// End of the current object.
    /// </summary>
    public abstract void EndObject();

    /// <summary>
    /// Start of an array.
    /// </summary>
    public abstract void StartArray();

    /// <summary>
    /// End of the current array.
    /// </summary>
    public abstract void EndArray();

    /// <summary>
    /// Name of a member.
    /// </summary>
    /// <param name="name">the name</param>
    public abstract void Member(string name);

    /// <summary>
    /// Parse "null".
    /// </summary>
    public abstract void ValueNull();

    /// <summary>
    /// Parse "false".
    /// </summary>
    public abstract void ValueFalse();

    /// <summary>
    /// Parse "true".
    /// </summary>
    public abstract void ValueTrue();

    /// <summary>
    /// A number value.
    /// </summary>
    /// <param name="number">the number</param>
    public abstract void ValueNumber(BigDecimal number);

    /// <summary>
    /// A string value.
    /// </summary>
    /// <param name="string">the string</param>
    public abstract void ValueString(string @string);

    /// <summary>
    /// Returns whether a member's name or the end of the current object is expected.
    /// </summary>
    /// <returns>true if it is, false otherwise</returns>
    public abstract bool IsPropertyExpected();

    /// <summary>
    /// Returns whether a value separator is expected before the next member or value.
    /// </summary>
    /// <returns>true if it is, false otherwise</returns>
    public abstract bool IsValueSeparatorExpected();

    /// <summary>
    /// Returns the result.
    /// </summary>
    /// <returns>the result</returns>
    public abstract object? GetResult();
}
