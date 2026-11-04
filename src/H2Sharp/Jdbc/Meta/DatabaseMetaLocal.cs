/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.jdbc.meta.DatabaseMetaLocal.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The metadata of an open database, read from the catalogue objects.
 *
 * The reference's JDBC DatabaseMetaData does not query INFORMATION_SCHEMA in its default
 * mode: it delegates to this class, which walks schemas, tables, columns, indexes and
 * constraints directly. The result rows and their order are therefore part of the
 * metadata contract, and the port keeps both.
 *
 * The numeric type codes, nullability codes and referential action codes below are
 * java.sql.DatabaseMetaData's. There is no C# counterpart for them: the ADO.NET database
 * metadata contract does not define codes, so the reference's numbers are used as they are
 * and the .NET provider layer decides how to present them.
 *
 * Four groups of methods are not answerable yet, and refuse with the name of the missing
 * machinery instead of an empty result that would read like "there is nothing":
 * procedures and their columns need FunctionAlias, privileges need Right and User, and
 * the function lists need Help. CREATE ALIAS is not ported either (D-004 keeps roles and
 * users out), so nothing in this database could produce those rows today; the refusal is
 * still the honest shape because the reference methods exist.
 */
using System;
using System.Collections.Generic;
using H2Sharp.Api;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Expressions.Conditions;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;
using Index = H2Sharp.Indexes.Index;

namespace H2Sharp.Jdbc.Meta;

/// <summary>
/// Local implementation of database meta information.
/// </summary>
public sealed class DatabaseMetaLocal : DatabaseMetaLocalBase
{
    private const int BestRowSession = 2;

    private const int BestRowNotPseudo = 1;

    private const int ColumnNoNulls = 0;

    private const int ColumnNullable = 1;

    private const int ImportedKeyCascade = 0;

    private const int ImportedKeyRestrict = 1;

    private const int ImportedKeySetNull = 2;

    private const int ImportedKeyNoAction = 3;

    private const int ImportedKeySetDefault = 4;

    private const int ImportedKeyNotDeferrable = 7;

    private const int TableIndexHashed = 2;

    private const int TableIndexOther = 3;

    private const int TypeNullable = 1;

    private const int TypeSearchable = 3;

    private static readonly Value Yes = ValueVarchar.Get("YES");

    private static readonly Value No = ValueVarchar.Get("NO");

    private static readonly ValueSmallint BestRowSessionValue = ValueSmallint.Get((short)BestRowSession);

    private static readonly ValueSmallint BestRowNotPseudoValue = ValueSmallint.Get((short)BestRowNotPseudo);

    private static readonly ValueInteger ColumnNoNullsValue = ValueInteger.Get(ColumnNoNulls);

    private static readonly ValueInteger ColumnNullableValue = ValueInteger.Get(ColumnNullable);

    private static readonly ValueSmallint ImportedKeyNoActionValue = ValueSmallint.Get((short)ImportedKeyNoAction);

    private static readonly ValueSmallint ImportedKeyCascadeValue = ValueSmallint.Get((short)ImportedKeyCascade);

    private static readonly ValueSmallint ImportedKeyRestrictValue = ValueSmallint.Get((short)ImportedKeyRestrict);

    private static readonly ValueSmallint ImportedKeyDefaultValue = ValueSmallint.Get((short)ImportedKeySetDefault);

    private static readonly ValueSmallint ImportedKeySetNullValue = ValueSmallint.Get((short)ImportedKeySetNull);

    private static readonly ValueSmallint ImportedKeyNotDeferrableValue =
        ValueSmallint.Get((short)ImportedKeyNotDeferrable);

    private static readonly ValueSmallint TableIndexHashedValue = ValueSmallint.Get((short)TableIndexHashed);

    private static readonly ValueSmallint TableIndexOtherValue = ValueSmallint.Get((short)TableIndexOther);

    // This list must be ordered
    private static readonly string[] TableTypes =
        ["BASE TABLE", "GLOBAL TEMPORARY", "LOCAL TEMPORARY", "SYNONYM", "VIEW"];

    private static readonly ValueSmallint TypeNullableValue = ValueSmallint.Get((short)TypeNullable);

    private static readonly ValueSmallint TypeSearchableValue = ValueSmallint.Get((short)TypeSearchable);

    private static readonly Value NoUsageRestrictions = ValueVarchar.Get("NO_USAGE_RESTRICTIONS");

    private readonly SessionLocal session;

    /// <summary>
    /// Creates the metadata reader of a session.
    /// </summary>
    /// <param name="session">the session</param>
    public DatabaseMetaLocal(SessionLocal session)
    {
        this.session = session;
    }

    /// <inheritdoc/>
    public override DefaultNullOrdering GetDefaultNullOrdering()
        => session.GetDatabase().GetDefaultNullOrdering();

    /// <inheritdoc/>
    public override string GetSQLKeywords()
    {
        System.Text.StringBuilder builder = new(103);
        _ = builder.Append(
            "CURRENT_CATALOG,"
            + "CURRENT_SCHEMA,"
            + "GROUPS,"
            + "IF,ILIKE,"
            + "KEY,");
        Mode mode = session.GetMode();
        if (mode.Limit)
        {
            _ = builder.Append("LIMIT,");
        }

        if (mode.MinusIsExcept)
        {
            _ = builder.Append("MINUS,");
        }

        _ = builder.Append(
            "OFFSET,"
            + "QUALIFY,"
            + "REGEXP,ROWNUM,");
        if (mode.TopInSelect || mode.TopInDml)
        {
            _ = builder.Append("TOP,");
        }

        return builder.Append("_ROWID_").ToString();
    }

    /// <inheritdoc/>
    public override string GetNumericFunctions()
        => throw new TODOException("DatabaseMetaLocal.getNumericFunctions: Help is not ported");

    /// <inheritdoc/>
    public override string GetStringFunctions()
        => throw new TODOException("DatabaseMetaLocal.getStringFunctions: Help is not ported");

    /// <inheritdoc/>
    public override string GetSystemFunctions()
        => throw new TODOException("DatabaseMetaLocal.getSystemFunctions: Help is not ported");

    /// <inheritdoc/>
    public override string GetTimeDateFunctions()
        => throw new TODOException("DatabaseMetaLocal.getTimeDateFunctions: Help is not ported");

    /// <inheritdoc/>
    /// <remarks>
    /// The reference reads Settings.DEFAULT_ESCAPE; Settings is not ported, so this is the
    /// reference's default value, which is the one an unconfigured database has.
    /// </remarks>
    public override string GetSearchStringEscape() => "\\";

    /// <inheritdoc/>
    /// <remarks>
    /// D-037: CREATE ALIAS is out of scope. The engine is embedded and stores no executable
    /// code, so a function alias cannot be persistent; there is nothing to report. The
    /// result keeps the reference columns and has no rows, which is the honest answer for
    /// this database, not a placeholder that hides an unported feature.
    /// </remarks>
    public override ResultInterface GetProcedures(
        string? catalog, string? schemaPattern, string? procedureNamePattern)
    {
        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("PROCEDURE_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("PROCEDURE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("PROCEDURE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("RESERVED1", TypeInfo.TypeNull);
        result.AddColumn("RESERVED2", TypeInfo.TypeNull);
        result.AddColumn("RESERVED3", TypeInfo.TypeNull);
        result.AddColumn("REMARKS", TypeInfo.TypeVarchar);
        result.AddColumn("PROCEDURE_TYPE", TypeInfo.TypeSmallint);
        result.AddColumn("SPECIFIC_NAME", TypeInfo.TypeVarchar);
        return result;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// D-037, the counterpart of GetProcedures: there are no function aliases, so the
    /// reference's procedure-column row set is empty.
    /// </remarks>
    public override ResultInterface GetProcedureColumns(
        string? catalog, string? schemaPattern, string? procedureNamePattern, string? columnNamePattern)
    {
        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("PROCEDURE_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("PROCEDURE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("PROCEDURE_NAME", TypeInfo.TypeVarchar);
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
        result.AddColumn("COLUMN_DEF", TypeInfo.TypeVarchar);
        result.AddColumn("SQL_DATA_TYPE", TypeInfo.TypeInteger);
        result.AddColumn("SQL_DATETIME_SUB", TypeInfo.TypeInteger);
        result.AddColumn("CHAR_OCTET_LENGTH", TypeInfo.TypeInteger);
        result.AddColumn("ORDINAL_POSITION", TypeInfo.TypeInteger);
        result.AddColumn("IS_NULLABLE", TypeInfo.TypeVarchar);
        result.AddColumn("SPECIFIC_NAME", TypeInfo.TypeVarchar);
        return result;
    }

    /// <inheritdoc/>
    public override ResultInterface GetTables(
        string? catalog, string? schemaPattern, string? tableNamePattern, string[]? types)
    {
        SimpleResult result = new();
        result.AddColumn("TABLE_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("TABLE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("TABLE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("TABLE_TYPE", TypeInfo.TypeVarchar);
        result.AddColumn("REMARKS", TypeInfo.TypeVarchar);
        result.AddColumn("TYPE_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("TYPE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("TYPE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("SELF_REFERENCING_COL_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("REF_GENERATION", TypeInfo.TypeVarchar);
        if (!CheckCatalogName(catalog))
        {
            return result;
        }

        Database db = session.GetDatabase();
        Value catalogValue = GetString(db.GetShortName());
        HashSet<string>? typesSet;
        if (types is not null)
        {
            typesSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (string type in types)
            {
                int idx = Array.BinarySearch(TableTypes, type, StringComparer.Ordinal);
                if (idx >= 0)
                {
                    _ = typesSet.Add(TableTypes[idx]);
                }
                else if (type.Equals("TABLE", StringComparison.Ordinal))
                {
                    _ = typesSet.Add("BASE TABLE");
                }
            }

            if (typesSet.Count == 0)
            {
                return result;
            }
        }
        else
        {
            typesSet = null;
        }

        foreach (Schema schema in GetSchemasForPattern(schemaPattern))
        {
            Value schemaValue = GetString(schema.GetName());
            foreach ((string name, Table table, bool synonym) in GetTablesForPattern(schema, tableNamePattern))
            {
                Value tableName = GetString(name);
                GetTablesAdd(result, catalogValue, schemaValue, tableName, table, synonym, typesSet);
            }
        }

        // TABLE_TYPE, TABLE_CAT, TABLE_SCHEM, TABLE_NAME
        result.SortRows(new SortOrder(session, [3, 1, 2]).Compare);
        return result;
    }

    private void GetTablesAdd(
        SimpleResult result, Value catalogValue, Value schemaValue, Value tableName, Table table,
        bool synonym, HashSet<string>? typesSet)
    {
        string type = synonym ? "SYNONYM" : table.GetSQLTableType();
        if (typesSet is not null && !typesSet.Contains(type))
        {
            return;
        }

        result.AddRow(
            // TABLE_CAT
            catalogValue,
            // TABLE_SCHEM
            schemaValue,
            // TABLE_NAME
            tableName,
            // TABLE_TYPE
            GetString(type),
            // REMARKS
            GetString(table.GetComment()),
            // TYPE_CAT
            ValueNull.Instance,
            // TYPE_SCHEM
            ValueNull.Instance,
            // TYPE_NAME
            ValueNull.Instance,
            // SELF_REFERENCING_COL_NAME
            ValueNull.Instance,
            // REF_GENERATION
            ValueNull.Instance);
    }

    /// <inheritdoc/>
    public override ResultInterface GetSchemas() => GetSchemas(null, null);

    /// <inheritdoc/>
    public override ResultInterface GetCatalogs()
    {
        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("TABLE_CAT", TypeInfo.TypeVarchar);
        result.AddRow(GetString(session.GetDatabase().GetShortName()));
        return result;
    }

    /// <inheritdoc/>
    public override ResultInterface GetTableTypes()
    {
        SimpleResult result = new();
        result.AddColumn("TABLE_TYPE", TypeInfo.TypeVarchar);
        // Order by TABLE_TYPE
        result.AddRow(GetString("BASE TABLE"));
        result.AddRow(GetString("GLOBAL TEMPORARY"));
        result.AddRow(GetString("LOCAL TEMPORARY"));
        result.AddRow(GetString("SYNONYM"));
        result.AddRow(GetString("VIEW"));
        return result;
    }

    /// <inheritdoc/>
    public override ResultInterface GetColumns(
        string? catalog, string? schemaPattern, string? tableNamePattern, string? columnNamePattern)
    {
        SimpleResult result = new();
        result.AddColumn("TABLE_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("TABLE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("TABLE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("COLUMN_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("DATA_TYPE", TypeInfo.TypeInteger);
        result.AddColumn("TYPE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("COLUMN_SIZE", TypeInfo.TypeInteger);
        result.AddColumn("BUFFER_LENGTH", TypeInfo.TypeInteger);
        result.AddColumn("DECIMAL_DIGITS", TypeInfo.TypeInteger);
        result.AddColumn("NUM_PREC_RADIX", TypeInfo.TypeInteger);
        result.AddColumn("NULLABLE", TypeInfo.TypeInteger);
        result.AddColumn("REMARKS", TypeInfo.TypeVarchar);
        result.AddColumn("COLUMN_DEF", TypeInfo.TypeVarchar);
        result.AddColumn("SQL_DATA_TYPE", TypeInfo.TypeInteger);
        result.AddColumn("SQL_DATETIME_SUB", TypeInfo.TypeInteger);
        result.AddColumn("CHAR_OCTET_LENGTH", TypeInfo.TypeInteger);
        result.AddColumn("ORDINAL_POSITION", TypeInfo.TypeInteger);
        result.AddColumn("IS_NULLABLE", TypeInfo.TypeVarchar);
        result.AddColumn("SCOPE_CATALOG", TypeInfo.TypeVarchar);
        result.AddColumn("SCOPE_SCHEMA", TypeInfo.TypeVarchar);
        result.AddColumn("SCOPE_TABLE", TypeInfo.TypeVarchar);
        result.AddColumn("SOURCE_DATA_TYPE", TypeInfo.TypeSmallint);
        result.AddColumn("IS_AUTOINCREMENT", TypeInfo.TypeVarchar);
        result.AddColumn("IS_GENERATEDCOLUMN", TypeInfo.TypeVarchar);
        if (!CheckCatalogName(catalog))
        {
            return result;
        }

        Database db = session.GetDatabase();
        Value catalogValue = GetString(db.GetShortName());
        CompareLike? columnLike = GetLike(columnNamePattern);
        foreach (Schema schema in GetSchemasForPattern(schemaPattern))
        {
            Value schemaValue = GetString(schema.GetName());
            foreach ((string name, Table table, _) in GetTablesForPattern(schema, tableNamePattern))
            {
                Value tableName = GetString(name);
                GetColumnsAdd(result, catalogValue, schemaValue, tableName, table, columnLike);
            }
        }

        // TABLE_CAT, TABLE_SCHEM, TABLE_NAME, ORDINAL_POSITION
        result.SortRows(new SortOrder(session, [1, 2, 16]).Compare);
        return result;
    }

    private void GetColumnsAdd(
        SimpleResult result, Value catalogValue, Value schemaValue, Value tableName, Table table,
        CompareLike? columnLike)
    {
        int ordinal = 0;
        foreach (Column column in table.GetColumns())
        {
            if (!column.GetVisible())
            {
                continue;
            }

            ordinal++;
            string name = column.GetName();
            if (columnLike is not null && !columnLike.Test(name))
            {
                continue;
            }

            TypeInfo type = column.GetTypeInfo();
            ValueInteger precision = ValueInteger.Get(MathUtils.ConvertLongToInt(type.GetPrecision()));
            bool nullable = column.IsNullable() == Column.Nullable;
            bool isGenerated = column.IsGenerated();
            result.AddRow(
                // TABLE_CAT
                catalogValue,
                // TABLE_SCHEM
                schemaValue,
                // TABLE_NAME
                tableName,
                // COLUMN_NAME
                GetString(name),
                // DATA_TYPE
                ValueInteger.Get(DataType.ConvertTypeToSqlType(type)),
                // TYPE_NAME
                GetDataTypeName(type),
                // COLUMN_SIZE
                precision,
                // BUFFER_LENGTH
                ValueNull.Instance,
                // DECIMAL_DIGITS
                ValueInteger.Get(type.GetScale()),
                // NUM_PREC_RADIX
                GetRadix(type.GetValueType(), small: false),
                // NULLABLE
                nullable ? ColumnNullableValue : ColumnNoNullsValue,
                // REMARKS
                GetString(column.GetComment()),
                // COLUMN_DEF
                isGenerated ? ValueNull.Instance : GetString(column.GetDefaultSQL()),
                // SQL_DATA_TYPE (unused)
                ValueNull.Instance,
                // SQL_DATETIME_SUB (unused)
                ValueNull.Instance,
                // CHAR_OCTET_LENGTH
                precision,
                // ORDINAL_POSITION
                ValueInteger.Get(ordinal),
                // IS_NULLABLE
                nullable ? Yes : No,
                // SCOPE_CATALOG
                ValueNull.Instance,
                // SCOPE_SCHEMA
                ValueNull.Instance,
                // SCOPE_TABLE
                ValueNull.Instance,
                // SOURCE_DATA_TYPE
                ValueNull.Instance,
                // IS_AUTOINCREMENT
                column.IsIdentity() ? Yes : No,
                // IS_GENERATEDCOLUMN
                isGenerated ? Yes : No);
        }
    }

    /// <inheritdoc/>
    public override ResultInterface GetColumnPrivileges(
        string? catalog, string? schema, string? table, string? columnNamePattern)
        => throw new TODOException(
            "DatabaseMetaLocal.getColumnPrivileges: Right and User are not ported (D-004)");

    /// <inheritdoc/>
    public override ResultInterface GetTablePrivileges(
        string? catalog, string? schemaPattern, string? tableNamePattern)
        => throw new TODOException(
            "DatabaseMetaLocal.getTablePrivileges: Right and User are not ported (D-004)");

    /// <inheritdoc/>
    public override ResultInterface GetBestRowIdentifier(
        string? catalogPattern, string? schemaPattern, string? tableName, int scope, bool nullable)
    {
        if (tableName is null)
        {
            throw DbException.GetInvalidValueException("table", null);
        }

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
        if (!CheckCatalogName(catalogPattern))
        {
            return result;
        }

        foreach (Schema schema in GetSchemas(schemaPattern))
        {
            Table? table = schema.FindTableOrView(session, tableName);
            if (table is null)
            {
                continue;
            }

            foreach (Constraint constraint in table.GetConstraints())
            {
                if (constraint.GetConstraintType() != Constraint.Type.PRIMARY_KEY)
                {
                    continue;
                }

                IndexColumn[] columns = ((ConstraintUnique)constraint).GetColumns();
                foreach (IndexColumn indexColumn in columns)
                {
                    Column column = indexColumn.GetColumn();
                    TypeInfo type = column.GetTypeInfo();
                    DataType dt = DataType.GetDataType(type.GetValueType());
                    result.AddRow(
                        // SCOPE
                        BestRowSessionValue,
                        // COLUMN_NAME
                        GetString(column.GetName()),
                        // DATA_TYPE
                        ValueInteger.Get(DataType.ConvertTypeToSqlType(type)),
                        // TYPE_NAME
                        GetDataTypeName(type),
                        // COLUMN_SIZE
                        ValueInteger.Get(MathUtils.ConvertLongToInt(type.GetPrecision())),
                        // BUFFER_LENGTH
                        ValueNull.Instance,
                        // DECIMAL_DIGITS
                        dt.SupportsScale ? ValueSmallint.Get(MathUtils.ConvertIntToShort(type.GetScale()))
                            : ValueNull.Instance,
                        // PSEUDO_COLUMN
                        BestRowNotPseudoValue);
                }
            }
        }

        // Order by SCOPE (always the same)
        return result;
    }

    private Value GetDataTypeName(TypeInfo typeInfo) => GetString(typeInfo.GetDeclaredTypeName());

    /// <inheritdoc/>
    public override ResultInterface GetPrimaryKeys(string? catalog, string? schema, string? tableName)
    {
        if (tableName is null)
        {
            throw DbException.GetInvalidValueException("table", null);
        }

        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("TABLE_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("TABLE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("TABLE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("COLUMN_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("KEY_SEQ", TypeInfo.TypeSmallint);
        result.AddColumn("PK_NAME", TypeInfo.TypeVarchar);
        if (!CheckCatalogName(catalog))
        {
            return result;
        }

        Database db = session.GetDatabase();
        Value catalogValue = GetString(db.GetShortName());
        foreach (Schema s in GetSchemas(schema))
        {
            Table? table = s.FindTableOrView(session, tableName);
            if (table is null)
            {
                continue;
            }

            foreach (Constraint constraint in table.GetConstraints())
            {
                if (constraint.GetConstraintType() != Constraint.Type.PRIMARY_KEY)
                {
                    continue;
                }

                Value schemaValue = GetString(s.GetName());
                Value tableValue = GetString(table.GetTableName());
                Value pkValue = GetString(constraint.GetName());
                IndexColumn[] columns = ((ConstraintUnique)constraint).GetColumns();
                for (int i = 0; i < columns.Length;)
                {
                    result.AddRow(
                        // TABLE_CAT
                        catalogValue,
                        // TABLE_SCHEM
                        schemaValue,
                        // TABLE_NAME
                        tableValue,
                        // COLUMN_NAME
                        GetString(columns[i].GetColumn().GetName()),
                        // KEY_SEQ
                        ValueSmallint.Get((short)++i),
                        // PK_NAME
                        pkValue);
                }
            }
        }

        // COLUMN_NAME
        result.SortRows(new SortOrder(session, [3]).Compare);
        return result;
    }

    /// <inheritdoc/>
    public override ResultInterface GetImportedKeys(string? catalog, string? schema, string? tableName)
    {
        if (tableName is null)
        {
            throw DbException.GetInvalidValueException("table", null);
        }

        SimpleResult result = InitCrossReferenceResult();
        if (!CheckCatalogName(catalog))
        {
            return result;
        }

        Database db = session.GetDatabase();
        Value catalogValue = GetString(db.GetShortName());
        foreach (Schema s in GetSchemas(schema))
        {
            Table? table = s.FindTableOrView(session, tableName);
            if (table is null)
            {
                continue;
            }

            foreach (Constraint constraint in table.GetConstraints())
            {
                if (constraint.GetConstraintType() != Constraint.Type.REFERENTIAL)
                {
                    continue;
                }

                ConstraintReferential fk = (ConstraintReferential)constraint;
                Table fkTable = fk.GetTable();
                if (!ReferenceEquals(fkTable, table))
                {
                    continue;
                }

                Table pkTable = fk.GetRefTable();
                AddCrossReferenceResult(
                    result, catalogValue, pkTable.GetSchema()!.GetName(), pkTable,
                    fkTable.GetSchema()!.GetName(), fkTable, fk);
            }
        }

        // PKTABLE_CAT, PKTABLE_SCHEM, PKTABLE_NAME, KEY_SEQ
        result.SortRows(new SortOrder(session, [1, 2, 8]).Compare);
        return result;
    }

    /// <inheritdoc/>
    public override ResultInterface GetExportedKeys(string? catalog, string? schema, string? tableName)
    {
        if (tableName is null)
        {
            throw DbException.GetInvalidValueException("table", null);
        }

        SimpleResult result = InitCrossReferenceResult();
        if (!CheckCatalogName(catalog))
        {
            return result;
        }

        Database db = session.GetDatabase();
        Value catalogValue = GetString(db.GetShortName());
        foreach (Schema s in GetSchemas(schema))
        {
            Table? table = s.FindTableOrView(session, tableName);
            if (table is null)
            {
                continue;
            }

            foreach (Constraint constraint in table.GetConstraints())
            {
                if (constraint.GetConstraintType() != Constraint.Type.REFERENTIAL)
                {
                    continue;
                }

                ConstraintReferential fk = (ConstraintReferential)constraint;
                Table pkTable = fk.GetRefTable();
                if (!ReferenceEquals(pkTable, table))
                {
                    continue;
                }

                Table fkTable = fk.GetTable();
                AddCrossReferenceResult(
                    result, catalogValue, pkTable.GetSchema()!.GetName(), pkTable,
                    fkTable.GetSchema()!.GetName(), fkTable, fk);
            }
        }

        // FKTABLE_CAT, FKTABLE_SCHEM, FKTABLE_NAME, KEY_SEQ
        result.SortRows(new SortOrder(session, [5, 6, 8]).Compare);
        return result;
    }

    /// <inheritdoc/>
    public override ResultInterface GetCrossReference(
        string? primaryCatalog, string? primarySchema, string? primaryTable,
        string? foreignCatalog, string? foreignSchema, string? foreignTable)
    {
        if (primaryTable is null)
        {
            throw DbException.GetInvalidValueException("primaryTable", null);
        }

        if (foreignTable is null)
        {
            throw DbException.GetInvalidValueException("foreignTable", null);
        }

        SimpleResult result = InitCrossReferenceResult();
        if (!CheckCatalogName(primaryCatalog) || !CheckCatalogName(foreignCatalog))
        {
            return result;
        }

        Database db = session.GetDatabase();
        Value catalogValue = GetString(db.GetShortName());
        foreach (Schema s in GetSchemas(foreignSchema))
        {
            Table? table = s.FindTableOrView(session, foreignTable);
            if (table is null)
            {
                continue;
            }

            foreach (Constraint constraint in table.GetConstraints())
            {
                if (constraint.GetConstraintType() != Constraint.Type.REFERENTIAL)
                {
                    continue;
                }

                ConstraintReferential fk = (ConstraintReferential)constraint;
                Table fkTable = fk.GetTable();
                if (!ReferenceEquals(fkTable, table))
                {
                    continue;
                }

                Table pkTable = fk.GetRefTable();
                if (!db.EqualsIdentifiers(pkTable.GetTableName(), primaryTable))
                {
                    continue;
                }

                Schema pkSchema = pkTable.GetSchema()!;
                if (!CheckSchema(primarySchema, pkSchema))
                {
                    continue;
                }

                AddCrossReferenceResult(
                    result, catalogValue, pkSchema.GetName(), pkTable,
                    fkTable.GetSchema()!.GetName(), fkTable, fk);
            }
        }

        // FKTABLE_CAT, FKTABLE_SCHEM, FKTABLE_NAME, KEY_SEQ
        result.SortRows(new SortOrder(session, [5, 6, 8]).Compare);
        return result;
    }

    private SimpleResult InitCrossReferenceResult()
    {
        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("PKTABLE_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("PKTABLE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("PKTABLE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("PKCOLUMN_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("FKTABLE_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("FKTABLE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("FKTABLE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("FKCOLUMN_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("KEY_SEQ", TypeInfo.TypeSmallint);
        result.AddColumn("UPDATE_RULE", TypeInfo.TypeSmallint);
        result.AddColumn("DELETE_RULE", TypeInfo.TypeSmallint);
        result.AddColumn("FK_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("PK_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("DEFERRABILITY", TypeInfo.TypeSmallint);
        return result;
    }

    private void AddCrossReferenceResult(
        SimpleResult result, Value catalog, string pkSchema, Table pkTable,
        string fkSchema, Table fkTable, ConstraintReferential fk)
    {
        Value pkSchemaValue = GetString(pkSchema);
        Value pkTableValue = GetString(pkTable.GetTableName());
        Value fkSchemaValue = GetString(fkSchema);
        Value fkTableValue = GetString(fkTable.GetTableName());
        IndexColumn[] pkCols = fk.GetRefColumns();
        IndexColumn[] fkCols = fk.GetColumns();
        Value update = GetRefAction(fk.GetUpdateAction());
        Value delete = GetRefAction(fk.GetDeleteAction());
        Value fkNameValue = GetString(fk.GetName());
        Value pkNameValue = GetString(fk.GetReferencedConstraint()!.GetName());
        for (int j = 0, len = fkCols.Length; j < len; j++)
        {
            result.AddRow(
                // PKTABLE_CAT
                catalog,
                // PKTABLE_SCHEM
                pkSchemaValue,
                // PKTABLE_NAME
                pkTableValue,
                // PKCOLUMN_NAME
                GetString(pkCols[j].GetColumn().GetName()),
                // FKTABLE_CAT
                catalog,
                // FKTABLE_SCHEM
                fkSchemaValue,
                // FKTABLE_NAME
                fkTableValue,
                // FKCOLUMN_NAME
                GetString(fkCols[j].GetColumn().GetName()),
                // KEY_SEQ
                ValueSmallint.Get((short)(j + 1)),
                // UPDATE_RULE
                update,
                // DELETE_RULE
                delete,
                // FK_NAME
                fkNameValue,
                // PK_NAME
                pkNameValue,
                // DEFERRABILITY
                ImportedKeyNotDeferrableValue);
        }
    }

    private static ValueSmallint GetRefAction(ConstraintActionType action)
    {
        return action switch
        {
            ConstraintActionType.NO_ACTION => ImportedKeyNoActionValue,
            ConstraintActionType.CASCADE => ImportedKeyCascadeValue,
            ConstraintActionType.RESTRICT => ImportedKeyRestrictValue,
            ConstraintActionType.SET_DEFAULT => ImportedKeyDefaultValue,
            ConstraintActionType.SET_NULL => ImportedKeySetNullValue,
            _ => throw DbException.GetInternalError("action=" + action),
        };
    }

    /// <inheritdoc/>
    public override ResultInterface GetTypeInfo()
    {
        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("TYPE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("DATA_TYPE", TypeInfo.TypeInteger);
        result.AddColumn("PRECISION", TypeInfo.TypeInteger);
        result.AddColumn("LITERAL_PREFIX", TypeInfo.TypeVarchar);
        result.AddColumn("LITERAL_SUFFIX", TypeInfo.TypeVarchar);
        result.AddColumn("CREATE_PARAMS", TypeInfo.TypeVarchar);
        result.AddColumn("NULLABLE", TypeInfo.TypeSmallint);
        result.AddColumn("CASE_SENSITIVE", TypeInfo.TypeBoolean);
        result.AddColumn("SEARCHABLE", TypeInfo.TypeSmallint);
        result.AddColumn("UNSIGNED_ATTRIBUTE", TypeInfo.TypeBoolean);
        result.AddColumn("FIXED_PREC_SCALE", TypeInfo.TypeBoolean);
        result.AddColumn("AUTO_INCREMENT", TypeInfo.TypeBoolean);
        result.AddColumn("LOCAL_TYPE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("MINIMUM_SCALE", TypeInfo.TypeSmallint);
        result.AddColumn("MAXIMUM_SCALE", TypeInfo.TypeSmallint);
        result.AddColumn("SQL_DATA_TYPE", TypeInfo.TypeInteger);
        result.AddColumn("SQL_DATETIME_SUB", TypeInfo.TypeInteger);
        result.AddColumn("NUM_PREC_RADIX", TypeInfo.TypeInteger);
        for (int i = 1, l = Value.TypeCount; i < l; i++)
        {
            DataType type = DataType.GetDataType(i);
            Value name = GetString(Value.GetTypeName(type.Type));
            result.AddRow(
                // TYPE_NAME
                name,
                // DATA_TYPE
                ValueInteger.Get(DataType.ConvertValueTypeToSqlType(type.Type)),
                // PRECISION
                ValueInteger.Get(MathUtils.ConvertLongToInt(type.MaxPrecision)),
                // LITERAL_PREFIX
                GetString(type.Prefix),
                // LITERAL_SUFFIX
                GetString(type.Suffix),
                // CREATE_PARAMS
                GetString(type.Params),
                // NULLABLE
                TypeNullableValue,
                // CASE_SENSITIVE
                ValueBoolean.Get(type.CaseSensitive),
                // SEARCHABLE
                TypeSearchableValue,
                // UNSIGNED_ATTRIBUTE
                ValueBoolean.False,
                // FIXED_PREC_SCALE
                ValueBoolean.Get(type.Type == Value.Numeric),
                // AUTO_INCREMENT
                ValueBoolean.Get(DataType.IsNumericType(i)),
                // LOCAL_TYPE_NAME
                name,
                // MINIMUM_SCALE
                ValueSmallint.Get(MathUtils.ConvertIntToShort(type.MinScale)),
                // MAXIMUM_SCALE
                ValueSmallint.Get(MathUtils.ConvertIntToShort(type.MaxScale)),
                // SQL_DATA_TYPE (unused)
                ValueNull.Instance,
                // SQL_DATETIME_SUB (unused)
                ValueNull.Instance,
                // NUM_PREC_RADIX
                GetRadix(type.Type, small: false));
        }

        // DATA_TYPE, better types first
        result.SortRows(new SortOrder(session, [1]).Compare);
        return result;
    }

    private static Value GetRadix(int valueType, bool small)
    {
        if (DataType.IsNumericType(valueType))
        {
            int radix = valueType == Value.Numeric || valueType == Value.Decfloat ? 10 : 2;
            return small ? ValueSmallint.Get((short)radix) : ValueInteger.Get(radix);
        }

        return ValueNull.Instance;
    }

    /// <inheritdoc/>
    public override ResultInterface GetIndexInfo(
        string? catalog, string? schema, string? tableName, bool unique, bool approximate)
    {
        if (tableName is null)
        {
            throw DbException.GetInvalidValueException("table", null);
        }

        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("TABLE_CAT", TypeInfo.TypeVarchar);
        result.AddColumn("TABLE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("TABLE_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("NON_UNIQUE", TypeInfo.TypeBoolean);
        result.AddColumn("INDEX_QUALIFIER", TypeInfo.TypeVarchar);
        result.AddColumn("INDEX_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("TYPE", TypeInfo.TypeSmallint);
        result.AddColumn("ORDINAL_POSITION", TypeInfo.TypeSmallint);
        result.AddColumn("COLUMN_NAME", TypeInfo.TypeVarchar);
        result.AddColumn("ASC_OR_DESC", TypeInfo.TypeVarchar);
        result.AddColumn("CARDINALITY", TypeInfo.TypeBigint);
        result.AddColumn("PAGES", TypeInfo.TypeBigint);
        result.AddColumn("FILTER_CONDITION", TypeInfo.TypeVarchar);
        if (!CheckCatalogName(catalog))
        {
            return result;
        }

        Database db = session.GetDatabase();
        Value catalogValue = GetString(db.GetShortName());
        foreach (Schema s in GetSchemas(schema))
        {
            Table? table = s.FindTableOrView(session, tableName);
            if (table is null)
            {
                continue;
            }

            GetIndexInfo(catalogValue, GetString(s.GetName()), table, unique, approximate, result, db);
        }

        // NON_UNIQUE, TYPE, INDEX_NAME, ORDINAL_POSITION
        result.SortRows(new SortOrder(session, [3, 6, 5, 7]).Compare);
        return result;
    }

    private void GetIndexInfo(
        Value catalogValue, Value schemaValue, Table table, bool unique, bool approximate,
        SimpleResult result, Database db)
    {
        foreach (Index index in table.GetIndexes())
        {
            if (index.GetCreateSql() is null)
            {
                continue;
            }

            int uniqueColumnCount = index.GetUniqueColumnCount();
            if (unique && uniqueColumnCount == 0)
            {
                continue;
            }

            Value tableValue = GetString(table.GetTableName());
            Value indexValue = GetString(index.GetName());
            IndexColumn[] cols = index.GetIndexColumns();
            ValueSmallint type = index.GetIndexType()!.IsHash() ? TableIndexHashedValue : TableIndexOtherValue;
            for (int i = 0, l = cols.Length; i < l; i++)
            {
                IndexColumn c = cols[i];
                bool nonUnique = i >= uniqueColumnCount;
                if (unique && nonUnique)
                {
                    break;
                }

                result.AddRow(
                    // TABLE_CAT
                    catalogValue,
                    // TABLE_SCHEM
                    schemaValue,
                    // TABLE_NAME
                    tableValue,
                    // NON_UNIQUE
                    ValueBoolean.Get(nonUnique),
                    // INDEX_QUALIFIER
                    catalogValue,
                    // INDEX_NAME
                    indexValue,
                    // TYPE
                    type,
                    // ORDINAL_POSITION
                    ValueSmallint.Get((short)(i + 1)),
                    // COLUMN_NAME
                    GetString(c.GetColumn().GetName()),
                    // ASC_OR_DESC
                    GetString((c.SortType & SortOrder.Descending) != 0 ? "D" : "A"),
                    // CARDINALITY
                    ValueBigint.Get(approximate
                        ? index.GetRowCountApproximation(session)
                        : index.GetRowCount(session)),
                    // PAGES
                    ValueBigint.Get(index.GetDiskSpaceUsed(approximate) / db.GetPageSize()),
                    // FILTER_CONDITION
                    ValueNull.Instance);
            }
        }
    }

    /// <inheritdoc/>
    public override ResultInterface GetSchemas(string? catalog, string? schemaPattern)
    {
        CheckClosed();
        SimpleResult result = new();
        result.AddColumn("TABLE_SCHEM", TypeInfo.TypeVarchar);
        result.AddColumn("TABLE_CATALOG", TypeInfo.TypeVarchar);
        if (!CheckCatalogName(catalog))
        {
            return result;
        }

        CompareLike? schemaLike = GetLike(schemaPattern);
        IEnumerable<Schema> allSchemas = session.GetDatabase().GetSchemas();
        Value catalogValue = GetString(session.GetDatabase().GetShortName());
        if (schemaLike is null)
        {
            foreach (Schema s in allSchemas)
            {
                result.AddRow(GetString(s.GetName()), catalogValue);
            }
        }
        else
        {
            foreach (Schema s in allSchemas)
            {
                string name = s.GetName();
                if (schemaLike.Test(name))
                {
                    result.AddRow(GetString(name), catalogValue);
                }
            }
        }

        // TABLE_CATALOG, TABLE_SCHEM
        result.SortRows(new SortOrder(session, [0]).Compare);
        return result;
    }

    /// <inheritdoc/>
    public override ResultInterface GetPseudoColumns(
        string? catalog, string? schemaPattern, string? tableNamePattern, string? columnNamePattern)
    {
        SimpleResult result = GetPseudoColumnsResult();
        if (!CheckCatalogName(catalog))
        {
            return result;
        }

        Database db = session.GetDatabase();
        Value catalogValue = GetString(db.GetShortName());
        CompareLike? columnLike = GetLike(columnNamePattern);
        foreach (Schema schema in GetSchemasForPattern(schemaPattern))
        {
            Value schemaValue = GetString(schema.GetName());
            foreach ((string name, Table table, _) in GetTablesForPattern(schema, tableNamePattern))
            {
                Value tableName = GetString(name);
                GetPseudoColumnsAdd(result, catalogValue, schemaValue, tableName, table, columnLike);
            }
        }

        // TABLE_CAT, TABLE_SCHEM, TABLE_NAME, COLUMN_NAME
        result.SortRows(new SortOrder(session, [1, 2, 3]).Compare);
        return result;
    }

    private void GetPseudoColumnsAdd(
        SimpleResult result, Value catalogValue, Value schemaValue, Value tableName, Table table,
        CompareLike? columnLike)
    {
        Column? rowId = table.GetRowIdColumn();
        if (rowId is not null)
        {
            GetPseudoColumnsAdd(result, catalogValue, schemaValue, tableName, columnLike, rowId);
        }

        foreach (Column column in table.GetColumns())
        {
            if (!column.GetVisible())
            {
                GetPseudoColumnsAdd(result, catalogValue, schemaValue, tableName, columnLike, column);
            }
        }
    }

    private void GetPseudoColumnsAdd(
        SimpleResult result, Value catalogValue, Value schemaValue, Value tableName,
        CompareLike? columnLike, Column column)
    {
        string name = column.GetName();
        if (columnLike is not null && !columnLike.Test(name))
        {
            return;
        }

        TypeInfo type = column.GetTypeInfo();
        ValueInteger precision = ValueInteger.Get(MathUtils.ConvertLongToInt(type.GetPrecision()));
        result.AddRow(
            // TABLE_CAT
            catalogValue,
            // TABLE_SCHEM
            schemaValue,
            // TABLE_NAME
            tableName,
            // COLUMN_NAME
            GetString(name),
            // DATA_TYPE
            ValueInteger.Get(DataType.ConvertTypeToSqlType(type)),
            // COLUMN_SIZE
            precision,
            // DECIMAL_DIGITS
            ValueInteger.Get(type.GetScale()),
            // NUM_PREC_RADIX
            GetRadix(type.GetValueType(), small: false),
            // COLUMN_USAGE
            NoUsageRestrictions,
            // REMARKS
            GetString(column.GetComment()),
            // CHAR_OCTET_LENGTH
            precision,
            // IS_NULLABLE
            column.IsNullable() == Column.Nullable ? Yes : No);
    }

    /// <inheritdoc/>
    internal override void CheckClosed()
    {
        if (session.IsClosed())
        {
            throw DbException.Get(ErrorCode.DatabaseCalledAtShutdown);
        }
    }

    private Value GetString(string? text)
        => text is not null ? ValueVarchar.Get(text, session) : ValueNull.Instance;

    private bool CheckCatalogName(string? catalog)
    {
        if (catalog is not null && catalog.Length > 0)
        {
            Database db = session.GetDatabase();
            return db.EqualsIdentifiers(catalog, db.GetShortName());
        }

        return true;
    }

    private IEnumerable<Schema> GetSchemas(string? schema)
    {
        Database db = session.GetDatabase();
        if (schema is null)
        {
            return db.GetSchemas();
        }

        if (schema.Length == 0)
        {
            return [db.GetMainSchema()!];
        }

        Schema? s = db.FindSchema(schema);
        return s is not null ? [s] : [];
    }

    private IEnumerable<Schema> GetSchemasForPattern(string? schemaPattern)
    {
        Database db = session.GetDatabase();
        if (schemaPattern is null)
        {
            return db.GetSchemas();
        }

        if (schemaPattern.Length == 0)
        {
            return [db.GetMainSchema()!];
        }

        List<Schema> list = [];
        CompareLike like = GetLike(schemaPattern)!;
        foreach (Schema s in db.GetSchemas())
        {
            if (like.Test(s.GetName()))
            {
                list.Add(s);
            }
        }

        return list;
    }

    private List<(string Name, Table Table, bool Synonym)> GetTablesForPattern(
        Schema schema, string? tablePattern)
    {
        List<Table> tables = schema.GetAllTablesAndViews(session);
        List<TableSynonym> synonyms = schema.GetAllSynonyms();
        List<(string Name, Table Table, bool Synonym)> list = [];
        if (tablePattern is null)
        {
            foreach (Table table in tables)
            {
                list.Add((table.GetTableName(), table, false));
            }

            foreach (TableSynonym synonym in synonyms)
            {
                list.Add((synonym.GetName()!, synonym.GetSynonymFor(), true));
            }

            return list;
        }

        CompareLike like = GetLike(tablePattern)!;
        foreach (Table table in tables)
        {
            if (like.Test(table.GetTableName()))
            {
                list.Add((table.GetTableName(), table, false));
            }
        }

        foreach (TableSynonym synonym in synonyms)
        {
            string name = synonym.GetName()!;
            if (like.Test(name))
            {
                list.Add((name, synonym.GetSynonymFor(), true));
            }
        }

        return list;
    }

    private bool CheckSchema(string? schemaName, Schema schema)
    {
        if (schemaName is null)
        {
            return true;
        }

        if (schemaName.Length == 0)
        {
            return ReferenceEquals(schema, session.GetDatabase().GetMainSchema());
        }

        return session.GetDatabase().EqualsIdentifiers(schemaName, schema.GetName());
    }

    private CompareLike? GetLike(string? pattern)
    {
        if (pattern is null)
        {
            return null;
        }

        CompareLike like = new(
            session.GetDatabase().GetCompareMode(), "\\", null, false, false, null, null,
            CompareLike.LikeType.Like);
        like.InitPattern(pattern, '\\');
        return like;
    }
}
