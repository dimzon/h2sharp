/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.value.ValueCollectionBase.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
using System;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;

namespace H2Sharp.Values;

/// <summary>
/// What an ARRAY and a ROW have in common: a list of values that are compared one by one.
/// </summary>
/// <remarks>
/// <para>
/// The list is not copied, exactly as in the reference, and that is what
/// <see cref="GetList"/> exists to say. A caller that keeps the array it passed in keeps
/// the ability to change the value behind the collection's back, so the name carries the
/// warning the reference carries in its javadoc.
/// </para>
/// <para>
/// The two kinds differ in what a list of different lengths means. For an ARRAY the shorter
/// one sorts first, and for a ROW the comparison is refused: a row has a fixed number of
/// fields, and two rows of different widths are not rows of the same thing at all.
/// </para>
/// </remarks>
public abstract class ValueCollectionBase : Value
{
    /// <summary>
    /// The values, which the collection does not copy.
    /// </summary>
    internal readonly Value[] values;

    private int hash;

    private protected ValueCollectionBase(Value[] values)
    {
        this.values = values;
    }

    /// <summary>
    /// The values, which must not be changed afterwards.
    /// </summary>
    /// <returns>the list</returns>
    public Value[] GetList() => values;

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (hash != 0)
        {
            return hash;
        }

        // The reference starts from the type number, so a collection of Nones is not the
        // collection of Nums with the same elements: an ARRAY never equals a ROW.
        int h = GetValueType();
        foreach (Value v in values)
        {
            unchecked
            {
                h = (31 * h) + v.GetHashCode();
            }
        }

        hash = h;
        return h;
    }

    /// <inheritdoc/>
    public override int CompareWithNull(
        Value? v, bool forEquality, CastDataProvider? provider, CompareMode compareMode)
    {
        if (ReferenceEquals(v, ValueNull.Instance))
        {
            return int.MinValue;
        }

        int leftType = GetValueType();
        int rightType = v!.GetValueType();
        if (rightType != leftType)
        {
            // Not the promotion this method's own base does, and not a conversion either:
            // comparing an ARRAY with a ROW is a type error, since there is no type that holds
            // both. The error is raised about the other value, as in the reference.
            throw GetDataConversionErrorException(v!, leftType);
        }

        ValueCollectionBase r = (ValueCollectionBase)v!;
        Value[] leftArray = values;
        Value[] rightArray = r.values;
        int leftLength = leftArray.Length;
        int rightLength = rightArray.Length;
        if (leftLength != rightLength)
        {
            if (leftType == Row)
            {
                throw DbException.Get(ErrorCode.ColumnCountDoesNotMatch);
            }

            if (forEquality)
            {
                return 1;
            }
        }

        if (forEquality)
        {
            // Equality of a collection is not decided by the first field that differs: a NULL
            // anywhere says that the two may still be equal, so the difference is remembered
            // and the rest is looked at. Returning the difference would answer "different"
            // for two rows that differ only where one of them has nothing.
            bool hasNull = false;
            for (int i = 0; i < leftLength; i++)
            {
                Value v1 = leftArray[i];
                Value v2 = rightArray[i];
                int comp = v1.CompareWithNull(v2, forEquality, provider, compareMode);
                if (comp != 0)
                {
                    if (comp != int.MinValue)
                    {
                        return comp;
                    }

                    hasNull = true;
                }
            }

            return hasNull ? int.MinValue : 0;
        }

        int len = Math.Min(leftLength, rightLength);
        for (int i = 0; i < len; i++)
        {
            Value v1 = leftArray[i];
            Value v2 = rightArray[i];
            int comp = v1.CompareWithNull(v2, forEquality, provider, compareMode);
            if (comp != 0)
            {
                return comp;
            }
        }

        return Math.Sign(leftLength.CompareTo(rightLength));
    }

    /// <inheritdoc/>
    public override bool ContainsNull()
    {
        foreach (Value v in values)
        {
            if (v.ContainsNull())
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    protected override Value GetValueWithFirstNullImpl(Value v)
    {
        ValueCollectionBase r = (ValueCollectionBase)v;
        Value[] leftArray = values;
        Value[] rightArray = r.values;
        int len = Math.Min(leftArray.Length, rightArray.Length);
        for (int i = 0; i < len; i++)
        {
            Value v1 = leftArray[i];
            Value v2 = rightArray[i];
            Value? c = v1.GetValueWithFirstNull(v2);
            if (ReferenceEquals(c, v1))
            {
                return this;
            }

            if (ReferenceEquals(c, v2))
            {
                return v;
            }
        }

        // Nothing has its first NULL earlier than the other's. The base turns that into null,
        // which says the two are the same shape as far as ordering goes.
        return null!;
    }

    /// <inheritdoc/>
    public override int GetMemory()
    {
        int memory = 72 + (values.Length * Constants.MemoryPointer);
        foreach (Value v in values)
        {
            memory += v.GetMemory();
        }

        return memory;
    }
}
