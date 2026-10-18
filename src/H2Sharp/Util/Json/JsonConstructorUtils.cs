/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.util.json.JsonConstructorUtils.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.IO;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Util.Json;

/// <summary>
/// Utilities for JSON constructors.
/// </summary>
/// <remarks>
/// These build JSON straight into bytes and never build a tree. That is the point: an aggregate over
/// a large table would otherwise allocate a node per row and throw it away, and the only parsing
/// anywhere here is the one a caller explicitly asks for by asking for unique keys.
/// </remarks>
public static class JsonConstructorUtils
{
    /// <summary>
    /// The ABSENT ON NULL flag: a null element is left out rather than written as JSON null.
    /// </summary>
    public const int JsonAbsentOnNull = 1;

    /// <summary>
    /// Shortens a string for an error message: a JSON document can be megabytes long, and
    /// carrying it whole into the message would copy and log it whole.
    /// </summary>
    /// <remarks>
    /// The cut is the reference's own in <c>ValueJson</c> and <c>JsonByteArrayTarget</c>
    /// (issue #12 C10); the unique-keys message below keeps its different boundary because
    /// the reference draws it differently there.
    /// </remarks>
    /// <param name="s">the string</param>
    /// <param name="length">how many characters to keep</param>
    /// <returns>the string, or its beginning followed by an ellipsis</returns>
    internal static string Truncate(string s, int length)
        => s.Length > length ? s[..length] + "..." : s;

    /// <summary>
    /// The WITH UNIQUE KEYS flag: the assembled object is checked for repeated names.
    /// </summary>
    public const int JsonWithUniqueKeys = 2;

    /// <summary>
    /// Appends a value to a JSON object in the specified stream.
    /// </summary>
    /// <param name="stream">the output stream to append to</param>
    /// <param name="key">the name of the property</param>
    /// <param name="value">the value of the property</param>
    /// <remarks>
    /// The separator goes in when the stream holds more than one byte, not when it is non-empty. The
    /// opening brace is already there, so "not empty" would put a comma after it and "empty" would
    /// be wrong too — the first member written is the one byte more than nothing.
    /// </remarks>
    public static void JsonObjectAppend(MemoryStream stream, string key, Value value)
    {
        if (stream.Length > 1)
        {
            stream.WriteByte((byte)',');
        }

        _ = JsonByteArrayTarget.EncodeString(stream, key);
        stream.WriteByte((byte)':');

        // The value's own conversion, and its bytes as they are: no re-validating and no
        // re-normalizing what was just produced.
        byte[] b = value.ConvertToJson(TypeInfo.TypeJson, Value.ConvertTo, null).GetBytesNoCopy()!;
        stream.Write(b, 0, b.Length);
    }

    /// <summary>
    /// Appends the trailing closing brace, validates the object, and converts it to a JSON value.
    /// </summary>
    /// <param name="stream">the stream holding the object</param>
    /// <param name="flags">the flags (<see cref="JsonWithUniqueKeys"/>)</param>
    /// <returns>the JSON value</returns>
    /// <exception cref="H2SharpException">if unique keys were asked for and are not unique</exception>
    public static Value JsonObjectFinish(MemoryStream stream, int flags)
    {
        stream.WriteByte((byte)'}');
        byte[] result = stream.ToArray();
        if ((flags & JsonWithUniqueKeys) != 0)
        {
            try
            {
                _ = JsonBytesSource.Parse(result, new JsonValidationTargetWithUniqueKeys());
            }
            catch (Exception)
            {
                // Both failures the check can produce are caught: the target's for a duplicate, and
                // the source's for something that is not an object at all. Either means the object
                // does not satisfy what was asked for.
                string s = JsonBytesSource.Parse(result, new JsonStringTarget());

                // The reference asks whether the *string* fits but hands over the *bytes* when it
                // does not, so the message carries a byte array's identity rather than the document.
                // That is reproduced: it is what a user of H2 sees, and matching it matters more than
                // producing the message that was presumably meant. The boundary is the reference's
                // here and differs from Truncate's (128 is kept and elongated, not cut), so this
                // site does not share the helper (issue #12 C10).
                throw DbException.GetInvalidValueException(
                    "JSON WITH UNIQUE KEYS",
                    s.Length < 128 ? result : s[..128] + "...");
            }
        }

        return ValueJson.GetInternal(result);
    }

    /// <summary>
    /// Appends a value to a JSON array in the specified output stream.
    /// </summary>
    /// <param name="stream">the output stream to append to</param>
    /// <param name="value">the value</param>
    /// <param name="flags">the flags (<see cref="JsonAbsentOnNull"/>)</param>
    /// <remarks>
    /// Two different nulls end up in the same place. SQL NULL and the JSON document <c>null</c> are
    /// different things, and by default both are written as JSON null; with the flag set, SQL NULL is
    /// left out entirely — and leaving it out before the separator is written is what keeps several
    /// nulls in a row from leaving a dangling comma.
    /// </remarks>
    public static void JsonArrayAppend(MemoryStream stream, Value value, int flags)
    {
        if (ReferenceEquals(value, ValueNull.Instance) || ReferenceEquals(value, ValueJson.Null))
        {
            if ((flags & JsonAbsentOnNull) != 0)
            {
                return;
            }

            value = ValueJson.Null;
        }

        if (stream.Length > 1)
        {
            stream.WriteByte((byte)',');
        }

        byte[] b = value.ConvertToType(TypeInfo.TypeJson).GetBytesNoCopy()!;
        stream.Write(b, 0, b.Length);
    }
}
