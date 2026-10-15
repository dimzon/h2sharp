/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.value.ExtTypeInfoEnum.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using System.Globalization;
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
    /// <remarks>
    /// Whether an enum starts at 0 or at 1 is a property of the connection, not of the list,
    /// so it is asked of the provider: the same <c>ENUM('A', 'B')</c> is stored as 1 and 2 by
    /// one client and as 0 and 1 by another. Without a provider the reference counts from 1, and
    /// so does this.
    /// </remarks>
    /// <param name="ordinal">ordinal value of an enum</param>
    /// <param name="provider">the cast information provider, which decides whether enums are 0-based</param>
    /// <returns><c>ValueEnum</c> instance</returns>
    /// <exception cref="H2SharpException">if the ordinal is out of range</exception>
    public ValueEnum GetValue(int ordinal, CastDataProvider? provider)
    {
        string label;
        if (provider is null || !provider.ZeroBasedEnums())
        {
            if (ordinal < 1 || ordinal > enumerators.Length)
            {
                throw DbException.Get(
                    ErrorCode.EnumValueNotPermitted,
                    GetTraceSql(),
                    ordinal.ToString(CultureInfo.InvariantCulture));
            }

            label = enumerators[ordinal - 1]!;
        }
        else
        {
            if (ordinal < 0 || ordinal >= enumerators.Length)
            {
                throw DbException.Get(
                    ErrorCode.EnumValueNotPermitted,
                    GetTraceSql(),
                    ordinal.ToString(CultureInfo.InvariantCulture));
            }

            label = enumerators[ordinal]!;
        }

        return new ValueEnum(this, label, ordinal);
    }

    /// <summary>
    /// Get <c>ValueEnum</c> instance for a label string.
    /// </summary>
    /// <param name="label">label string</param>
    /// <param name="provider">the cast information provider, which decides whether enums are 0-based</param>
    /// <returns><c>ValueEnum</c> instance</returns>
    /// <exception cref="H2SharpException">if the label is not one of the enumerators</exception>
    public ValueEnum GetValue(string label, CastDataProvider? provider)
        => GetValueOrNull(label, provider)
        ?? throw DbException.Get(ErrorCode.EnumValueNotPermitted, GetTraceSql(), label);

    /// <summary>
    /// Get <c>ValueEnum</c> instance for a label string, or null if the label is not one of the
    /// enumerators.
    /// </summary>
    /// <remarks>
    /// Matching is against the cleaned labels, so a label written with surrounding quotes or
    /// spaces finds the same enumerator. That is the reference's <c>sanitize</c>, and it is why
    /// this returns null instead of throwing: a caller that wants a value or an error asks for
    /// <see cref="GetValue(string, CastDataProvider?)"/>, and a caller that wants to know
    /// whether a label belongs to this list asks here.
    /// </remarks>
    /// <param name="label">label string</param>
    /// <param name="provider">the cast information provider, which decides whether enums are 0-based</param>
    /// <returns><c>ValueEnum</c> instance, or null</returns>
    public ValueEnum? GetValueOrNull(string label, CastDataProvider? provider)
    {
        string? l = Sanitize(label);
        if (l is not null)
        {
            for (int i = 0, ordinal = provider is null || !provider.ZeroBasedEnums() ? 1 : 0;
                i < cleaned.Length;
                i++, ordinal++)
            {
                if (string.Equals(l, cleaned[i], StringComparison.Ordinal))
                {
                    return new ValueEnum(this, enumerators[i]!, ordinal);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The enumerator list to compare a binary operation in, which is the list of whichever
    /// side is an ENUM.
    /// </summary>
    /// <remarks>
    /// Two ENUMs of different lists are compared by putting both into one of the lists — the
    /// left one's — which re-derives the right one's ordinal from its label. That is the
    /// reference's answer and it is the only one available: ordinals of two lists have nothing
    /// to do with each other, so the two have to agree on which list is being spoken.
    /// </remarks>
    /// <param name="left">left (first) operand</param>
    /// <param name="right">right (second) operand</param>
    /// <returns>enumerators from the left or the right value</returns>
    /// <exception cref="H2SharpException">if neither value is an ENUM</exception>
    public static ExtTypeInfoEnum GetEnumeratorsForBinaryOperation(Value left, Value right)
    {
        if (left.GetValueType() == Value.Enum)
        {
            return ((ValueEnum)left).GetEnumerators();
        }

        if (right.GetValueType() == Value.Enum)
        {
            return ((ValueEnum)right).GetEnumerators();
        }

        throw DbException.Get(
            ErrorCode.UnknownDataType1,
            "type1=" + left.GetValueType().ToString(CultureInfo.InvariantCulture)
            + ", type2=" + right.GetValueType().ToString(CultureInfo.InvariantCulture));
    }

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
            hash = (31 * hash) + Utils.JavaHashCode(e);
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
}
