/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Ported from org.h2.value.Value.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The one-argument convertTo, which is what a comparison calls.
 *
 * This used to hold a narrow stand-in that knew three families of types and refused the rest.
 * The narrow version is gone: the general conversion family is in Value.Conversion.Families.cs
 * now, so this is the same dispatch the reference has, and a comparison reaches exactly the
 * conversion a cast would.
 */
using H2Sharp.Engine;

namespace H2Sharp.Values;

/// <summary>
/// A value of some type. Every value knows its type number, and every type knows how to
/// describe itself as SQL.
/// </summary>
public abstract partial class Value
{
    /// <summary>
    /// Convert a value to the specified type without taking scale and precision into account.
    /// </summary>
    /// <param name="targetType">the type of the returned value</param>
    /// <returns>the converted value</returns>
    public Value ConvertToForComparison(int targetType) => ConvertToForComparison(targetType, null);

    /// <summary>
    /// Convert a value to the specified type without taking scale and precision into account.
    /// </summary>
    /// <remarks>
    /// The reference's <c>convertTo(int, provider)</c>, which a comparison calls. It is a
    /// separate method from the three-argument one for two reasons that are both about the
    /// collection types: an ARRAY has no component type to convert to, and a ROW has no field
    /// names to convert by, so those two targets ask for the value as a collection rather than
    /// for a collection of a given shape. Every other target has a single type to ask for and
    /// goes through the general conversion in <see cref="ConvertToValue"/>.
    /// </remarks>
    /// <param name="targetType">the type of the returned value</param>
    /// <param name="provider">the cast information provider</param>
    /// <returns>the converted value</returns>
    public Value ConvertToForComparison(int targetType, CastDataProvider? provider)
    {
        switch (targetType)
        {
            case Array:
                return ConvertToAnyArray(provider);

            case Row:
                return ConvertToAnyRow();

            default:
                return ConvertToValue(TypeInfo.GetTypeInfo(targetType), provider, ConvertTo, null);
        }
    }

    /// <summary>
    /// Converts this value to an ARRAY of any component type, which is what a comparison needs:
    /// there is no target element type to ask for, only a question of whether the two values are
    /// the same shape.
    /// </summary>
    /// <remarks>
    /// The reference's <c>convertToAnyArray</c>. A scalar becomes a one element ARRAY of itself,
    /// with the component type taken from that value's own type — so a BLOB becomes an ARRAY of
    /// BLOB, not of VARBINARY. That is not the same as <see cref="Value.ConvertToValue(TypeInfo,
    /// CastDataProvider?, int, object?)"/>'s ARRAY branch, which does convert a BLOB to a
    /// VARBINARY element; here there is no target element type at all, so the value keeps its
    /// own. The two answers differ, and the reference has both.
    /// </remarks>
    /// <param name="provider">the cast information provider</param>
    /// <returns>the value as an ARRAY</returns>
    public ValueArray ConvertToAnyArray(CastDataProvider? provider)
    {
        if (GetValueType() == Array)
        {
            return (ValueArray)this;
        }

        return ValueArray.Get(GetTypeInfo(), [this], provider);
    }

    /// <summary>
    /// Converts this value to a ROW of any field types.
    /// </summary>
    /// <remarks>
    /// The reference's <c>convertToAnyRow</c>. A value that is not already a ROW becomes a ROW of
    /// one field holding it, which is what makes a row comparable with the scalar it is.
    /// </remarks>
    /// <returns>the value as a ROW</returns>
    public ValueRow ConvertToAnyRow()
        => GetValueType() == Row ? (ValueRow)this : ValueRow.Get([this]);
}
