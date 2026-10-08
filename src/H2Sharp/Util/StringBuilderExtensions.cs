/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Java's StringBuilder.append, for the one case where the two builders disagree.
 */
using System.Text;

namespace H2Sharp.Util;

/// <summary>
/// The places where a .NET string builder and a Java one are not interchangeable.
/// </summary>
/// <remarks>
/// Appending a null is the only such place that shows up in ordinary rendering. Java writes the
/// four letters of "null"; .NET writes nothing. That is invisible in most code and visible in
/// exactly the values that can be null inside a larger one — a NULL element of an ARRAY or a
/// ROW — where the reference's own text says <c>[null, 1]</c> and the untranslated builder says
/// <c>[, 1]</c>.
/// <para>
/// Named <c>AppendJ</c> because the reference's own methods carry a J when they exist only to
/// match the platform's behaviour, and because a name that says nothing would be worse than a
/// name that says "like Java".
/// </para>
/// </remarks>
public static class StringBuilderExtensions
{
    /// <summary>
    /// Appends a string, writing the word <c>null</c> when there is none — which is what Java's
    /// <c>StringBuilder.append(String)</c> does and .NET's does not.
    /// </summary>
    /// <param name="builder">the builder to append to</param>
    /// <param name="value">the string, which may be null</param>
    /// <returns>the same builder</returns>
    public static StringBuilder AppendJ(this StringBuilder builder, string? value)
        => builder.Append(value ?? "null");
}
