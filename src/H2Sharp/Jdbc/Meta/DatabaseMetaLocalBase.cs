/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.jdbc.meta.DatabaseMetaLocalBase.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The metadata answers that do not depend on the catalogue: the product identity and the
 * JDBC collections H2 has no rows for. The reference keeps this class package-private and
 * lets the public DatabaseMetaLocal extend it; C# forbids a public class from inheriting a
 * less accessible base, so the class is public here. It is a language requirement, not a
 * decision to expose a second entry point: the type is abstract and every member except
 * CheckClosed is sealed.
 */
using H2Sharp.Result;
using H2Sharp.Values;

namespace H2Sharp.Jdbc.Meta;

/// <summary>
/// Base implementation of database meta information.
/// </summary>
public abstract class DatabaseMetaLocalBase : DatabaseMeta
{
    /// <inheritdoc/>
    public sealed override string GetDatabaseProductVersion()
        => H2Sharp.Engine.Constants.FullVersion;

    /// <inheritdoc/>
    public sealed override ResultInterface GetVersionColumns(string? catalog, string? schema, string? table)
    {
        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("SCOPE", TypeInfo.TypeSmallint);
        result.AddColumn("COLUMN_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("DATA_TYPE", TypeInfo.TypeInteger);
        result.AddColumn("TYPE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("COLUMN_SIZE", TypeInfo.TypeInteger);
        result.AddColumn("BUFFER_LENGTH", TypeInfo.TypeInteger);
        result.AddColumn("DECIMAL_DIGITS", TypeInfo.TypeSmallint);
        result.AddColumn("PSEUDO_COLUMN", TypeInfo.TypeSmallint);
        return result;
    }

    /// <inheritdoc/>
    public sealed override ResultInterface GetUDTs(
        string? catalog, string? schemaPattern, string? typeNamePattern, int[]? types)
    {
        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("TYPE_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("TYPE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("TYPE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("CLASS_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("DATA_TYPE", TypeInfo.TypeInteger);
        result.AddColumn("REMARKS", TypeInfo.TypeVarchar);
        result.AddColumn("BASE_TYPE", TypeInfo.TypeSmallint);
        return result;
    }

    /// <inheritdoc/>
    public sealed override ResultInterface GetSuperTypes(
        string? catalog, string? schemaPattern, string? typeNamePattern)
    {
        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("TYPE_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("TYPE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("TYPE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("SUPERTYPE_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("SUPERTYPE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("SUPERTYPE_NAME", TypeInfo.TypeVarchar);
        return result;
    }

    /// <inheritdoc/>
    public sealed override ResultInterface GetSuperTables(
        string? catalog, string? schemaPattern, string? tableNamePattern)
    {
        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("TABLE_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("TABLE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("TABLE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("SUPERTABLE_NAME", TypeInfo.TypeVarchar);
        return result;
    }

    /// <inheritdoc/>
    public sealed override ResultInterface GetAttributes(
        string? catalog, string? schemaPattern, string? typeNamePattern, string? attributeNamePattern)
    {
        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("TYPE_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("TYPE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("TYPE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("ATTR_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("DATA_TYPE", TypeInfo.TypeInteger);
        result.AddColumn("ATTR_TYPE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("ATTR_SIZE", TypeInfo.TypeInteger);
        result.AddColumn("DECIMAL_DIGITS", TypeInfo.TypeInteger);
        result.AddColumn("NUM_PREC_RADIX", TypeInfo.TypeInteger);
        result.AddColumn("NULLABLE", TypeInfo.TypeInteger);
        result.AddColumn("REMARKS", TypeInfo.TypeVarchar);
        result.AddColumn("ATTR_DEF", TypeInfo.TypeVarchar);
        result.AddColumn("SQL_DATA_TYPE", TypeInfo.TypeInteger);
        result.AddColumn("SQL_DATETIME_SUB", TypeInfo.TypeInteger);
        result.AddColumn("CHAR_OCTET_LENGTH", TypeInfo.TypeInteger);
        result.AddColumn("ORDINAL_POSITION", TypeInfo.TypeInteger);
        result.AddColumn("IS_NULLABLE", TypeInfo.TypeVarchar);
        result.AddColumn("SCOPE_CATALOG", TypeInfo.TypeVarchar);
        result.AddColumn("SCOPE_SCHEMA", TypeInfo.TypeVarchar);
        result.AddColumn("SCOPE_TABLE", TypeInfo.TypeVarchar);
        result.AddColumn("SOURCE_DATA_TYPE", TypeInfo.TypeSmallint);
        return result;
    }

    /// <inheritdoc/>
    public sealed override int GetDatabaseMajorVersion()
        => H2Sharp.Engine.Constants.VersionMajor;

    /// <inheritdoc/>
    public sealed override int GetDatabaseMinorVersion()
        => H2Sharp.Engine.Constants.VersionMinor;

    /// <inheritdoc/>
    public sealed override ResultInterface GetFunctions(
        string? catalog, string? schemaPattern, string? functionNamePattern)
    {
        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("FUNCTION_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("FUNCTION_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("FUNCTION_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("REMARKS", TypeInfo.TypeVarchar);
        result.AddColumn("FUNCTION_TYPE", TypeInfo.TypeSmallint);
        result.AddColumn("SPECIFIC_NAME", TypeInfo.TypeVarchar);
        return result;
    }

    /// <inheritdoc/>
    public sealed override ResultInterface GetFunctionColumns(
        string? catalog, string? schemaPattern, string? functionNamePattern, string? columnNamePattern)
    {
        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("FUNCTION_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("FUNCTION_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("FUNCTION_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("COLUMN_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("COLUMN_TYPE", TypeInfo.TypeSmallint);
        result.AddColumn("DATA_TYPE", TypeInfo.TypeInteger);
        result.AddColumn("TYPE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("PRECISION", TypeInfo.TypeInteger);
        result.AddColumn("LENGTH", TypeInfo.TypeInteger);
        result.AddColumn("SCALE", TypeInfo.TypeSmallint);
        result.AddColumn("RADIX", TypeInfo.TypeSmallint);
        result.AddColumn("NULLABLE", TypeInfo.TypeSmallint);
        result.AddColumn("REMARKS", TypeInfo.TypeVarchar);
        result.AddColumn("CHAR_OCTET_LENGTH", TypeInfo.TypeInteger);
        result.AddColumn("ORDINAL_POSITION", TypeInfo.TypeInteger);
        result.AddColumn("IS_NULLABLE", TypeInfo.TypeVarchar);
        result.AddColumn("SPECIFIC_NAME", TypeInfo.TypeVarchar);
        return result;
    }

    /// <summary>
    /// Creates the empty pseudo-column result the reference builds in this class.
    /// </summary>
    /// <returns>the result with its columns and no rows</returns>
    internal SimpleResult GetPseudoColumnsResult()
    {
        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("TABLE_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("TABLE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("TABLE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("COLUMN_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("DATA_TYPE", TypeInfo.TypeInteger);
        result.AddColumn("COLUMN_SIZE", TypeInfo.TypeInteger);
        result.AddColumn("DECIMAL_DIGITS", TypeInfo.TypeInteger);
        result.AddColumn("NUM_PREC_RADIX", TypeInfo.TypeInteger);
        result.AddColumn("COLUMN_USAGE", TypeInfo.TypeVarchar);
        result.AddColumn("REMARKS", TypeInfo.TypeVarchar);
        result.AddColumn("CHAR_OCTET_LENGTH", TypeInfo.TypeInteger);
        result.AddColumn("IS_NULLABLE", TypeInfo.TypeVarchar);
        return result;
    }

    /// <summary>
    /// Refuses once the session is closed, the way the reference refuses.
    /// </summary>
    internal abstract void CheckClosed();
}
