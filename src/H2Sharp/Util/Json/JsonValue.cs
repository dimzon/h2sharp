/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.util.json.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Collections.Generic;
using H2Sharp.Util;

namespace H2Sharp.Util.Json;

/// <summary>
/// A JSON value.
/// </summary>
/// <remarks>
/// A JSON value knows how to write itself into a <see cref="JsonTarget"/>, and nothing else.
/// That is the whole design: one way in (the parser calls the target's methods) and one way out
/// (the value calls them back), so a parser and a printer never have to know about each other
/// and the same value can be written as text, into bytes, or nowhere at all for validation.
/// </remarks>
public abstract class JsonValue
{
    /// <summary>
    /// Appends this value to the specified target.
    /// </summary>
    /// <param name="target">the target</param>
    public abstract void AddTo(JsonTarget target);

    /// <summary>
    /// This value as JSON text.
    /// </summary>
    /// <returns>the JSON text</returns>
    public sealed override string ToString()
    {
        JsonStringTarget target = new();
        AddTo(target);
        return (string)target.GetResult()!;
    }
}

/// <summary>
/// JSON null.
/// </summary>
public sealed class JsonNull : JsonValue
{
    /// <summary>
    /// The only value of this type: null carries nothing, so there is nothing to tell apart.
    /// </summary>
    public static readonly JsonNull Null = new();

    private JsonNull()
    {
    }

    /// <inheritdoc/>
    public override void AddTo(JsonTarget target) => target.ValueNull();
}

/// <summary>
/// JSON boolean.
/// </summary>
public sealed class JsonBoolean : JsonValue
{
    /// <summary>
    /// The <c>false</c> value.
    /// </summary>
    public static readonly JsonBoolean False = new(false);

    /// <summary>
    /// The <c>true</c> value.
    /// </summary>
    public static readonly JsonBoolean True = new(true);

    private readonly bool value;

    private JsonBoolean(bool value)
    {
        this.value = value;
    }

    /// <inheritdoc/>
    public override void AddTo(JsonTarget target)
    {
        if (value)
        {
            target.ValueTrue();
        }
        else
        {
            target.ValueFalse();
        }
    }

    /// <summary>
    /// Returns the value.
    /// </summary>
    /// <returns>the value</returns>
    public bool GetBoolean() => value;
}

/// <summary>
/// JSON number.
/// </summary>
public sealed class JsonNumber : JsonValue
{
    private readonly BigDecimal value;

    internal JsonNumber(BigDecimal value)
    {
        this.value = value;
    }

    /// <inheritdoc/>
    public override void AddTo(JsonTarget target) => target.ValueNumber(value);

    /// <summary>
    /// Returns the value.
    /// </summary>
    /// <returns>the value</returns>
    public BigDecimal GetBigDecimal() => value;
}

/// <summary>
/// JSON string.
/// </summary>
public sealed class JsonString : JsonValue
{
    private readonly string value;

    internal JsonString(string value)
    {
        this.value = value;
    }

    /// <inheritdoc/>
    public override void AddTo(JsonTarget target) => target.ValueString(value);

    /// <summary>
    /// Returns the value.
    /// </summary>
    /// <returns>the value</returns>
    public string GetString() => value;
}

/// <summary>
/// JSON object: a list of named members, in the order they were written.
/// </summary>
/// <remarks>
/// The members are a list rather than a map because JSON allows the same name twice and the
/// order is part of the text. <see cref="GetFirst"/> therefore says "first", because that is
/// the one a lookup by name can mean here.
/// </remarks>
public sealed class JsonObject : JsonValue
{
    private readonly List<KeyValuePair<string, JsonValue>> members = [];

    internal JsonObject()
    {
    }

    /// <summary>
    /// Add a key-value pair.
    /// </summary>
    /// <param name="name">the key</param>
    /// <param name="value">the value</param>
    internal void AddMember(string name, JsonValue value)
        => members.Add(new KeyValuePair<string, JsonValue>(name, value));

    /// <inheritdoc/>
    public override void AddTo(JsonTarget target)
    {
        target.StartObject();
        foreach (KeyValuePair<string, JsonValue> member in members)
        {
            target.Member(member.Key);
            member.Value.AddTo(target);
        }

        target.EndObject();
    }

    /// <summary>
    /// Returns the members, in order.
    /// </summary>
    /// <returns>the members</returns>
    public KeyValuePair<string, JsonValue>[] GetMembers() => [.. members];

    /// <summary>
    /// Returns the value of the first member with the specified name.
    /// </summary>
    /// <param name="name">name of the member</param>
    /// <returns>the value of the first member with the specified name, or null</returns>
    public JsonValue? GetFirst(string name)
    {
        foreach (KeyValuePair<string, JsonValue> entry in members)
        {
            if (string.Equals(name, entry.Key, StringComparison.Ordinal))
            {
                return entry.Value;
            }
        }

        return null;
    }
}

/// <summary>
/// JSON array.
/// </summary>
public sealed class JsonArray : JsonValue
{
    private readonly List<JsonValue> elements = [];

    internal JsonArray()
    {
    }

    /// <summary>
    /// Add a value to the array.
    /// </summary>
    /// <param name="value">the value to add</param>
    internal void AddElement(JsonValue value) => elements.Add(value);

    /// <inheritdoc/>
    public override void AddTo(JsonTarget target)
    {
        target.StartArray();
        foreach (JsonValue element in elements)
        {
            element.AddTo(target);
        }

        target.EndArray();
    }

    /// <summary>
    /// Returns the array length.
    /// </summary>
    /// <returns>the array length</returns>
    public int Length() => elements.Count;

    /// <summary>
    /// Returns the elements.
    /// </summary>
    /// <returns>the elements</returns>
    public JsonValue[] GetArray() => [.. elements];

    /// <summary>
    /// Returns the elements, each converted.
    /// </summary>
    /// <typeparam name="T">the type of elements</typeparam>
    /// <param name="converter">a converter to the requested type</param>
    /// <returns>the converted elements</returns>
    public T[] GetArray<T>(Func<JsonValue, T> converter)
    {
        T[] array = new T[elements.Count];
        for (int i = 0; i < elements.Count; i++)
        {
            array[i] = converter(elements[i]);
        }

        return array;
    }

    /// <summary>
    /// Returns the value at the specified 0-based index, or null.
    /// </summary>
    /// <param name="index">0-based index</param>
    /// <returns>the value at the specified 0-based index, or null</returns>
    public JsonValue? GetElement(int index)
        => index >= 0 && index < elements.Count ? elements[index] : null;
}

/// <summary>
/// JSON item type.
/// </summary>
public enum JsonItemType
{
    /// <summary>
    /// Either <see cref="Array"/>, <see cref="Object"/>, or <see cref="Scalar"/>.
    /// </summary>
    Value,

    /// <summary>
    /// JSON array.
    /// </summary>
    Array,

    /// <summary>
    /// JSON object.
    /// </summary>
    Object,

    /// <summary>
    /// JSON scalar value: string, number, <c>true</c>, <c>false</c>, or <c>null</c>.
    /// </summary>
    Scalar,
}

/// <summary>
/// Whether an item type includes another one, which is what an <c>IS JSON</c> test asks: the
/// column says what it allows and the value says what it is.
/// </summary>
/// <param name="type">the item type that includes</param>
/// <param name="other">the item type to check</param>
/// <returns>whether the first includes the second</returns>
public static class JsonItemTypes
{
    /// <summary>
    /// Checks whether this item type includes the specified item type.
    /// </summary>
    /// <param name="type">the item type that includes</param>
    /// <param name="other">item type to check</param>
    /// <returns>whether this item type includes the specified item type</returns>
    public static bool Includes(this JsonItemType type, JsonItemType other)
        => type == JsonItemType.Value || type == other;
}
