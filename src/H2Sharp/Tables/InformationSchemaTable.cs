/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.table.InformationSchemaTable and org.h2.table.MetaTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The metadata pseudo tables of the INFORMATION_SCHEMA.
 *
 * The reference has one class for the whole catalogue: a table per view, the columns of that
 * view, and rows that are computed from the metadata of the database when the view is read.
 * The rows are not stored, so a view of the metadata is always current.
 *
 * What is left out here, and why:
 *
 * * The legacy catalogue, InformationSchemaTableLegacy: the reference reaches it through a
 *   session flag that this port does not have.
 * * The rows of the views whose objects the port does not have: there are no users, roles,
 *   rights, aliases, domains, constants, triggers or synonyms here, and no check
 *   constraints, so those views are empty. The views themselves are here, with the
 *   columns of the reference, so that a query against one of them is answered and not
 *   refused.
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Tables;

/// <summary>
/// A metadata table of the INFORMATION_SCHEMA: one of the views, with the columns of the
/// reference and the rows computed from the metadata of the database.
/// </summary>
public sealed class InformationSchemaTable : Table
{
    /// <summary>
    /// The approximate number of rows of a meta table.
    /// </summary>
    public const long RowCountApproximation = 1000;

    /// <summary>The INFORMATION_SCHEMA_CATALOG_NAME view.</summary>
    public const int InformationSchemaCatalogName = 0;

    /// <summary>The CHECK_CONSTRAINTS view.</summary>
    public const int CheckConstraints = InformationSchemaCatalogName + 1;

    /// <summary>The COLLATIONS view.</summary>
    public const int Collations = CheckConstraints + 1;

    /// <summary>The COLUMNS view.</summary>
    public const int Columns = Collations + 1;

    /// <summary>The COLUMN_PRIVILEGES view.</summary>
    public const int ColumnPrivileges = Columns + 1;

    /// <summary>The CONSTRAINT_COLUMN_USAGE view.</summary>
    public const int ConstraintColumnUsage = ColumnPrivileges + 1;

    /// <summary>The DOMAINS view.</summary>
    public const int Domains = ConstraintColumnUsage + 1;

    /// <summary>The DOMAIN_CONSTRAINTS view.</summary>
    public const int DomainConstraints = Domains + 1;

    /// <summary>The ELEMENT_TYPES view.</summary>
    public const int ElementTypes = DomainConstraints + 1;

    /// <summary>The FIELDS view.</summary>
    public const int Fields = ElementTypes + 1;

    /// <summary>The KEY_COLUMN_USAGE view.</summary>
    public const int KeyColumnUsage = Fields + 1;

    /// <summary>The PARAMETERS view.</summary>
    public const int Parameters = KeyColumnUsage + 1;

    /// <summary>The REFERENTIAL_CONSTRAINTS view.</summary>
    public const int ReferentialConstraints = Parameters + 1;

    /// <summary>The ROUTINES view.</summary>
    public const int Routines = ReferentialConstraints + 1;

    /// <summary>The SCHEMATA view.</summary>
    public const int Schemata = Routines + 1;

    /// <summary>The SEQUENCES view.</summary>
    public const int Sequences = Schemata + 1;

    /// <summary>The TABLES view.</summary>
    public const int Tables = Sequences + 1;

    /// <summary>The TABLE_CONSTRAINTS view.</summary>
    public const int TableConstraints = Tables + 1;

    /// <summary>The TABLE_PRIVILEGES view.</summary>
    public const int TablePrivileges = TableConstraints + 1;

    /// <summary>The TRIGGERS view.</summary>
    public const int Triggers = TablePrivileges + 1;

    /// <summary>The VIEWS view.</summary>
    public const int Views = Triggers + 1;

    /// <summary>The CONSTANTS view.</summary>
    public const int Constants = Views + 1;

    /// <summary>The ENUM_VALUES view.</summary>
    public const int EnumValues = Constants + 1;

    /// <summary>The INDEXES view.</summary>
    public const int Indexes = EnumValues + 1;

    /// <summary>The INDEX_COLUMNS view.</summary>
    public const int IndexColumns = Indexes + 1;

    /// <summary>The IN_DOUBT view.</summary>
    public const int InDoubt = IndexColumns + 1;

    /// <summary>The LOCKS view.</summary>
    public const int Locks = InDoubt + 1;

    /// <summary>The QUERY_STATISTICS view.</summary>
    public const int QueryStatistics = Locks + 1;

    /// <summary>The RIGHTS view.</summary>
    public const int Rights = QueryStatistics + 1;

    /// <summary>The ROLES view.</summary>
    public const int Roles = Rights + 1;

    /// <summary>The SESSIONS view.</summary>
    public const int Sessions = Roles + 1;

    /// <summary>The SESSION_STATE view.</summary>
    public const int SessionState = Sessions + 1;

    /// <summary>The SETTINGS view.</summary>
    public const int Settings = SessionState + 1;

    /// <summary>The SYNONYMS view.</summary>
    public const int Synonyms = Settings + 1;

    /// <summary>The USERS view.</summary>
    public const int Users = Synonyms + 1;

    /// <summary>
    /// The number of the views, from 0 to <see cref="MetaTableTypeCount"/> - 1.
    /// </summary>
    public const int MetaTableTypeCount = Users + 1;

    /// <summary>
    /// The name of the character set, which is the same for every database here.
    /// </summary>
    private const string CharacterSetName = "Unicode";

    private readonly int type;

    private readonly string name;

    private readonly Column[] columns;

    private readonly InformationSchemaIndex index;

    private readonly Schema schema;

    /// <summary>
    /// Creates a metadata table.
    /// </summary>
    /// <param name="schema">the schema</param>
    /// <param name="id">the object id</param>
    /// <param name="type">the meta table type</param>
    public InformationSchemaTable(Schema schema, int id, int type)
    {
        this.type = type;
        this.schema = schema;
        name = GetMetaTableName(type);
        columns = BuildColumns(type);
        for (int i = 0; i < columns.Length; i++)
        {
            columns[i].SetTable(this, i);
        }

        index = new InformationSchemaIndex(this);
    }

    /// <summary>
    /// The name of the view of this type.
    /// </summary>
    /// <param name="type">the meta table type</param>
    /// <returns>the name</returns>
    public static string GetMetaTableName(int type) => type switch
    {
        InformationSchemaCatalogName => "INFORMATION_SCHEMA_CATALOG_NAME",
        CheckConstraints => "CHECK_CONSTRAINTS",
        Collations => "COLLATIONS",
        Columns => "COLUMNS",
        ColumnPrivileges => "COLUMN_PRIVILEGES",
        ConstraintColumnUsage => "CONSTRAINT_COLUMN_USAGE",
        Domains => "DOMAINS",
        DomainConstraints => "DOMAIN_CONSTRAINTS",
        ElementTypes => "ELEMENT_TYPES",
        Fields => "FIELDS",
        KeyColumnUsage => "KEY_COLUMN_USAGE",
        Parameters => "PARAMETERS",
        ReferentialConstraints => "REFERENTIAL_CONSTRAINTS",
        Routines => "ROUTINES",
        Schemata => "SCHEMATA",
        Sequences => "SEQUENCES",
        Tables => "TABLES",
        TableConstraints => "TABLE_CONSTRAINTS",
        TablePrivileges => "TABLE_PRIVILEGES",
        Triggers => "TRIGGERS",
        Views => "VIEWS",
        Constants => "CONSTANTS",
        EnumValues => "ENUM_VALUES",
        Indexes => "INDEXES",
        IndexColumns => "INDEX_COLUMNS",
        InDoubt => "IN_DOUBT",
        Locks => "LOCKS",
        QueryStatistics => "QUERY_STATISTICS",
        Rights => "RIGHTS",
        Roles => "ROLES",
        Sessions => "SESSIONS",
        SessionState => "SESSION_STATE",
        Settings => "SETTINGS",
        Synonyms => "SYNONYMS",
        Users => "USERS",
        _ => throw DbException.GetInternalError("type=" + type),
    };

    /// <summary>
    /// The columns of the view of this type, as the reference declares them.
    /// </summary>
    /// <param name="type">the meta table type</param>
    /// <returns>the columns</returns>
    private static Column[] BuildColumns(int type) => type switch
    {
        InformationSchemaCatalogName =>
        [
            NewColumn("CATALOG_NAME"),
        ],
        CheckConstraints =>
        [
            NewColumn("CONSTRAINT_CATALOG"),
            NewColumn("CONSTRAINT_SCHEMA"),
            NewColumn("CONSTRAINT_NAME"),
            NewColumn("CHECK_CLAUSE"),
        ],
        Collations =>
        [
            NewColumn("COLLATION_CATALOG"),
            NewColumn("COLLATION_SCHEMA"),
            NewColumn("COLLATION_NAME"),
            NewColumn("PAD_ATTRIBUTE"),
            // extensions
            NewColumn("LANGUAGE_TAG"),
        ],
        Columns =>
        [
            NewColumn("TABLE_CATALOG"),
            NewColumn("TABLE_SCHEMA"),
            NewColumn("TABLE_NAME"),
            NewColumn("COLUMN_NAME"),
            NewColumn("ORDINAL_POSITION", TypeInfo.TypeInteger),
            NewColumn("COLUMN_DEFAULT"),
            NewColumn("IS_NULLABLE"),
            NewColumn("DATA_TYPE"),
            NewColumn("CHARACTER_MAXIMUM_LENGTH", TypeInfo.TypeBigint),
            NewColumn("CHARACTER_OCTET_LENGTH", TypeInfo.TypeBigint),
            NewColumn("NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_PRECISION_RADIX", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_SCALE", TypeInfo.TypeInteger),
            NewColumn("DATETIME_PRECISION", TypeInfo.TypeInteger),
            NewColumn("INTERVAL_TYPE"),
            NewColumn("INTERVAL_PRECISION", TypeInfo.TypeInteger),
            NewColumn("CHARACTER_SET_CATALOG"),
            NewColumn("CHARACTER_SET_SCHEMA"),
            NewColumn("CHARACTER_SET_NAME"),
            NewColumn("COLLATION_CATALOG"),
            NewColumn("COLLATION_SCHEMA"),
            NewColumn("COLLATION_NAME"),
            NewColumn("DOMAIN_CATALOG"),
            NewColumn("DOMAIN_SCHEMA"),
            NewColumn("DOMAIN_NAME"),
            NewColumn("MAXIMUM_CARDINALITY", TypeInfo.TypeInteger),
            NewColumn("DTD_IDENTIFIER"),
            NewColumn("IS_IDENTITY"),
            NewColumn("IDENTITY_GENERATION"),
            NewColumn("IDENTITY_START", TypeInfo.TypeBigint),
            NewColumn("IDENTITY_INCREMENT", TypeInfo.TypeBigint),
            NewColumn("IDENTITY_MAXIMUM", TypeInfo.TypeBigint),
            NewColumn("IDENTITY_MINIMUM", TypeInfo.TypeBigint),
            NewColumn("IDENTITY_CYCLE"),
            NewColumn("IS_GENERATED"),
            NewColumn("GENERATION_EXPRESSION"),
            NewColumn("DECLARED_DATA_TYPE"),
            NewColumn("DECLARED_NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("DECLARED_NUMERIC_SCALE", TypeInfo.TypeInteger),
            // extensions
            NewColumn("GEOMETRY_TYPE"),
            NewColumn("GEOMETRY_SRID", TypeInfo.TypeInteger),
            NewColumn("IDENTITY_BASE", TypeInfo.TypeBigint),
            NewColumn("IDENTITY_CACHE", TypeInfo.TypeBigint),
            NewColumn("COLUMN_ON_UPDATE"),
            NewColumn("IS_VISIBLE", TypeInfo.TypeBoolean),
            NewColumn("DEFAULT_ON_NULL", TypeInfo.TypeBoolean),
            NewColumn("SELECTIVITY", TypeInfo.TypeInteger),
            NewColumn("REMARKS"),
        ],
        ColumnPrivileges =>
        [
            NewColumn("GRANTOR"),
            NewColumn("GRANTEE"),
            NewColumn("TABLE_CATALOG"),
            NewColumn("TABLE_SCHEMA"),
            NewColumn("TABLE_NAME"),
            NewColumn("COLUMN_NAME"),
            NewColumn("PRIVILEGE_TYPE"),
            NewColumn("IS_GRANTABLE"),
        ],
        ConstraintColumnUsage =>
        [
            NewColumn("TABLE_CATALOG"),
            NewColumn("TABLE_SCHEMA"),
            NewColumn("TABLE_NAME"),
            NewColumn("COLUMN_NAME"),
            NewColumn("CONSTRAINT_CATALOG"),
            NewColumn("CONSTRAINT_SCHEMA"),
            NewColumn("CONSTRAINT_NAME"),
        ],
        Domains =>
        [
            NewColumn("DOMAIN_CATALOG"),
            NewColumn("DOMAIN_SCHEMA"),
            NewColumn("DOMAIN_NAME"),
            NewColumn("DATA_TYPE"),
            NewColumn("CHARACTER_MAXIMUM_LENGTH", TypeInfo.TypeBigint),
            NewColumn("CHARACTER_OCTET_LENGTH", TypeInfo.TypeBigint),
            NewColumn("CHARACTER_SET_CATALOG"),
            NewColumn("CHARACTER_SET_SCHEMA"),
            NewColumn("CHARACTER_SET_NAME"),
            NewColumn("COLLATION_CATALOG"),
            NewColumn("COLLATION_SCHEMA"),
            NewColumn("COLLATION_NAME"),
            NewColumn("NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_PRECISION_RADIX", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_SCALE", TypeInfo.TypeInteger),
            NewColumn("DATETIME_PRECISION", TypeInfo.TypeInteger),
            NewColumn("INTERVAL_TYPE"),
            NewColumn("INTERVAL_PRECISION", TypeInfo.TypeInteger),
            NewColumn("DOMAIN_DEFAULT"),
            NewColumn("MAXIMUM_CARDINALITY", TypeInfo.TypeInteger),
            NewColumn("DTD_IDENTIFIER"),
            NewColumn("DECLARED_DATA_TYPE"),
            NewColumn("DECLARED_NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("DECLARED_NUMERIC_SCALE", TypeInfo.TypeInteger),
            // extensions
            NewColumn("GEOMETRY_TYPE"),
            NewColumn("GEOMETRY_SRID", TypeInfo.TypeInteger),
            NewColumn("DOMAIN_ON_UPDATE"),
            NewColumn("PARENT_DOMAIN_CATALOG"),
            NewColumn("PARENT_DOMAIN_SCHEMA"),
            NewColumn("PARENT_DOMAIN_NAME"),
            NewColumn("REMARKS"),
        ],
        DomainConstraints =>
        [
            NewColumn("CONSTRAINT_CATALOG"),
            NewColumn("CONSTRAINT_SCHEMA"),
            NewColumn("CONSTRAINT_NAME"),
            NewColumn("DOMAIN_CATALOG"),
            NewColumn("DOMAIN_SCHEMA"),
            NewColumn("DOMAIN_NAME"),
            NewColumn("IS_DEFERRABLE"),
            NewColumn("INITIALLY_DEFERRED"),
            // extensions
            NewColumn("REMARKS"),
        ],
        ElementTypes =>
        [
            NewColumn("OBJECT_CATALOG"),
            NewColumn("OBJECT_SCHEMA"),
            NewColumn("OBJECT_NAME"),
            NewColumn("OBJECT_TYPE"),
            NewColumn("COLLECTION_TYPE_IDENTIFIER"),
            NewColumn("DATA_TYPE"),
            NewColumn("CHARACTER_MAXIMUM_LENGTH", TypeInfo.TypeBigint),
            NewColumn("CHARACTER_OCTET_LENGTH", TypeInfo.TypeBigint),
            NewColumn("CHARACTER_SET_CATALOG"),
            NewColumn("CHARACTER_SET_SCHEMA"),
            NewColumn("CHARACTER_SET_NAME"),
            NewColumn("COLLATION_CATALOG"),
            NewColumn("COLLATION_SCHEMA"),
            NewColumn("COLLATION_NAME"),
            NewColumn("NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_PRECISION_RADIX", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_SCALE", TypeInfo.TypeInteger),
            NewColumn("DATETIME_PRECISION", TypeInfo.TypeInteger),
            NewColumn("INTERVAL_TYPE"),
            NewColumn("INTERVAL_PRECISION", TypeInfo.TypeInteger),
            NewColumn("MAXIMUM_CARDINALITY", TypeInfo.TypeInteger),
            NewColumn("DTD_IDENTIFIER"),
            NewColumn("DECLARED_DATA_TYPE"),
            NewColumn("DECLARED_NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("DECLARED_NUMERIC_SCALE", TypeInfo.TypeInteger),
            // extensions
            NewColumn("GEOMETRY_TYPE"),
            NewColumn("GEOMETRY_SRID", TypeInfo.TypeInteger),
        ],
        Fields =>
        [
            NewColumn("OBJECT_CATALOG"),
            NewColumn("OBJECT_SCHEMA"),
            NewColumn("OBJECT_NAME"),
            NewColumn("OBJECT_TYPE"),
            NewColumn("ROW_IDENTIFIER"),
            NewColumn("FIELD_NAME"),
            NewColumn("ORDINAL_POSITION", TypeInfo.TypeInteger),
            NewColumn("DATA_TYPE"),
            NewColumn("CHARACTER_MAXIMUM_LENGTH", TypeInfo.TypeBigint),
            NewColumn("CHARACTER_OCTET_LENGTH", TypeInfo.TypeBigint),
            NewColumn("CHARACTER_SET_CATALOG"),
            NewColumn("CHARACTER_SET_SCHEMA"),
            NewColumn("CHARACTER_SET_NAME"),
            NewColumn("COLLATION_CATALOG"),
            NewColumn("COLLATION_SCHEMA"),
            NewColumn("COLLATION_NAME"),
            NewColumn("NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_PRECISION_RADIX", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_SCALE", TypeInfo.TypeInteger),
            NewColumn("DATETIME_PRECISION", TypeInfo.TypeInteger),
            NewColumn("INTERVAL_TYPE"),
            NewColumn("INTERVAL_PRECISION", TypeInfo.TypeInteger),
            NewColumn("MAXIMUM_CARDINALITY", TypeInfo.TypeInteger),
            NewColumn("DTD_IDENTIFIER"),
            NewColumn("DECLARED_DATA_TYPE"),
            NewColumn("DECLARED_NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("DECLARED_NUMERIC_SCALE", TypeInfo.TypeInteger),
            // extensions
            NewColumn("GEOMETRY_TYPE"),
            NewColumn("GEOMETRY_SRID", TypeInfo.TypeInteger),
        ],
        KeyColumnUsage =>
        [
            NewColumn("CONSTRAINT_CATALOG"),
            NewColumn("CONSTRAINT_SCHEMA"),
            NewColumn("CONSTRAINT_NAME"),
            NewColumn("TABLE_CATALOG"),
            NewColumn("TABLE_SCHEMA"),
            NewColumn("TABLE_NAME"),
            NewColumn("COLUMN_NAME"),
            NewColumn("ORDINAL_POSITION", TypeInfo.TypeInteger),
            NewColumn("POSITION_IN_UNIQUE_CONSTRAINT", TypeInfo.TypeInteger),
        ],
        Parameters =>
        [
            NewColumn("SPECIFIC_CATALOG"),
            NewColumn("SPECIFIC_SCHEMA"),
            NewColumn("SPECIFIC_NAME"),
            NewColumn("ORDINAL_POSITION", TypeInfo.TypeInteger),
            NewColumn("PARAMETER_MODE"),
            NewColumn("IS_RESULT"),
            NewColumn("AS_LOCATOR"),
            NewColumn("PARAMETER_NAME"),
            NewColumn("DATA_TYPE"),
            NewColumn("CHARACTER_MAXIMUM_LENGTH", TypeInfo.TypeBigint),
            NewColumn("CHARACTER_OCTET_LENGTH", TypeInfo.TypeBigint),
            NewColumn("CHARACTER_SET_CATALOG"),
            NewColumn("CHARACTER_SET_SCHEMA"),
            NewColumn("CHARACTER_SET_NAME"),
            NewColumn("COLLATION_CATALOG"),
            NewColumn("COLLATION_SCHEMA"),
            NewColumn("COLLATION_NAME"),
            NewColumn("NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_PRECISION_RADIX", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_SCALE", TypeInfo.TypeInteger),
            NewColumn("DATETIME_PRECISION", TypeInfo.TypeInteger),
            NewColumn("INTERVAL_TYPE"),
            NewColumn("INTERVAL_PRECISION", TypeInfo.TypeInteger),
            NewColumn("MAXIMUM_CARDINALITY", TypeInfo.TypeInteger),
            NewColumn("DTD_IDENTIFIER"),
            NewColumn("DECLARED_DATA_TYPE"),
            NewColumn("DECLARED_NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("DECLARED_NUMERIC_SCALE", TypeInfo.TypeInteger),
            NewColumn("PARAMETER_DEFAULT"),
            // extensions
            NewColumn("GEOMETRY_TYPE"),
            NewColumn("GEOMETRY_SRID", TypeInfo.TypeInteger),
        ],
        ReferentialConstraints =>
        [
            NewColumn("CONSTRAINT_CATALOG"),
            NewColumn("CONSTRAINT_SCHEMA"),
            NewColumn("CONSTRAINT_NAME"),
            NewColumn("UNIQUE_CONSTRAINT_CATALOG"),
            NewColumn("UNIQUE_CONSTRAINT_SCHEMA"),
            NewColumn("UNIQUE_CONSTRAINT_NAME"),
            NewColumn("MATCH_OPTION"),
            NewColumn("UPDATE_RULE"),
            NewColumn("DELETE_RULE"),
        ],
        Routines =>
        [
            NewColumn("SPECIFIC_CATALOG"),
            NewColumn("SPECIFIC_SCHEMA"),
            NewColumn("SPECIFIC_NAME"),
            NewColumn("ROUTINE_CATALOG"),
            NewColumn("ROUTINE_SCHEMA"),
            NewColumn("ROUTINE_NAME"),
            NewColumn("ROUTINE_TYPE"),
            NewColumn("DATA_TYPE"),
            NewColumn("CHARACTER_MAXIMUM_LENGTH", TypeInfo.TypeBigint),
            NewColumn("CHARACTER_OCTET_LENGTH", TypeInfo.TypeBigint),
            NewColumn("CHARACTER_SET_CATALOG"),
            NewColumn("CHARACTER_SET_SCHEMA"),
            NewColumn("CHARACTER_SET_NAME"),
            NewColumn("COLLATION_CATALOG"),
            NewColumn("COLLATION_SCHEMA"),
            NewColumn("COLLATION_NAME"),
            NewColumn("NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_PRECISION_RADIX", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_SCALE", TypeInfo.TypeInteger),
            NewColumn("DATETIME_PRECISION", TypeInfo.TypeInteger),
            NewColumn("INTERVAL_TYPE"),
            NewColumn("INTERVAL_PRECISION", TypeInfo.TypeInteger),
            NewColumn("MAXIMUM_CARDINALITY", TypeInfo.TypeInteger),
            NewColumn("DTD_IDENTIFIER"),
            NewColumn("ROUTINE_BODY"),
            NewColumn("ROUTINE_DEFINITION"),
            NewColumn("EXTERNAL_NAME"),
            NewColumn("EXTERNAL_LANGUAGE"),
            NewColumn("PARAMETER_STYLE"),
            NewColumn("IS_DETERMINISTIC"),
            NewColumn("DECLARED_DATA_TYPE"),
            NewColumn("DECLARED_NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("DECLARED_NUMERIC_SCALE", TypeInfo.TypeInteger),
            // extensions
            NewColumn("GEOMETRY_TYPE"),
            NewColumn("GEOMETRY_SRID", TypeInfo.TypeInteger),
            NewColumn("REMARKS"),
        ],
        Schemata =>
        [
            NewColumn("CATALOG_NAME"),
            NewColumn("SCHEMA_NAME"),
            NewColumn("SCHEMA_OWNER"),
            NewColumn("DEFAULT_CHARACTER_SET_CATALOG"),
            NewColumn("DEFAULT_CHARACTER_SET_SCHEMA"),
            NewColumn("DEFAULT_CHARACTER_SET_NAME"),
            NewColumn("SQL_PATH"),
            // extensions
            NewColumn("DEFAULT_COLLATION_NAME"),
            NewColumn("REMARKS"),
        ],
        Sequences =>
        [
            NewColumn("SEQUENCE_CATALOG"),
            NewColumn("SEQUENCE_SCHEMA"),
            NewColumn("SEQUENCE_NAME"),
            NewColumn("DATA_TYPE"),
            NewColumn("NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_PRECISION_RADIX", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_SCALE", TypeInfo.TypeInteger),
            NewColumn("START_VALUE", TypeInfo.TypeBigint),
            NewColumn("MINIMUM_VALUE", TypeInfo.TypeBigint),
            NewColumn("MAXIMUM_VALUE", TypeInfo.TypeBigint),
            NewColumn("INCREMENT", TypeInfo.TypeBigint),
            NewColumn("CYCLE_OPTION"),
            NewColumn("DECLARED_DATA_TYPE"),
            NewColumn("DECLARED_NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("DECLARED_NUMERIC_SCALE", TypeInfo.TypeInteger),
            // extensions
            NewColumn("BASE_VALUE", TypeInfo.TypeBigint),
            NewColumn("CACHE", TypeInfo.TypeBigint),
            NewColumn("REMARKS"),
        ],
        Tables =>
        [
            NewColumn("TABLE_CATALOG"),
            NewColumn("TABLE_SCHEMA"),
            NewColumn("TABLE_NAME"),
            NewColumn("TABLE_TYPE"),
            NewColumn("IS_INSERTABLE_INTO"),
            NewColumn("COMMIT_ACTION"),
            // extensions
            NewColumn("STORAGE_TYPE"),
            NewColumn("REMARKS"),
            NewColumn("LAST_MODIFICATION", TypeInfo.TypeBigint),
            NewColumn("TABLE_CLASS"),
            NewColumn("ROW_COUNT_ESTIMATE", TypeInfo.TypeBigint),
        ],
        TableConstraints =>
        [
            NewColumn("CONSTRAINT_CATALOG"),
            NewColumn("CONSTRAINT_SCHEMA"),
            NewColumn("CONSTRAINT_NAME"),
            NewColumn("CONSTRAINT_TYPE"),
            NewColumn("TABLE_CATALOG"),
            NewColumn("TABLE_SCHEMA"),
            NewColumn("TABLE_NAME"),
            NewColumn("IS_DEFERRABLE"),
            NewColumn("INITIALLY_DEFERRED"),
            NewColumn("ENFORCED"),
            NewColumn("NULLS_DISTINCT"),
            // extensions
            NewColumn("INDEX_CATALOG"),
            NewColumn("INDEX_SCHEMA"),
            NewColumn("INDEX_NAME"),
            NewColumn("REMARKS"),
        ],
        TablePrivileges =>
        [
            NewColumn("GRANTOR"),
            NewColumn("GRANTEE"),
            NewColumn("TABLE_CATALOG"),
            NewColumn("TABLE_SCHEMA"),
            NewColumn("TABLE_NAME"),
            NewColumn("PRIVILEGE_TYPE"),
            NewColumn("IS_GRANTABLE"),
            NewColumn("WITH_HIERARCHY"),
        ],
        Triggers =>
        [
            NewColumn("TRIGGER_CATALOG"),
            NewColumn("TRIGGER_SCHEMA"),
            NewColumn("TRIGGER_NAME"),
            NewColumn("EVENT_MANIPULATION"),
            NewColumn("EVENT_OBJECT_CATALOG"),
            NewColumn("EVENT_OBJECT_SCHEMA"),
            NewColumn("EVENT_OBJECT_TABLE"),
            NewColumn("ACTION_ORIENTATION"),
            NewColumn("ACTION_TIMING"),
            // extensions
            NewColumn("IS_ROLLBACK", TypeInfo.TypeBoolean),
            NewColumn("JAVA_CLASS"),
            NewColumn("QUEUE_SIZE", TypeInfo.TypeInteger),
            NewColumn("NO_WAIT", TypeInfo.TypeBoolean),
            NewColumn("REMARKS"),
        ],
        Views =>
        [
            NewColumn("TABLE_CATALOG"),
            NewColumn("TABLE_SCHEMA"),
            NewColumn("TABLE_NAME"),
            NewColumn("VIEW_DEFINITION"),
            NewColumn("CHECK_OPTION"),
            NewColumn("IS_UPDATABLE"),
            NewColumn("INSERTABLE_INTO"),
            NewColumn("IS_TRIGGER_UPDATABLE"),
            NewColumn("IS_TRIGGER_DELETABLE"),
            NewColumn("IS_TRIGGER_INSERTABLE_INTO"),
            // extensions
            NewColumn("STATUS"),
            NewColumn("REMARKS"),
        ],
        Constants =>
        [
            NewColumn("CONSTANT_CATALOG"),
            NewColumn("CONSTANT_SCHEMA"),
            NewColumn("CONSTANT_NAME"),
            NewColumn("VALUE_DEFINITION"),
            NewColumn("DATA_TYPE"),
            NewColumn("CHARACTER_MAXIMUM_LENGTH", TypeInfo.TypeBigint),
            NewColumn("CHARACTER_OCTET_LENGTH", TypeInfo.TypeBigint),
            NewColumn("CHARACTER_SET_CATALOG"),
            NewColumn("CHARACTER_SET_SCHEMA"),
            NewColumn("CHARACTER_SET_NAME"),
            NewColumn("COLLATION_CATALOG"),
            NewColumn("COLLATION_SCHEMA"),
            NewColumn("COLLATION_NAME"),
            NewColumn("NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_PRECISION_RADIX", TypeInfo.TypeInteger),
            NewColumn("NUMERIC_SCALE", TypeInfo.TypeInteger),
            NewColumn("DATETIME_PRECISION", TypeInfo.TypeInteger),
            NewColumn("INTERVAL_TYPE"),
            NewColumn("INTERVAL_PRECISION", TypeInfo.TypeInteger),
            NewColumn("MAXIMUM_CARDINALITY", TypeInfo.TypeInteger),
            NewColumn("DTD_IDENTIFIER"),
            NewColumn("DECLARED_DATA_TYPE"),
            NewColumn("DECLARED_NUMERIC_PRECISION", TypeInfo.TypeInteger),
            NewColumn("DECLARED_NUMERIC_SCALE", TypeInfo.TypeInteger),
            NewColumn("GEOMETRY_TYPE"),
            NewColumn("GEOMETRY_SRID", TypeInfo.TypeInteger),
            NewColumn("REMARKS"),
        ],
        EnumValues =>
        [
            NewColumn("OBJECT_CATALOG"),
            NewColumn("OBJECT_SCHEMA"),
            NewColumn("OBJECT_NAME"),
            NewColumn("OBJECT_TYPE"),
            NewColumn("ENUM_IDENTIFIER"),
            NewColumn("VALUE_NAME"),
            NewColumn("VALUE_ORDINAL"),
        ],
        Indexes =>
        [
            NewColumn("INDEX_CATALOG"),
            NewColumn("INDEX_SCHEMA"),
            NewColumn("INDEX_NAME"),
            NewColumn("TABLE_CATALOG"),
            NewColumn("TABLE_SCHEMA"),
            NewColumn("TABLE_NAME"),
            NewColumn("INDEX_TYPE_NAME"),
            NewColumn("NULLS_DISTINCT"),
            NewColumn("IS_GENERATED", TypeInfo.TypeBoolean),
            NewColumn("REMARKS"),
            NewColumn("INDEX_CLASS"),
        ],
        IndexColumns =>
        [
            NewColumn("INDEX_CATALOG"),
            NewColumn("INDEX_SCHEMA"),
            NewColumn("INDEX_NAME"),
            NewColumn("TABLE_CATALOG"),
            NewColumn("TABLE_SCHEMA"),
            NewColumn("TABLE_NAME"),
            NewColumn("COLUMN_NAME"),
            NewColumn("ORDINAL_POSITION", TypeInfo.TypeInteger),
            NewColumn("ORDERING_SPECIFICATION"),
            NewColumn("NULL_ORDERING"),
            NewColumn("IS_UNIQUE", TypeInfo.TypeBoolean),
        ],
        InDoubt =>
        [
            NewColumn("TRANSACTION_NAME"),
            NewColumn("TRANSACTION_STATE"),
        ],
        Locks =>
        [
            NewColumn("TABLE_SCHEMA"),
            NewColumn("TABLE_NAME"),
            NewColumn("SESSION_ID", TypeInfo.TypeInteger),
            NewColumn("LOCK_TYPE"),
        ],
        QueryStatistics =>
        [
            NewColumn("SQL_STATEMENT"),
            NewColumn("EXECUTION_COUNT", TypeInfo.TypeInteger),
            NewColumn("MIN_EXECUTION_TIME", TypeInfo.TypeDouble),
            NewColumn("MAX_EXECUTION_TIME", TypeInfo.TypeDouble),
            NewColumn("CUMULATIVE_EXECUTION_TIME", TypeInfo.TypeDouble),
            NewColumn("AVERAGE_EXECUTION_TIME", TypeInfo.TypeDouble),
            NewColumn("STD_DEV_EXECUTION_TIME", TypeInfo.TypeDouble),
            NewColumn("MIN_ROW_COUNT", TypeInfo.TypeBigint),
            NewColumn("MAX_ROW_COUNT", TypeInfo.TypeBigint),
            NewColumn("CUMULATIVE_ROW_COUNT", TypeInfo.TypeBigint),
            NewColumn("AVERAGE_ROW_COUNT", TypeInfo.TypeDouble),
            NewColumn("STD_DEV_ROW_COUNT", TypeInfo.TypeDouble),
        ],
        Rights =>
        [
            NewColumn("GRANTEE"),
            NewColumn("GRANTEETYPE"),
            NewColumn("GRANTEDROLE"),
            NewColumn("RIGHTS"),
            NewColumn("TABLE_SCHEMA"),
            NewColumn("TABLE_NAME"),
        ],
        Roles =>
        [
            NewColumn("ROLE_NAME"),
            NewColumn("REMARKS"),
        ],
        Sessions =>
        [
            NewColumn("SESSION_ID", TypeInfo.TypeInteger),
            NewColumn("USER_NAME"),
            NewColumn("SERVER"),
            NewColumn("CLIENT_ADDR"),
            NewColumn("CLIENT_INFO"),
            NewColumn("SESSION_START", TypeInfo.TypeTimestampTz),
            NewColumn("ISOLATION_LEVEL"),
            NewColumn("EXECUTING_STATEMENT"),
            NewColumn("EXECUTING_STATEMENT_START", TypeInfo.TypeTimestampTz),
            NewColumn("CONTAINS_UNCOMMITTED", TypeInfo.TypeBoolean),
            NewColumn("SESSION_STATE"),
            NewColumn("BLOCKER_ID", TypeInfo.TypeInteger),
            NewColumn("SLEEP_SINCE", TypeInfo.TypeTimestampTz),
        ],
        SessionState =>
        [
            NewColumn("STATE_KEY"),
            NewColumn("STATE_COMMAND"),
        ],
        Settings =>
        [
            NewColumn("SETTING_NAME"),
            NewColumn("SETTING_VALUE"),
        ],
        Synonyms =>
        [
            NewColumn("SYNONYM_CATALOG"),
            NewColumn("SYNONYM_SCHEMA"),
            NewColumn("SYNONYM_NAME"),
            NewColumn("SYNONYM_FOR"),
            NewColumn("SYNONYM_FOR_SCHEMA"),
            NewColumn("TYPE_NAME"),
            NewColumn("STATUS"),
            NewColumn("REMARKS"),
        ],
        Users =>
        [
            NewColumn("USER_NAME"),
            NewColumn("IS_ADMIN", TypeInfo.TypeBoolean),
            NewColumn("REMARKS"),
        ],
        _ => throw DbException.GetInternalError("type=" + type),
    };

    /// <summary>
    /// Creates a column with the given name and the character string data type.
    /// </summary>
    /// <param name="name">the column name</param>
    /// <returns>the column</returns>
    private static Column NewColumn(string name) => new(name, TypeInfo.TypeVarchar);

    /// <summary>
    /// Creates a column with the given name and data type.
    /// </summary>
    /// <param name="name">the column name</param>
    /// <param name="type">the data type</param>
    /// <returns>the column</returns>
    private static Column NewColumn(string name, TypeInfo type) => new(name, type);

    /// <inheritdoc/>
    public override string GetTableName() => name;

    /// <inheritdoc/>
    /// <remarks>
    /// The reference has views and standard tables among the metadata: the standard
    /// tables are the ones that are not views, and the catalogue says which is which.
    /// </remarks>
    public override bool IsView() => IsViewType(type);

    /// <summary>
    /// Whether the view of this type is a view, as the reference says.
    /// </summary>
    /// <param name="type">the meta table type</param>
    /// <returns>true if it is a view</returns>
    public static bool IsViewType(int type) => type switch
    {
        InformationSchemaCatalogName => false,
        Constants => false,
        EnumValues => false,
        Indexes => false,
        IndexColumns => false,
        InDoubt => false,
        Locks => false,
        QueryStatistics => false,
        Rights => false,
        Roles => false,
        Sessions => false,
        SessionState => false,
        Settings => false,
        Synonyms => false,
        Users => false,
        _ => true,
    };

    /// <inheritdoc/>
    public override int GetTableType() => SYSTEM_TABLE;

    /// <inheritdoc/>
    public override int GetColumnCount() => columns.Length;

    /// <inheritdoc/>
    public override Column GetColumn(int index) => columns[index];

    /// <inheritdoc/>
    public override Column[] GetColumns() => columns;

    /// <inheritdoc/>
    public override Index? GetScanIndex(SessionLocal session) => index;

    /// <inheritdoc/>
    public override Index[] GetIndexes() => [index];

    /// <inheritdoc/>
    public override Index? GetIndexFor(SessionLocal session, SearchRow row) => GetScanIndex(session);

    /// <inheritdoc/>
    public override long GetRowCountApproximation(SessionLocal session) => RowCountApproximation;

    /// <inheritdoc/>
    public override bool CanGetRowCount(SessionLocal session) => false;

    /// <inheritdoc/>
    public override long GetRowCount(SessionLocal session)
        => throw DbException.GetInternalError(name);

    /// <inheritdoc/>
    public override string? GetCreateSql() => null;

    /// <inheritdoc/>
    public override string GetSql(int sqlFlags) => name;

    /// <inheritdoc/>
    public override StringBuilder GetSql(StringBuilder builder, int sqlFlags) => builder.Append(name);

    /// <inheritdoc/>
    public override void Truncate(SessionLocal session)
    {
    }

    /// <inheritdoc/>
    public override void RemoveChildrenAndResources(SessionLocal session)
    {
    }

    /// <inheritdoc/>
    public override void Close()
    {
    }

    /// <inheritdoc/>
    public override bool IsClosed() => false;

    /// <inheritdoc/>
    public override Schema? GetSchema() => schema;

    /// <summary>
    /// The rows of this view, computed from the metadata of the database.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the rows</returns>
    internal List<Value[]> GenerateRows(SessionLocal session)
    {
        List<Value[]> rows = [];
        Database database = session.GetDatabase();
        string catalog = database.GetShortName();
        string mainSchemaName = database.GetMainSchema()?.GetName() ?? string.Empty;
        string collation = database.GetCompareMode().GetName();
        switch (type)
        {
            case InformationSchemaCatalogName:
                Add(rows, catalog);
                break;
            case Collations:
                // The reference lists the collations of the compare mode; this port has one
                // compare mode and no locales of its own.
                Add(rows, catalog, mainSchemaName, CompareMode.Off, "NO PAD", null);
                break;
            case Schemata:
                foreach (Schema schema in database.GetSchemas())
                {
                    Add(
                        rows,
                        catalog,
                        schema.GetName(),
                        session.GetUserName(),
                        catalog,
                        mainSchemaName,
                        CharacterSetName,
                        null,
                        collation,
                        schema.GetComment());
                }

                break;
            case Tables:
                foreach (Table table in GetAllTables(session))
                {
                    Add(
                        rows,
                        catalog,
                        table.GetSchema()?.GetName(),
                        table.GetTableName(),
                        GetTableTypeName(table),
                        table is InformationSchemaTable ? "NO" : "YES",
                        null,
                        GetStorageType(table),
                        table.GetComment(),
                        null,
                        GetTableClass(table), ValueBigint.Get(table.GetRowCountApproximation(session)));
                }

                break;
            case Columns:
                foreach (Table table in GetAllTables(session))
                {
                    AddColumns(session, rows, catalog, mainSchemaName, collation, table);
                }

                break;
            case TableConstraints:
                foreach (Constraint constraint in GetAllConstraints(session))
                {
                    AddTableConstraints(session, rows, catalog, constraint);
                }

                break;
            case ReferentialConstraints:
                foreach (Constraint constraint in GetAllConstraints(session))
                {
                    if (constraint is ConstraintReferential referential)
                    {
                        AddReferentialConstraints(session, rows, catalog, referential);
                    }
                }

                break;
            case KeyColumnUsage:
                foreach (Constraint constraint in GetAllConstraints(session))
                {
                    AddKeyColumnUsage(session, rows, catalog, constraint);
                }

                break;
            case ConstraintColumnUsage:
                foreach (Constraint constraint in GetAllConstraints(session))
                {
                    AddConstraintColumnUsage(session, rows, catalog, constraint);
                }

                break;
            case Indexes:
                foreach (Table table in GetAllTables(session))
                {
                    foreach (Index index in table.GetIndexes())
                    {
                        if (index.GetCreateSql() is null)
                        {
                            continue;
                        }

                        AddIndexes(session, rows, catalog, table, index);
                    }
                }

                break;
            case IndexColumns:
                foreach (Table table in GetAllTables(session))
                {
                    foreach (Index index in table.GetIndexes())
                    {
                        if (index.GetCreateSql() is null)
                        {
                            continue;
                        }

                        AddIndexColumns(session, rows, catalog, table, index);
                    }
                }

                break;
            case Settings:
                // The reference lists the settings of the database and of the session; this
                // port has neither, and what it does have is the version it is.
                Add(
                    rows,
                    "info.VERSION_MAJOR",
                    H2Sharp.Engine.Constants.VersionMajor.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Add(
                    rows,
                    "info.VERSION_MINOR",
                    H2Sharp.Engine.Constants.VersionMinor.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case Views:
                foreach (Table table in GetAllTables(session))
                {
                    if (!table.IsView())
                    {
                        continue;
                    }

                    // The reference reads the definition of a view and the instead-of
                    // triggers over it; this port has neither, so the definition is null
                    // and the triggers say "NO".
                    Add(
                        rows,
                        catalog,
                        table.GetSchema()?.GetName(),
                        table.GetTableName(),
                        null,
                        "NONE",
                        "NO",
                        "NO",
                        "NO",
                        "NO",
                        "NO",
                        "VALID",
                        table.GetComment());
                }

                break;
            case Sequences:
                foreach (Schemas.Schema schema in database.GetSchemas())
                {
                    foreach (Schemas.Sequence sequence in schema.GetAllSequences())
                    {
                        // The sequence of an identity column belongs to its table and is not
                        // a sequence of the schema.
                        if (sequence.GetBelongsToTable())
                        {
                            continue;
                        }

                        AddSequence(session, rows, catalog, sequence);
                    }
                }

                break;
            case Locks:
            case InDoubt:
            case QueryStatistics:
            case SessionState:
            case Sessions:
            case Users:
            case Roles:
            case Rights:
            case TablePrivileges:
            case ColumnPrivileges:
            case Parameters:
            case Routines:
            case Constants:
            case Domains:
            case DomainConstraints:
            case ElementTypes:
            case Fields:
            case EnumValues:
            case CheckConstraints:
            case Triggers:
            case Synonyms:
                // The objects these views list do not exist in this port yet, so the views
                // are empty. They are here with the columns of the reference, so that a query
                // against one of them is answered rather than refused.
                break;
            default:
                throw DbException.GetInternalError("type=" + type);
        }

        return rows;
    }

    /// <summary>
    /// All the tables of the database, the schemas and their tables and views.
    /// </summary>
    /// <param name="session">the session</param>
    /// <returns>the tables</returns>
    private static List<Table> GetAllTables(SessionLocal session)
    {
        List<Table> tables = [];
        foreach (Schema schema in session.GetDatabase().GetSchemas())
        {
            tables.AddRange(schema.GetAllTablesAndViews(session));
        }

        return tables;
    }

    /// <summary>
    /// All the constraints of the database, the constraints of the tables of the schemas.
    /// </summary>
    /// <remarks>
    /// The reference walks the registry of each schema, where every constraint is one object.
    /// That walk is not ported, so this walks the tables instead — and a referential
    /// constraint is on two tables' lists, the one it belongs to and the one it points at.
    /// The answers are the same objects, so the duplicates are dropped by identity: the set
    /// is what the reference lists, in the order the tables list it.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <returns>the constraints</returns>
    private static List<Constraint> GetAllConstraints(SessionLocal session)
    {
        List<Constraint> constraints = [];
        foreach (Table table in GetAllTables(session))
        {
            foreach (Constraint constraint in table.GetConstraints())
            {
                if (!constraints.Contains(constraint))
                {
                    constraints.Add(constraint);
                }
            }
        }

        return constraints;
    }

    /// <summary>
    /// The row of the SEQUENCES view for one sequence.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="rows">the rows</param>
    /// <param name="catalog">the catalog name</param>
    /// <param name="sequence">the sequence</param>
    private static void AddSequence(
        SessionLocal session,
        List<Value[]> rows,
        string catalog,
        Schemas.Sequence sequence)
    {
        DataTypeInformation dt = DataTypeInformation.ValueOf(sequence.GetDataType());
        Schemas.Sequence.Cycle cycle = sequence.GetCycle();
        Add(
            rows,
            // SEQUENCE_CATALOG
            catalog,
            // SEQUENCE_SCHEMA
            sequence.GetSchema()?.GetName(),
            // SEQUENCE_NAME
            sequence.GetName(),
            // DATA_TYPE
            dt.DataType,
            // NUMERIC_PRECISION
            ValueInteger.Get(sequence.GetEffectivePrecision()),
            // NUMERIC_PRECISION_RADIX
            dt.NumericPrecisionRadix,
            // NUMERIC_SCALE
            dt.NumericScale,
            // START_VALUE
            ValueBigint.Get(sequence.GetStartValue()),
            // MINIMUM_VALUE
            ValueBigint.Get(sequence.GetMinValue()),
            // MAXIMUM_VALUE
            ValueBigint.Get(sequence.GetMaxValue()),
            // INCREMENT
            ValueBigint.Get(sequence.GetIncrement()),
            // CYCLE_OPTION
            cycle == Schemas.Sequence.Cycle.CYCLE ? "YES" : "NO",
            // DECLARED_DATA_TYPE
            dt.DeclaredDataType,
            // DECLARED_NUMERIC_PRECISION
            dt.DeclaredNumericPrecision,
            // DECLARED_NUMERIC_SCALE
            dt.DeclaredNumericScale,
            // extensions
            // BASE_VALUE
            cycle != Schemas.Sequence.Cycle.EXHAUSTED ? ValueBigint.Get(sequence.GetBaseValue()) : null,
                        // CACHE
                        ValueBigint.Get(sequence.GetCacheSize()),
                        // REMARKS
                        sequence.GetComment());
    }

    /// <summary>
    /// The rows of the COLUMNS view for one table.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="rows">the rows</param>
    /// <param name="catalog">the catalog name</param>
    /// <param name="mainSchemaName">the name of the main schema</param>
    /// <param name="collation">the name of the compare mode</param>
    /// <param name="table">the table</param>
    private static void AddColumns(
        SessionLocal session,
        List<Value[]> rows,
        string catalog,
        string mainSchemaName,
        string collation,
        Table table)
    {
        Column[] cols = table.GetColumns();
        for (int i = 0; i < cols.Length; i++)
        {
            Column c = cols[i];
            TypeInfo typeInfo = c.GetTypeInfo();
            DataTypeInformation dt = DataTypeInformation.ValueOf(typeInfo);
            string? characterSetCatalog = null;
            string? characterSetSchema = null;
            string? characterSetName = null;
            string? collationName = null;
            if (dt.HasCharsetAndCollation)
            {
                characterSetCatalog = catalog;
                characterSetSchema = mainSchemaName;
                characterSetName = CharacterSetName;
                collationName = collation;
            }

            string? columnDefault = null;
            string isGenerated;
            string? generationExpression = null;
            string? onUpdate = null;
            string isIdentity;
            string? identityGeneration = null;
            Value? identityStart = null;
            Value? identityIncrement = null;
            Value? identityMaximum = null;
            Value? identityMinimum = null;
            string? identityCycle = null;
            Value? identityBase = null;
            Value? identityCache = null;
            Schemas.Sequence? sequence = c.GetSequence();
            if (sequence is not null)
            {
                // An identity column takes its value from a sequence, and it has no default.
                columnDefault = null;
                isGenerated = "NEVER";
                generationExpression = null;
                isIdentity = "YES";
                identityGeneration = c.IsGeneratedAlways() ? "ALWAYS" : "BY DEFAULT";
                identityStart = ValueBigint.Get(sequence.GetStartValue());
                identityIncrement = ValueBigint.Get(sequence.GetIncrement());
                identityMaximum = ValueBigint.Get(sequence.GetMaxValue());
                identityMinimum = ValueBigint.Get(sequence.GetMinValue());
                Schemas.Sequence.Cycle cycle = sequence.GetCycle();
                identityCycle = cycle == Schemas.Sequence.Cycle.CYCLE ? "YES" : "NO";
                identityBase = cycle != Schemas.Sequence.Cycle.EXHAUSTED
                    ? ValueBigint.Get(sequence.GetBaseValue())
                    : null;
                identityCache = ValueBigint.Get(sequence.GetCacheSize());
            }
            else
            {
                if (c.IsGenerated())
                {
                    isGenerated = "ALWAYS";
                    generationExpression = c.GetDefaultExpression() is null
                        ? null
                        : c.GetDefaultExpression()!.GetUnenclosedSql(new StringBuilder(), HasSql.DefaultSqlFlags)
                            .ToString();
                }
                else
                {
                    columnDefault = c.GetDefaultExpression() is null
                        ? null
                        : c.GetDefaultExpression()!.GetUnenclosedSql(new StringBuilder(), HasSql.DefaultSqlFlags)
                            .ToString();
                    isGenerated = "NEVER";
                }

                isIdentity = "NO";
            }

            if (c.GetOnUpdateExpression() is not null)
            {
                onUpdate = c.GetOnUpdateExpression()!
                    .GetUnenclosedSql(new StringBuilder(), HasSql.DefaultSqlFlags).ToString();
            }

            // The values are in the order of the columns the view declares, and there is one
            // value for every column of it: the reader walks the row by that order, and a row
            // that is shorter leaves it outside the array.
            Add(
                rows,
                // TABLE_CATALOG
                catalog,
                // TABLE_SCHEMA
                table.GetSchema()?.GetName(),
                // TABLE_NAME
                table.GetTableName(),
                // COLUMN_NAME
                c.GetName(),
                // ORDINAL_POSITION
                ValueInteger.Get(i + 1),
                // COLUMN_DEFAULT
                columnDefault,
                // IS_NULLABLE
                c.IsNullable() == Column.Nullable ? "YES" : "NO",
                // DATA_TYPE
                dt.DataType,
                // CHARACTER_MAXIMUM_LENGTH
                dt.CharacterPrecision,
                // CHARACTER_OCTET_LENGTH
                dt.CharacterPrecision,
                // NUMERIC_PRECISION
                dt.NumericPrecision,
                // NUMERIC_PRECISION_RADIX
                dt.NumericPrecisionRadix,
                // NUMERIC_SCALE
                dt.NumericScale,
                // DATETIME_PRECISION
                dt.DateTimePrecision,
                // INTERVAL_TYPE
                dt.IntervalType,
                // INTERVAL_PRECISION
                dt.IntervalPrecision,
                // CHARACTER_SET_CATALOG
                characterSetCatalog,
                // CHARACTER_SET_SCHEMA
                characterSetSchema,
                // CHARACTER_SET_NAME
                characterSetName,
                // COLLATION_CATALOG
                characterSetCatalog,
                // COLLATION_SCHEMA
                characterSetSchema,
                // COLLATION_NAME
                collationName,
                // DOMAIN_CATALOG
                null,
                // DOMAIN_SCHEMA
                null,
                // DOMAIN_NAME
                null,
                // MAXIMUM_CARDINALITY
                dt.MaximumCardinality,
                // DTD_IDENTIFIER
                (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                // IS_IDENTITY
                isIdentity,
                // IDENTITY_GENERATION
                identityGeneration,
                // IDENTITY_START
                identityStart,
                // IDENTITY_INCREMENT
                identityIncrement,
                // IDENTITY_MAXIMUM
                identityMaximum,
                // IDENTITY_MINIMUM
                identityMinimum,
                // IDENTITY_CYCLE
                identityCycle,
                // IS_GENERATED
                isGenerated,
                // GENERATION_EXPRESSION
                generationExpression,
                // DECLARED_DATA_TYPE
                dt.DeclaredDataType,
                // DECLARED_NUMERIC_PRECISION
                dt.DeclaredNumericPrecision,
                // DECLARED_NUMERIC_SCALE
                dt.DeclaredNumericScale,
                // extensions
                // GEOMETRY_TYPE
                dt.GeometryType,
                // GEOMETRY_SRID
                dt.GeometrySrid,
                // IDENTITY_BASE
                identityBase,
                // IDENTITY_CACHE
                identityCache,
                // COLUMN_ON_UPDATE
                onUpdate,
                // IS_VISIBLE
                ValueBoolean.Get(c.GetVisible()),
                // DEFAULT_ON_NULL
                ValueBoolean.Get(c.IsDefaultOnNull()),
                // SELECTIVITY
                ValueInteger.Get(c.GetSelectivity()),
                // REMARKS
                c.GetComment());
        }
    }

    /// <summary>
    /// The row of the TABLE_CONSTRAINTS view for one constraint.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="rows">the rows</param>
    /// <param name="catalog">the catalog name</param>
    /// <param name="constraint">the constraint</param>
    private static void AddTableConstraints(
        SessionLocal session,
        List<Value[]> rows,
        string catalog,
        Constraint constraint)
    {
        Constraint.Type constraintType = constraint.GetConstraintType();
        Table table = constraint.GetTable();
        Index? index = constraint.GetIndex();
        Add(
            rows,
            catalog,
            constraint.GetSchema()?.GetName(),
            constraint.GetName(),
            ConstraintTypeRules.GetSqlName(constraintType),
            catalog,
            table.GetSchema()?.GetName(),
            table.GetTableName(),
            "NO",
            "NO",
            "YES",
            constraintType == Constraint.Type.UNIQUE && constraint is ConstraintUnique unique
                ? NullsDistinctToString(unique.GetNullsDistinct())
                : null,
            index is null ? null : catalog,
            index?.GetSchema()?.GetName(),
            index?.GetName(),
            null);
    }

    /// <summary>
    /// The row of the REFERENTIAL_CONSTRAINTS view for one foreign key.
    /// </summary>
    /// <remarks>
    /// The reference's rows, verbatim: the constraint and the unique constraint it points
    /// at, MATCH_OPTION NONE, and the two rules in their SQL spelling.
    /// </remarks>
    /// <param name="session">the session</param>
    /// <param name="rows">the rows</param>
    /// <param name="catalog">the catalog name</param>
    /// <param name="constraint">the constraint</param>
    private static void AddReferentialConstraints(
        SessionLocal session,
        List<Value[]> rows,
        string catalog,
        ConstraintReferential constraint)
    {
        ConstraintUnique unique = constraint.GetReferencedConstraint()!;
        Add(
            rows,
            catalog,
            constraint.GetSchema()?.GetName(),
            constraint.GetName(),
            catalog,
            unique.GetSchema()?.GetName(),
            unique.GetName(),
            "NONE",
            ConstraintActionTypeRules.GetSqlName(constraint.GetUpdateAction()),
            ConstraintActionTypeRules.GetSqlName(constraint.GetDeleteAction()));
    }

    /// <summary>
    /// The rows of the KEY_COLUMN_USAGE view for one constraint.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="rows">the rows</param>
    /// <param name="catalog">the catalog name</param>
    /// <param name="constraint">the constraint</param>
    private static void AddKeyColumnUsage(
        SessionLocal session,
        List<Value[]> rows,
        string catalog,
        Constraint constraint)
    {
        IndexColumn[]? indexColumns = constraint switch
        {
            ConstraintUnique unique => unique.GetColumns(),
            ConstraintReferential referential => referential.GetColumns(),
            _ => null,
        };

        if (indexColumns is null)
        {
            return;
        }

        Table table = constraint.GetTable();
        for (int i = 0; i < indexColumns.Length; i++)
        {
            Add(
                rows,
                catalog,
                constraint.GetSchema()?.GetName(),
                constraint.GetName(),
                catalog,
                table.GetSchema()?.GetName(),
                table.GetTableName(),
                indexColumns[i].GetColumn()?.GetName(),
                ValueInteger.Get(i + 1),
                null);
        }
    }

    /// <summary>
    /// The rows of the CONSTRAINT_COLUMN_USAGE view for one constraint.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="rows">the rows</param>
    /// <param name="catalog">the catalog name</param>
    /// <param name="constraint">the constraint</param>
    private static void AddConstraintColumnUsage(
        SessionLocal session,
        List<Value[]> rows,
        string catalog,
        Constraint constraint)
    {
        Table table = constraint.GetTable();
        foreach (Column column in constraint.GetReferencedColumns(table))
        {
            Add(
                rows,
                catalog,
                column.GetTable()?.GetSchema()?.GetName(),
                column.GetTable()?.GetTableName(),
                column.GetName(),
                catalog,
                constraint.GetSchema()?.GetName(),
                constraint.GetName());
        }
    }

    /// <summary>
    /// The row of the INDEXES view for one index.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="rows">the rows</param>
    /// <param name="catalog">the catalog name</param>
    /// <param name="table">the table</param>
    /// <param name="index">the index</param>
    private static void AddIndexes(
        SessionLocal session,
        List<Value[]> rows,
        string catalog,
        Table table,
        Index index)
    {
        IndexType? indexType = index.GetIndexType();
        Add(
            rows,
            catalog,
            index.GetSchema()?.GetName(),
            index.GetName(),
            catalog,
            table.GetSchema()?.GetName(),
            table.GetTableName(),
            indexType?.GetSql(false),
            NullsDistinctToString(indexType?.GetNullsDistinct()),
            ValueBoolean.Get(indexType?.GetBelongsToConstraint() ?? false),
            index.GetComment(),
            index.GetType().Name);
    }

    /// <summary>
    /// The rows of the INDEX_COLUMNS view for one index.
    /// </summary>
    /// <param name="session">the session</param>
    /// <param name="rows">the rows</param>
    /// <param name="catalog">the catalog name</param>
    /// <param name="table">the table</param>
    /// <param name="index">the index</param>
    private static void AddIndexColumns(
        SessionLocal session,
        List<Value[]> rows,
        string catalog,
        Table table,
        Index index)
    {
        IndexColumn[] cols = index.GetColumns();
        int uniqueColumnCount = index.GetUniqueColumnCount();
        for (int i = 0; i < cols.Length; i++)
        {
            IndexColumn idxCol = cols[i];
            int sortType = idxCol.SortType;
            Add(
                rows,
                catalog,
                index.GetSchema()?.GetName(),
                index.GetName(),
                catalog,
                table.GetSchema()?.GetName(),
                table.GetTableName(),
                idxCol.GetColumn()?.GetName(),
                ValueInteger.Get(i + 1),
                (sortType & Result.SortOrder.Descending) == 0 ? "ASC" : "DESC",
                (sortType & Result.SortOrder.NullsFirst) != 0
                    ? "FIRST"
                    : (sortType & Result.SortOrder.NullsLast) != 0 ? "LAST" : null,
                ValueBoolean.Get(i + 1 <= uniqueColumnCount));
        }
    }

    /// <summary>
    /// The name of the kind of the table, as the reference writes it in the TABLES view.
    /// </summary>
    /// <param name="table">the table</param>
    /// <returns>the name</returns>
    private static string GetTableTypeName(Table table) => table.IsView() ? "VIEW" : "BASE TABLE";

    /// <summary>
    /// How the table is stored.
    /// </summary>
    /// <param name="table">the table</param>
    /// <returns>the name</returns>
    private static string GetStorageType(Table table) => "MEMORY";

    /// <summary>
    /// The class of the table, as the reference writes it in the TABLES view.
    /// </summary>
    /// <param name="table">the table</param>
    /// <returns>the name</returns>
    private static string GetTableClass(Table table) => table.GetType().Name;

    /// <summary>
    /// The NULLS_DISTINCT of a constraint or an index, as the reference writes it.
    /// </summary>
    /// <param name="nullsDistinct">the setting</param>
    /// <returns>the name</returns>
    private static string? NullsDistinctToString(NullsDistinct? nullsDistinct) => nullsDistinct switch
    {
        NullsDistinct.Distinct => "YES",
        NullsDistinct.AllDistinct => "ALL",
        NullsDistinct.NotDistinct => "NO",
        _ => null,
    };

    /// <summary>
    /// Adds a row: a string stays a string, a null stays a null, and a value stays a value.
    /// </summary>
    /// <param name="rows">the rows</param>
    /// <param name="values">the values of the row</param>
    private static void Add(List<Value[]> rows, params object?[] values)
    {
        Value[] converted = new Value[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            converted[i] = values[i] switch
            {
                null => ValueNull.Instance,
                string s => ValueVarchar.Get(s),
                Value v => v,
                _ => throw DbException.GetInternalError("value type: " + values[i]!.GetType().Name),
            };
        }

        rows.Add(converted);
    }

    /// <summary>
    /// What the metadata says about a data type: the pieces of it that the views of the
    /// catalogue show.
    /// </summary>
    private sealed class DataTypeInformation
    {
        /// <summary>DATA_TYPE.</summary>
        internal string DataType { get; }

        /// <summary>CHARACTER_MAXIMUM_LENGTH.</summary>
        internal Value? CharacterPrecision { get; }

        /// <summary>NUMERIC_PRECISION.</summary>
        internal Value? NumericPrecision { get; }

        /// <summary>NUMERIC_PRECISION_RADIX.</summary>
        internal Value? NumericPrecisionRadix { get; }

        /// <summary>NUMERIC_SCALE.</summary>
        internal Value? NumericScale { get; }

        /// <summary>DATETIME_PRECISION.</summary>
        internal Value? DateTimePrecision { get; }

        /// <summary>INTERVAL_PRECISION.</summary>
        internal Value? IntervalPrecision { get; }

        /// <summary>INTERVAL_TYPE.</summary>
        internal Value? IntervalType { get; }

        /// <summary>MAXIMUM_CARDINALITY.</summary>
        internal Value? MaximumCardinality { get; }

        /// <summary>Whether the type has a character set and a collation.</summary>
        internal bool HasCharsetAndCollation { get; }

        /// <summary>DECLARED_DATA_TYPE.</summary>
        internal string? DeclaredDataType { get; }

        /// <summary>DECLARED_NUMERIC_PRECISION.</summary>
        internal Value? DeclaredNumericPrecision { get; }

        /// <summary>DECLARED_NUMERIC_SCALE.</summary>
        internal Value? DeclaredNumericScale { get; }

        /// <summary>GEOMETRY_TYPE.</summary>
        internal string? GeometryType { get; }

        /// <summary>GEOMETRY_SRID.</summary>
        internal Value? GeometrySrid { get; }

        private DataTypeInformation(
            string dataType,
            Value? characterPrecision,
            Value? numericPrecision,
            Value? numericPrecisionRadix,
            Value? numericScale,
            Value? datetimePrecision,
            Value? intervalPrecision,
            Value? intervalType,
            Value? maximumCardinality,
            bool hasCharsetAndCollation,
            string? declaredDataType,
            Value? declaredNumericPrecision,
            Value? declaredNumericScale,
            string? geometryType,
            Value? geometrySrid)
        {
            DataType = dataType;
            CharacterPrecision = characterPrecision;
            NumericPrecision = numericPrecision;
            NumericPrecisionRadix = numericPrecisionRadix;
            NumericScale = numericScale;
            DateTimePrecision = datetimePrecision;
            IntervalPrecision = intervalPrecision;
            IntervalType = intervalType;
            MaximumCardinality = maximumCardinality;
            HasCharsetAndCollation = hasCharsetAndCollation;
            DeclaredDataType = declaredDataType;
            DeclaredNumericPrecision = declaredNumericPrecision;
            DeclaredNumericScale = declaredNumericScale;
            GeometryType = geometryType;
            GeometrySrid = geometrySrid;
        }

        /// <summary>
        /// What the metadata says about the type.
        /// </summary>
        /// <param name="typeInfo">the type</param>
        /// <returns>the information</returns>
        internal static DataTypeInformation ValueOf(TypeInfo typeInfo)
        {
            int type = typeInfo.GetValueType();
            string dataType = Value.GetTypeName(type);
            Value? characterPrecision = null;
            Value? numericPrecision = null;
            Value? numericScale = null;
            Value? numericPrecisionRadix = null;
            Value? datetimePrecision = null;
            Value? intervalPrecision = null;
            Value? maximumCardinality = null;
            string? intervalType = null;
            bool hasCharsetAndCollation = false;
            string? declaredDataType = null;
            Value? declaredNumericPrecision = null;
            Value? declaredNumericScale = null;
            string? geometryType = null;
            Value? geometrySrid = null;
            switch (type)
            {
                case Value.Char:
                case Value.Varchar:
                case Value.Clob:
                case Value.VarcharIgnorecase:
                    hasCharsetAndCollation = true;
                    goto case Value.Binary;
                case Value.Binary:
                case Value.Varbinary:
                case Value.Blob:
                case Value.JavaObject:
                case Value.Json:
                    characterPrecision = ValueBigint.Get(typeInfo.GetPrecision());
                    break;
                case Value.Tinyint:
                case Value.Smallint:
                case Value.Integer:
                case Value.Bigint:
                    numericPrecision = ValueInteger.Get(ToInt(typeInfo.GetPrecision()));
                    numericScale = ValueInteger.Get(0);
                    numericPrecisionRadix = ValueInteger.Get(2);
                    declaredDataType = dataType;
                    break;
                case Value.Numeric:
                    {
                        numericPrecision = ValueInteger.Get(ToInt(typeInfo.GetPrecision()));
                        numericScale = ValueInteger.Get(typeInfo.GetScale());
                        numericPrecisionRadix = ValueInteger.Get(10);
                        declaredDataType = typeInfo.GetExtTypeInfo() is not null ? "DECIMAL" : "NUMERIC";
                        if (typeInfo.GetDeclaredPrecision() >= 0L)
                        {
                            declaredNumericPrecision = numericPrecision;
                        }

                        if (typeInfo.GetDeclaredScale() >= 0)
                        {
                            declaredNumericScale = numericScale;
                        }

                        break;
                    }

                case Value.Real:
                case Value.Double:
                    {
                        numericPrecision = ValueInteger.Get(ToInt(typeInfo.GetPrecision()));
                        numericPrecisionRadix = ValueInteger.Get(2);
                        long declaredPrecision = typeInfo.GetDeclaredPrecision();
                        if (declaredPrecision >= 0)
                        {
                            declaredDataType = "FLOAT";
                            if (declaredPrecision > 0)
                            {
                                declaredNumericPrecision = ValueInteger.Get((int)declaredPrecision);
                            }
                        }
                        else
                        {
                            declaredDataType = dataType;
                        }

                        break;
                    }

                case Value.Decfloat:
                    numericPrecision = ValueInteger.Get(ToInt(typeInfo.GetPrecision()));
                    numericPrecisionRadix = ValueInteger.Get(10);
                    declaredDataType = dataType;
                    if (typeInfo.GetDeclaredPrecision() >= 0L)
                    {
                        declaredNumericPrecision = numericPrecision;
                    }

                    break;
                case Value.IntervalYear:
                case Value.IntervalMonth:
                case Value.IntervalDay:
                case Value.IntervalHour:
                case Value.IntervalMinute:
                case Value.IntervalSecond:
                case Value.IntervalYearToMonth:
                case Value.IntervalDayToHour:
                case Value.IntervalDayToMinute:
                case Value.IntervalDayToSecond:
                case Value.IntervalHourToMinute:
                case Value.IntervalHourToSecond:
                case Value.IntervalMinuteToSecond:
                    intervalType = IntervalQualifiers.Name(
                        IntervalQualifiers.ValueOf(type - Value.IntervalYear));
                    dataType = "INTERVAL";
                    intervalPrecision = ValueInteger.Get(ToInt(typeInfo.GetPrecision()));
                    goto case Value.Date;
                case Value.Date:
                case Value.Time:
                case Value.TimeTz:
                case Value.Timestamp:
                case Value.TimestampTz:
                    datetimePrecision = ValueInteger.Get(typeInfo.GetScale());
                    break;
                case Value.Geometry:
                    {
                        if (typeInfo.GetExtTypeInfo() is ExtTypeInfoGeometry extTypeInfo)
                        {
                            int typeCode = extTypeInfo.GetTypeAndDimensionSystem();
                            if (typeCode != 0)
                            {
                                geometryType = Util.Geometry.EWKTUtils
                                    .FormatGeometryTypeAndDimensionSystem(new StringBuilder(), typeCode)
                                    .ToString();
                            }

                            int? srid = extTypeInfo.GetSrid();
                            if (srid is not null)
                            {
                                geometrySrid = ValueInteger.Get(srid.Value);
                            }
                        }

                        break;
                    }

                case Value.Array:
                    maximumCardinality = ValueInteger.Get(ToInt(typeInfo.GetPrecision()));
                    break;
            }

            return new DataTypeInformation(
                dataType,
                characterPrecision,
                numericPrecision,
                numericPrecisionRadix,
                numericScale,
                datetimePrecision,
                intervalPrecision,
                intervalType is null ? null : ValueVarchar.Get(intervalType),
                maximumCardinality,
                hasCharsetAndCollation,
                declaredDataType,
                declaredNumericPrecision,
                declaredNumericScale,
                geometryType,
                geometrySrid);
        }

        /// <summary>
        /// The precision as an int, which is what the views show.
        /// </summary>
        /// <param name="value">the value</param>
        /// <returns>the value as an int</returns>
        private static int ToInt(long value) => (int)value;
    }
}
