/*
 * Copyright 2004-2026 H2 Group.
 * Copyright 2026 H2Sharp contributors.
 * Ported from org.h2.value.ExtTypeInfo.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 */
namespace H2Sharp.Values;

/// <summary>
/// Extended parameters of a data type: the element type of an array, the fields of a row,
/// the enum definition, and so on.
/// </summary>
/// <remarks>
/// The reference declares this as an abstract class implementing HasSQL. The concrete
/// subclasses (ExtTypeInfoEnum, ExtTypeInfoGeometry, ExtTypeInfoNumeric, ExtTypeInfoRow)
/// are not ported yet, so only the shape is here for now; see MIGRATION_STATUS.md for why
/// they are the next unit rather than something invented to fill the gap.
/// </remarks>
public abstract class ExtTypeInfo
{
    /// <summary>
    /// The declared type name, as written in the SQL rather than the canonical one.
    /// </summary>
    /// <returns>the declared type name</returns>
    public abstract string GetDeclaredTypeName();
}
