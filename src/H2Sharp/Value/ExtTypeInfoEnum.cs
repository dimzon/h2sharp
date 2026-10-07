/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ExtTypeInfoEnum.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Util;

namespace H2Sharp.Values;

/// <summary>
/// Extended parameters of the ENUM data type.
/// </summary>
/// <remarks>
/// <para>
/// The two value factories are not ported: they return a <c>ValueEnum</c> and take a
/// <c>CastDataProvider</c>, and neither class exists yet. They fail loudly rather than
/// returning a plausible value. Everything else is ported, including the validation in the
/// constructor, because that is what the type is: an enum definition is rejected or accepted
/// when the type is built, not when a value arrives.
/// </para>
/// <para>
/// The cleaned labels are kept even though only the value factories use them here, since
/// they are what the deduplication compares and what a label lookup has to match.
/// </para>
/// </remarks>
public sealed class ExtTypeInfoEnum : ExtTypeInfo
{
    private readonly string?[] enumerators;
    private readonly string?[] cleaned;

    private TypeInfo? type;

    /// <summary>
    /// Creates new instance of extended parameters of the ENUM data type.
    /// </summary>
    /// <param name="enumerators">
    /// the enumerators. May not be modified by caller or by this class.
    /// </param>
    /// <exception cref="H2SharpException">if there are none, or one is empty</exception>
    /// <exception cref="H2SharpException">if two of them are the same after cleaning</exception>
    public ExtTypeInfoEnum(string?[] enumerators)
    {
        int length;
        if (enumerators is null || (length = enumerators.Length) == 0)
        {
            throw DbException.Get(ErrorCode.EnumEmpty);
        }

        if (length > Constants.MaxArrayCardinality)
        {
            throw DbException.GetValueTooLongException(
                "ENUM", "(" + length + " elements)", length);
        }

        string?[] cleanedLabels = new string?[length];
        for (int i = 0; i < length; i++)
        {
            string? l = Sanitize(enumerators[i]);
            if (string.IsNullOrEmpty(l))
            {
                throw DbException.Get(ErrorCode.EnumEmpty);
            }

            for (int j = 0; j < i; j++)
            {
                if (string.Equals(l, cleanedLabels[j], StringComparison.Ordinal))
                {
                    throw DbException.Get(
                        ErrorCode.EnumDuplicate,
                        ToSql(new StringBuilder(), enumerators).ToString());
                }
            }

            cleanedLabels[i] = l;
        }

        this.enumerators = enumerators;
        cleaned = SameLabels(cleanedLabels, enumerators) ? enumerators : cleanedLabels;
    }

    /// <summary>
    /// The labels as they compare for equality: trimmed and upper cased.
    /// </summary>
    /// <remarks>
    /// Unused until the value factories are ported, and kept because it is what the
    /// deduplication above just computed. The reference computes exactly the same array and
    /// hands the original over when cleaning changed nothing, so the two sides share storage
    /// there as well.
    /// </remarks>
    internal string?[] Cleaned => cleaned;

    /// <summary>
    /// Get count of elements in enumeration.
    /// </summary>
    /// <returns>count of elements in enumeration</returns>
    public int GetCount() => enumerators.Length;

    /// <summary>
    /// Returns an enumerator with specified 0-based ordinal value.
    /// </summary>
    /// <param name="ordinal">ordinal value of an enumerator</param>
    /// <returns>the enumerator with specified ordinal value</returns>
    public string? GetEnumerator(int ordinal) => enumerators[ordinal];

    /// <summary>
    /// Get <c>ValueEnum</c> instance for an ordinal.
    /// </summary>
    /// <param name="ordinal">ordinal value of an enum</param>
    /// <returns><c>ValueEnum</c> instance</returns>
    /// <exception cref="NotSupportedException">always, <c>ValueEnum</c> is not ported</exception>
    public object GetValue(int ordinal)
        => throw new NotSupportedException(
            "An enum value needs ValueEnum, which is not ported yet.");

    /// <summary>
    /// Get <c>ValueEnum</c> instance for a label string.
    /// </summary>
    /// <param name="label">label string</param>
    /// <returns><c>ValueEnum</c> instance</returns>
    /// <exception cref="NotSupportedException">always, <c>ValueEnum</c> is not ported</exception>
    public object GetValue(string label)
        => throw new NotSupportedException(
            "An enum value needs ValueEnum, which is not ported yet.");

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // The reference uses Arrays.hashCode, which is specified as
        //   31 * (31 * ... + enumerators[0]) + enumerators[1] + ...
        // over the array elements, so it is spelled out here rather than delegated to
        // GetHashCode on the elements, which is not what Java does.
        int hash = 1;
        foreach (string? e in enumerators)
        {
            hash = (31 * hash) + JavaHashCode(e);
        }

        return hash + 203_117;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj is not ExtTypeInfoEnum other)
        {
            return false;
        }

        return enumerators.AsSpan().SequenceEqual(other.enumerators.AsSpan(), StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags)
        => ToSql(builder, enumerators);

    /// <summary>
    /// The ENUM type this definition describes: its precision is the longest label and its
    /// scale is zero.
    /// </summary>
    /// <remarks>
    /// Computed once and kept, as in the reference, because the type is asked for on every
    /// comparison and the answer cannot change: the labels are fixed at construction.
    /// </remarks>
    /// <returns>the type</returns>
    internal TypeInfo GetEnumType()
    {
        TypeInfo? cached = type;
        if (cached is not null)
        {
            return cached;
        }

        int p = 0;
        foreach (string? s in enumerators)
        {
            // A null label cannot survive the constructor, so this only sees a null when the
            // array was null and the reference would have thrown before reaching here.
            int l = s?.Length ?? 0;
            if (l > p)
            {
                p = l;
            }
        }

        type = new TypeInfo(Value.Enum, p, 0, this);
        return type;
    }

    private static string? Sanitize(string? label)
    {
        if (label is null)
        {
            return null;
        }

        int length = label.Length;
        if (length > Constants.MaxStringLength)
        {
            throw DbException.GetValueTooLongException("ENUM", label, length);
        }

        return StringUtils.ToUpperEnglish(label.Trim());
    }

    private static StringBuilder ToSql(StringBuilder builder, string?[] enumerators)
    {
        _ = builder.Append('(');
        for (int i = 0; i < enumerators.Length; i++)
        {
            if (i != 0)
            {
                _ = builder.Append(", ");
            }

            _ = builder.Append('\'');
            string? s = enumerators[i];
            if (s is not null)
            {
                for (int j = 0; j < s.Length; j++)
                {
                    char c = s[j];
                    if (c == '\'')
                    {
                        _ = builder.Append('\'');
                    }

                    _ = builder.Append(c);
                }
            }

            _ = builder.Append('\'');
        }

        return builder.Append(')');
    }

    private static bool SameLabels(string?[] left, string?[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Java's <c>String.hashCode</c>: the UTF-16 code units folded with 31.
    /// </summary>
    /// <remarks>
    /// Not <c>StringComparer.Ordinal.GetHashCode</c>, which .NET randomises per process.
    /// The reference hash codes are part of the observable behaviour of a data type, and a
    /// port that produced different numbers in each language would be wrong in the same way
    /// a wrong string comparison would be.
    /// </remarks>
    /// <param name="s">the string</param>
    /// <returns>the Java hash code, with Java's wrapping overflow</returns>
    private static int JavaHashCode(string? s)
    {
        if (s is null)
        {
            return 0;
        }

        unchecked
        {
            int hash = 0;
            foreach (char c in s)
            {
                hash = (31 * hash) + c;
            }

            return hash;
        }
    }
}
