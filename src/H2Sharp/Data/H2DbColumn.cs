/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 * Ported from none (new file, no Java counterpart).
 *
 * One column of a result, as ADO.NET describes it.
 *
 * DbColumn carries its description as settable properties rather than abstract members,
 * so this is the whole description in one constructor call: the name the query gave, the
 * position, the engine type and what it reads as. Everything the engine does not know —
 * keys, auto-increment, expression text — stays at the base default, which is null or
 * false, rather than a guess about a table the query may not even read.
 */
using System;
using System.Data.Common;

namespace H2Sharp.Data;

/// <summary>
/// Describes one column of a result.
/// </summary>
internal sealed class H2DbColumn : DbColumn
{
    /// <summary>
    /// Creates the description of a result column.
    /// </summary>
    /// <param name="name">the column name</param>
    /// <param name="ordinal">the column position</param>
    /// <param name="fieldType">the CLR type values read as</param>
    /// <param name="dataTypeName">the engine type name</param>
    /// <param name="precision">the precision, or -1</param>
    /// <param name="scale">the scale, or -1</param>
    /// <param name="allowNull">whether NULL reads</param>
    internal H2DbColumn(
        string name, int ordinal, Type fieldType, string dataTypeName,
        int precision, int scale, bool allowNull)
    {
        ColumnName = name;
        ColumnOrdinal = ordinal;
        DataType = fieldType;
        DataTypeName = dataTypeName;
        NumericPrecision = precision >= 0 ? precision : null;
        NumericScale = scale >= 0 ? scale : null;
        AllowDBNull = allowNull;
    }
}
