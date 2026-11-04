/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.jdbc.meta.DatabaseMeta.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The database metadata contract behind the reference's JDBC DatabaseMetaData.
 *
 * The reference keeps the answers in the engine and lets the JDBC interface translate
 * them; this is the engine half. Its shape is the reference's, including the methods the
 * port cannot answer yet: a stub that refuses with the name of the missing machinery is
 * better than one that returns an empty result that reads like "there is nothing".
 */
using H2Sharp.Engine;
using H2Sharp.Result;

namespace H2Sharp.Jdbc.Meta;

/// <summary>
/// Database meta information.
/// </summary>
public abstract class DatabaseMeta
{
    /// <summary>
    /// Gets the default ordering of NULL values.
    /// </summary>
    /// <remarks>
    /// The reference names this method <c>defaultNullOrdering</c>. In C# the return type
    /// already has that name, and a member named like the type makes every unqualified use
    /// of the type ambiguous, so the method is spelled with the usual <c>Get</c> prefix.
    /// </remarks>
    /// <returns>the default ordering</returns>
    public abstract DefaultNullOrdering GetDefaultNullOrdering();

    /// <summary>
    /// Gets the product version.
    /// </summary>
    /// <returns>the product version</returns>
    public abstract string GetDatabaseProductVersion();

    /// <summary>
    /// Gets the SQL keywords the database treats specially.
    /// </summary>
    /// <returns>the comma-separated keywords</returns>
    public abstract string GetSQLKeywords();

    /// <summary>
    /// Gets the numeric functions.
    /// </summary>
    /// <returns>the comma-separated function names</returns>
    public abstract string GetNumericFunctions();

    /// <summary>
    /// Gets the string functions.
    /// </summary>
    /// <returns>the comma-separated function names</returns>
    public abstract string GetStringFunctions();

    /// <summary>
    /// Gets the system functions.
    /// </summary>
    /// <returns>the comma-separated function names</returns>
    public abstract string GetSystemFunctions();

    /// <summary>
    /// Gets the time and date functions.
    /// </summary>
    /// <returns>the comma-separated function names</returns>
    public abstract string GetTimeDateFunctions();

    /// <summary>
    /// Gets the escape sequence of LIKE patterns.
    /// </summary>
    /// <returns>the escape sequence</returns>
    public abstract string GetSearchStringEscape();

    /// <summary>
    /// Gets the procedures of the database.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schemaPattern">the schema pattern, or null</param>
    /// <param name="procedureNamePattern">the procedure pattern, or null</param>
    /// <returns>the procedures</returns>
    public abstract ResultInterface GetProcedures(
        string? catalog, string? schemaPattern, string? procedureNamePattern);

    /// <summary>
    /// Gets the columns of the procedures of the database.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schemaPattern">the schema pattern, or null</param>
    /// <param name="procedureNamePattern">the procedure pattern, or null</param>
    /// <param name="columnNamePattern">the column pattern, or null</param>
    /// <returns>the procedure columns</returns>
    public abstract ResultInterface GetProcedureColumns(
        string? catalog, string? schemaPattern, string? procedureNamePattern, string? columnNamePattern);

    /// <summary>
    /// Gets the tables and views of the database.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schemaPattern">the schema pattern, or null</param>
    /// <param name="tableNamePattern">the table pattern, or null</param>
    /// <param name="types">the table types to include, or null for all</param>
    /// <returns>the tables</returns>
    public abstract ResultInterface GetTables(
        string? catalog, string? schemaPattern, string? tableNamePattern, string[]? types);

    /// <summary>
    /// Gets the schemas of the database.
    /// </summary>
    /// <returns>the schemas</returns>
    public abstract ResultInterface GetSchemas();

    /// <summary>
    /// Gets the catalogs of the database.
    /// </summary>
    /// <returns>the catalogs</returns>
    public abstract ResultInterface GetCatalogs();

    /// <summary>
    /// Gets the table types of the database.
    /// </summary>
    /// <returns>the table types</returns>
    public abstract ResultInterface GetTableTypes();

    /// <summary>
    /// Gets the columns of the tables and views of the database.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schemaPattern">the schema pattern, or null</param>
    /// <param name="tableNamePattern">the table pattern, or null</param>
    /// <param name="columnNamePattern">the column pattern, or null</param>
    /// <returns>the columns</returns>
    public abstract ResultInterface GetColumns(
        string? catalog, string? schemaPattern, string? tableNamePattern, string? columnNamePattern);

    /// <summary>
    /// Gets the column privileges.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schema">the schema, or null</param>
    /// <param name="table">the table</param>
    /// <param name="columnNamePattern">the column pattern, or null</param>
    /// <returns>the privileges</returns>
    public abstract ResultInterface GetColumnPrivileges(
        string? catalog, string? schema, string? table, string? columnNamePattern);

    /// <summary>
    /// Gets the table privileges.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schemaPattern">the schema pattern, or null</param>
    /// <param name="tableNamePattern">the table pattern, or null</param>
    /// <returns>the privileges</returns>
    public abstract ResultInterface GetTablePrivileges(
        string? catalog, string? schemaPattern, string? tableNamePattern);

    /// <summary>
    /// Gets the columns that uniquely identify a row.
    /// </summary>
    /// <param name="catalogPattern">the catalog pattern, or null</param>
    /// <param name="schemaPattern">the schema pattern, or null</param>
    /// <param name="tableName">the table</param>
    /// <param name="scope">the scope</param>
    /// <param name="nullable">whether nullable columns are included</param>
    /// <returns>the best row identifier columns</returns>
    public abstract ResultInterface GetBestRowIdentifier(
        string? catalogPattern, string? schemaPattern, string? tableName, int scope, bool nullable);

    /// <summary>
    /// Gets the columns that change when a row changes.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schema">the schema, or null</param>
    /// <param name="table">the table</param>
    /// <returns>the version columns</returns>
    public abstract ResultInterface GetVersionColumns(string? catalog, string? schema, string? table);

    /// <summary>
    /// Gets the primary key columns of a table.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schema">the schema, or null</param>
    /// <param name="table">the table</param>
    /// <returns>the primary key columns</returns>
    public abstract ResultInterface GetPrimaryKeys(string? catalog, string? schema, string? table);

    /// <summary>
    /// Gets the foreign keys of a table.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schema">the schema, or null</param>
    /// <param name="table">the table</param>
    /// <returns>the imported keys</returns>
    public abstract ResultInterface GetImportedKeys(string? catalog, string? schema, string? table);

    /// <summary>
    /// Gets the foreign keys that reference a table.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schema">the schema, or null</param>
    /// <param name="table">the table</param>
    /// <returns>the exported keys</returns>
    public abstract ResultInterface GetExportedKeys(string? catalog, string? schema, string? table);

    /// <summary>
    /// Gets the foreign keys between two tables.
    /// </summary>
    /// <param name="primaryCatalog">the primary catalog, or null</param>
    /// <param name="primarySchema">the primary schema, or null</param>
    /// <param name="primaryTable">the primary table</param>
    /// <param name="foreignCatalog">the foreign catalog, or null</param>
    /// <param name="foreignSchema">the foreign schema, or null</param>
    /// <param name="foreignTable">the foreign table</param>
    /// <returns>the cross reference</returns>
    public abstract ResultInterface GetCrossReference(
        string? primaryCatalog, string? primarySchema, string? primaryTable,
        string? foreignCatalog, string? foreignSchema, string? foreignTable);

    /// <summary>
    /// Gets the data types the database supports.
    /// </summary>
    /// <returns>the type information</returns>
    public abstract ResultInterface GetTypeInfo();

    /// <summary>
    /// Gets the indexes of a table.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schema">the schema, or null</param>
    /// <param name="table">the table</param>
    /// <param name="unique">whether only unique indexes are returned</param>
    /// <param name="approximate">whether approximate statistics are allowed</param>
    /// <returns>the index information</returns>
    public abstract ResultInterface GetIndexInfo(
        string? catalog, string? schema, string? table, bool unique, bool approximate);

    /// <summary>
    /// Gets the user-defined types.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schemaPattern">the schema pattern, or null</param>
    /// <param name="typeNamePattern">the type pattern, or null</param>
    /// <param name="types">the type codes, or null</param>
    /// <returns>the user-defined types</returns>
    public abstract ResultInterface GetUDTs(
        string? catalog, string? schemaPattern, string? typeNamePattern, int[]? types);

    /// <summary>
    /// Gets the supertypes of the user-defined types.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schemaPattern">the schema pattern, or null</param>
    /// <param name="typeNamePattern">the type pattern, or null</param>
    /// <returns>the supertypes</returns>
    public abstract ResultInterface GetSuperTypes(
        string? catalog, string? schemaPattern, string? typeNamePattern);

    /// <summary>
    /// Gets the supertables of the tables.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schemaPattern">the schema pattern, or null</param>
    /// <param name="tableNamePattern">the table pattern, or null</param>
    /// <returns>the supertables</returns>
    public abstract ResultInterface GetSuperTables(
        string? catalog, string? schemaPattern, string? tableNamePattern);

    /// <summary>
    /// Gets the attributes of the user-defined types.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schemaPattern">the schema pattern, or null</param>
    /// <param name="typeNamePattern">the type pattern, or null</param>
    /// <param name="attributeNamePattern">the attribute pattern, or null</param>
    /// <returns>the attributes</returns>
    public abstract ResultInterface GetAttributes(
        string? catalog, string? schemaPattern, string? typeNamePattern, string? attributeNamePattern);

    /// <summary>
    /// Gets the major version of the database.
    /// </summary>
    /// <returns>the major version</returns>
    public abstract int GetDatabaseMajorVersion();

    /// <summary>
    /// Gets the minor version of the database.
    /// </summary>
    /// <returns>the minor version</returns>
    public abstract int GetDatabaseMinorVersion();

    /// <summary>
    /// Gets the schemas matching the patterns.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schemaPattern">the schema pattern, or null</param>
    /// <returns>the schemas</returns>
    public abstract ResultInterface GetSchemas(string? catalog, string? schemaPattern);

    /// <summary>
    /// Gets the functions of the database.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schemaPattern">the schema pattern, or null</param>
    /// <param name="functionNamePattern">the function pattern, or null</param>
    /// <returns>the functions</returns>
    public abstract ResultInterface GetFunctions(
        string? catalog, string? schemaPattern, string? functionNamePattern);

    /// <summary>
    /// Gets the columns of the functions of the database.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schemaPattern">the schema pattern, or null</param>
    /// <param name="functionNamePattern">the function pattern, or null</param>
    /// <param name="columnNamePattern">the column pattern, or null</param>
    /// <returns>the function columns</returns>
    public abstract ResultInterface GetFunctionColumns(
        string? catalog, string? schemaPattern, string? functionNamePattern, string? columnNamePattern);

    /// <summary>
    /// Gets the pseudo columns of the tables.
    /// </summary>
    /// <param name="catalog">the catalog, or null</param>
    /// <param name="schemaPattern">the schema pattern, or null</param>
    /// <param name="tableNamePattern">the table pattern, or null</param>
    /// <param name="columnNamePattern">the column pattern, or null</param>
    /// <returns>the pseudo columns</returns>
    public abstract ResultInterface GetPseudoColumns(
        string? catalog, string? schemaPattern, string? tableNamePattern, string? columnNamePattern);
}
