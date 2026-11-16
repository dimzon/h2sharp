/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0 (see LICENSE-H2.txt).
 *
 * Ported from org.h2.mode.PgCatalogTable.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The tables of the PG_CATALOG schema. Rows are generated from the database metadata when
 * they are read, as in the reference.
 */
using System.Collections.Generic;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Server.Pg;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Modes;

/// <summary>
/// A metadata table of the PostgreSQL compatibility schema.
/// </summary>
public sealed class PgCatalogTable : MetaTable
{
    private const int PgAm = 0;
    private const int PgAttrdef = PgAm + 1;
    private const int PgAttribute = PgAttrdef + 1;
    private const int PgAuthid = PgAttribute + 1;
    private const int PgClass = PgAuthid + 1;
    private const int PgConstraint = PgClass + 1;
    private const int PgDatabase = PgConstraint + 1;
    private const int PgDescription = PgDatabase + 1;
    private const int PgGroup = PgDescription + 1;
    private const int PgIndex = PgGroup + 1;
    private const int PgInherits = PgIndex + 1;
    private const int PgNamespace = PgInherits + 1;
    private const int PgProc = PgNamespace + 1;
    private const int PgRoles = PgProc + 1;
    private const int PgSettings = PgRoles + 1;
    private const int PgTablespace = PgSettings + 1;
    private const int PgTrigger = PgTablespace + 1;
    private const int PgType = PgTrigger + 1;
    private const int PgUser = PgType + 1;

    /// <summary>
    /// The number of meta table types, 0 to <see cref="MetaTableTypeCount"/> - 1.
    /// </summary>
    public const int MetaTableTypeCount = PgUser + 1;

    private static readonly object[][] PgExtraTypes =
    [
        [18, "char", 1, 0],
        [19, "name", 64, 18],
        [22, "int2vector", -1, 21],
        [24, "regproc", 4, 0],
        [PgServer.PgTypeInt2Array, "_int2", -1, PgServer.PgTypeInt2],
        [PgServer.PgTypeInt4Array, "_int4", -1, PgServer.PgTypeInt4],
        [PgServer.PgTypeVarcharArray, "_varchar", -1, PgServer.PgTypeVarchar],
        [2205, "regclass", 4, 0],
    ];

    /// <summary>
    /// Creates a PostgreSQL catalogue table.
    /// </summary>
    /// <param name="schema">the schema</param>
    /// <param name="id">the object id</param>
    /// <param name="type">the meta table type</param>
    public PgCatalogTable(Schema schema, int id, int type)
        : base(schema, id, type)
    {
        Column[] columns;
        switch (type)
        {
            case PgAm:
                SetMetaTableName("PG_AM");
                columns = [Column("OID", TypeInfo.TypeInteger), Column("AMNAME")];
                break;
            case PgAttrdef:
                SetMetaTableName("PG_ATTRDEF");
                columns =
                [
                    Column("OID", TypeInfo.TypeInteger), Column("ADSRC", TypeInfo.TypeInteger),
                    Column("ADRELID", TypeInfo.TypeInteger), Column("ADNUM", TypeInfo.TypeInteger),
                    Column("ADBIN"),
                ];
                break;
            case PgAttribute:
                SetMetaTableName("PG_ATTRIBUTE");
                columns =
                [
                    Column("OID", TypeInfo.TypeInteger), Column("ATTRELID", TypeInfo.TypeInteger),
                    Column("ATTNAME"), Column("ATTTYPID", TypeInfo.TypeInteger),
                    Column("ATTLEN", TypeInfo.TypeInteger), Column("ATTNUM", TypeInfo.TypeInteger),
                    Column("ATTTYPMOD", TypeInfo.TypeInteger), Column("ATTNOTNULL", TypeInfo.TypeBoolean),
                    Column("ATTISDROPPED", TypeInfo.TypeBoolean), Column("ATTHASDEF", TypeInfo.TypeBoolean),
                ];
                break;
            case PgAuthid:
                SetMetaTableName("PG_AUTHID");
                columns =
                [
                    Column("OID", TypeInfo.TypeInteger), Column("ROLNAME"),
                    Column("ROLSUPER", TypeInfo.TypeBoolean), Column("ROLINHERIT", TypeInfo.TypeBoolean),
                    Column("ROLCREATEROLE", TypeInfo.TypeBoolean), Column("ROLCREATEDB", TypeInfo.TypeBoolean),
                    Column("ROLCATUPDATE", TypeInfo.TypeBoolean), Column("ROLCANLOGIN", TypeInfo.TypeBoolean),
                    Column("ROLCONNLIMIT", TypeInfo.TypeBoolean), Column("ROLPASSWORD", TypeInfo.TypeBoolean),
                    Column("ROLVALIDUNTIL", TypeInfo.TypeTimestampTz), ArrayColumn("ROLCONFIG"),
                ];
                break;
            case PgClass:
                SetMetaTableName("PG_CLASS");
                columns =
                [
                    Column("OID", TypeInfo.TypeInteger), Column("RELNAME"),
                    Column("RELNAMESPACE", TypeInfo.TypeInteger), Column("RELKIND", TypeInfo.TypeChar),
                    Column("RELAM", TypeInfo.TypeInteger), Column("RELTUPLES", TypeInfo.TypeDouble),
                    Column("RELTABLESPACE", TypeInfo.TypeInteger), Column("RELPAGES", TypeInfo.TypeInteger),
                    Column("RELHASINDEX", TypeInfo.TypeBoolean), Column("RELHASRULES", TypeInfo.TypeBoolean),
                    Column("RELHASOIDS", TypeInfo.TypeBoolean), Column("RELCHECKS", TypeInfo.TypeSmallint),
                    Column("RELTRIGGERS", TypeInfo.TypeInteger),
                ];
                break;
            case PgConstraint:
                SetMetaTableName("PG_CONSTRAINT");
                columns =
                [
                    Column("OID", TypeInfo.TypeInteger), Column("CONNAME"), Column("CONTYPE"),
                    Column("CONRELID", TypeInfo.TypeInteger), Column("CONFRELID", TypeInfo.TypeInteger),
                    ArrayColumn("CONKEY", TypeInfo.TypeSmallint),
                ];
                break;
            case PgDatabase:
                SetMetaTableName("PG_DATABASE");
                columns =
                [
                    Column("OID", TypeInfo.TypeInteger), Column("DATNAME"),
                    Column("ENCODING", TypeInfo.TypeInteger), Column("DATLASTSYSOID", TypeInfo.TypeInteger),
                    Column("DATALLOWCONN", TypeInfo.TypeBoolean), ArrayColumn("DATCONFIG"),
                    ArrayColumn("DATACL"), Column("DATDBA", TypeInfo.TypeInteger),
                    Column("DATTABLESPACE", TypeInfo.TypeInteger),
                ];
                break;
            case PgDescription:
                SetMetaTableName("PG_DESCRIPTION");
                columns =
                [
                    Column("OBJOID", TypeInfo.TypeInteger), Column("OBJSUBID", TypeInfo.TypeInteger),
                    Column("CLASSOID", TypeInfo.TypeInteger), Column("DESCRIPTION"),
                ];
                break;
            case PgGroup:
                SetMetaTableName("PG_GROUP");
                columns = [Column("OID", TypeInfo.TypeInteger), Column("GRONAME")];
                break;
            case PgIndex:
                SetMetaTableName("PG_INDEX");
                columns =
                [
                    Column("OID", TypeInfo.TypeInteger), Column("INDEXRELID", TypeInfo.TypeInteger),
                    Column("INDRELID", TypeInfo.TypeInteger), Column("INDISCLUSTERED", TypeInfo.TypeBoolean),
                    Column("INDISUNIQUE", TypeInfo.TypeBoolean), Column("INDISPRIMARY", TypeInfo.TypeBoolean),
                    Column("INDEXPRS"), ArrayColumn("INDKEY", TypeInfo.TypeInteger), Column("INDPRED"),
                ];
                break;
            case PgInherits:
                SetMetaTableName("PG_INHERITS");
                columns =
                [
                    Column("INHRELID", TypeInfo.TypeInteger), Column("INHPARENT", TypeInfo.TypeInteger),
                    Column("INHSEQNO", TypeInfo.TypeInteger),
                ];
                break;
            case PgNamespace:
                SetMetaTableName("PG_NAMESPACE");
                columns = [Column("ID", TypeInfo.TypeInteger), Column("NSPNAME")];
                break;
            case PgProc:
                SetMetaTableName("PG_PROC");
                columns =
                [
                    Column("OID", TypeInfo.TypeInteger), Column("PRONAME"),
                    Column("PRORETTYPE", TypeInfo.TypeInteger),
                    ArrayColumn("PROARGTYPES", TypeInfo.TypeInteger), Column("PRONAMESPACE", TypeInfo.TypeInteger),
                ];
                break;
            case PgRoles:
                SetMetaTableName("PG_ROLES");
                columns =
                [
                    Column("OID", TypeInfo.TypeInteger), Column("ROLNAME"),
                    Column("ROLSUPER", TypeInfo.TypeChar), Column("ROLCREATEROLE", TypeInfo.TypeChar),
                    Column("ROLCREATEDB", TypeInfo.TypeChar),
                ];
                break;
            case PgSettings:
                SetMetaTableName("PG_SETTINGS");
                columns =
                [
                    Column("OID", TypeInfo.TypeInteger), Column("NAME"), Column("SETTING"),
                ];
                break;
            case PgTablespace:
                SetMetaTableName("PG_TABLESPACE");
                columns =
                [
                    Column("OID", TypeInfo.TypeInteger), Column("SPCNAME"), Column("SPCLOCATION"),
                    Column("SPCOWNER", TypeInfo.TypeInteger), ArrayColumn("SPCACL"),
                ];
                break;
            case PgTrigger:
                SetMetaTableName("PG_TRIGGER");
                columns =
                [
                    Column("OID", TypeInfo.TypeInteger), Column("TGCONSTRRELID", TypeInfo.TypeInteger),
                    Column("TGFOID", TypeInfo.TypeInteger), Column("TGARGS", TypeInfo.TypeInteger),
                    Column("TGNARGS", TypeInfo.TypeInteger), Column("TGDEFERRABLE", TypeInfo.TypeBoolean),
                    Column("TGINITDEFERRED", TypeInfo.TypeBoolean), Column("TGCONSTRNAME"),
                    Column("TGRELID", TypeInfo.TypeInteger),
                ];
                break;
            case PgType:
                SetMetaTableName("PG_TYPE");
                columns =
                [
                    Column("OID", TypeInfo.TypeInteger), Column("TYPNAME"),
                    Column("TYPNAMESPACE", TypeInfo.TypeInteger), Column("TYPLEN", TypeInfo.TypeInteger),
                    Column("TYPTYPE"), Column("TYPDELIM"), Column("TYPRELID", TypeInfo.TypeInteger),
                    Column("TYPELEM", TypeInfo.TypeInteger), Column("TYPBASETYPE", TypeInfo.TypeInteger),
                    Column("TYPTYPMOD", TypeInfo.TypeInteger), Column("TYPNOTNULL", TypeInfo.TypeBoolean),
                    Column("TYPINPUT"),
                ];
                break;
            case PgUser:
                SetMetaTableName("PG_USER");
                columns =
                [
                    Column("OID", TypeInfo.TypeInteger), Column("USENAME"),
                    Column("USECREATEDB", TypeInfo.TypeBoolean), Column("USESUPER", TypeInfo.TypeBoolean),
                ];
                break;
            default:
                throw DbException.GetInternalError("type=" + type);
        }

        SetColumns(columns);
        SetIndexes();
        indexColumn = -1;
        metaIndex = null;
    }

    /// <inheritdoc/>
    public override List<Row> GenerateRows(SessionLocal session, SearchRow? first, SearchRow? last)
    {
        List<Row> rows = [];
        string catalog = GetSchema()!.GetDatabase().GetShortName();
        switch (Type)
        {
            case PgAm:
                {
                    string[] am = ["btree", "hash"];
                    for (int i = 0; i < am.Length; i++)
                    {
                        Add(session, rows, ValueInteger.Get(i), am[i]);
                    }

                    break;
                }
            case PgAttrdef:
            case PgAuthid:
            case PgGroup:
            case PgIndex:
            case PgInherits:
            case PgProc:
            case PgTrigger:
                // The reference deliberately leaves these tables empty for MS Access or because
                // the objects they describe are not part of this database.
                break;
            case PgAttribute:
                foreach (Table table in GetAllTables(session, null, null))
                {
                    pgAttribute(session, rows, table);
                }

                break;
            case PgClass:
                foreach (Table table in GetAllTables(session, null, null))
                {
                    pgClass(session, rows, table);
                }

                break;
            case PgConstraint:
                pgConstraint(session, rows);
                break;
            case PgDatabase:
                // The reference chooses the smallest admin user id; the port's system user is
                // the only principal (D-004) and has id 0.
                Add(session, rows, ValueInteger.Get(100_001), catalog, ValueInteger.Get(6),
                    ValueInteger.Get(100_000), ValueBoolean.True, null, null, ValueInteger.Get(0),
                    ValueInteger.Get(0));
                break;
            case PgDescription:
                Add(session, rows, ValueInteger.Get(0), ValueInteger.Get(0), ValueInteger.Get(-1), catalog);
                break;
            case PgNamespace:
                foreach (Schema schema in GetSchema()!.GetDatabase().GetSchemas())
                {
                    Add(session, rows, ValueInteger.Get(schema.GetId()), schema.GetName());
                }

                break;
            case PgRoles:
            case PgUser:
                // D-004: the port has no users or roles, so these tables have no rows.
                break;
            case PgSettings:
                {
                    string[][] settings =
                    [
                        ["autovacuum", "on"],
                        ["stats_start_collector", "on"],
                        ["stats_row_level", "on"],
                    ];
                    for (int i = 0; i < settings.Length; i++)
                    {
                        Add(session, rows, ValueInteger.Get(i), settings[i][0], settings[i][1]);
                    }

                    break;
                }
            case PgTablespace:
                Add(session, rows, ValueInteger.Get(0), "main", "?", ValueInteger.Get(0), null);
                break;
            case PgType:
                {
                    HashSet<int> types = [];
                    for (int i = 1; i < Value.TypeCount; i++)
                    {
                        DataType t = DataType.GetDataType(i);
                        if (t.Type == Value.Array)
                        {
                            continue;
                        }

                        int pgType = PgServer.ConvertType(TypeInfo.GetTypeInfo(t.Type));
                        if (pgType == PgServer.PgTypeUnknown || !types.Add(pgType))
                        {
                            continue;
                        }

                        AddPgType(session, rows, pgType, Value.GetTypeName(t.Type), -1, 0);
                    }

                    foreach (object[] pgType in PgExtraTypes)
                    {
                        AddPgType(session, rows, (int)pgType[0], (string)pgType[1],
                            (int)pgType[2], (int)pgType[3]);
                    }

                    break;
                }
            default:
                throw DbException.GetInternalError("type=" + Type);
        }

        return rows;
    }

    /// <inheritdoc/>
    public override long GetMaxDataModificationId()
        => GetSchema()!.GetDatabase().GetModificationDataId();

    private void AddPgType(SessionLocal session, List<Row> rows, int oid, string name, int length, int element)
    {
        Add(session, rows, ValueInteger.Get(oid), name,
            ValueInteger.Get(Constants.PgCatalogSchemaId), ValueInteger.Get(length),
            "b", ",", ValueInteger.Get(0), ValueInteger.Get(element), ValueInteger.Get(0),
            ValueInteger.Get(-1), ValueBoolean.False, null);
    }

    private Column ArrayColumn(string name, TypeInfo? component = null)
        => Column(name, TypeInfo.GetTypeInfo(Value.Array, -1L, 0, component ?? TypeInfo.TypeVarchar));

    private void pgAttribute(SessionLocal session, List<Row> rows, Table table)
    {
        Column[] columns = table.GetColumns();
        int tableId = table.GetId();
        for (int i = 0; i < columns.Length;)
        {
            Column column = columns[i++];
            AddAttribute(session, rows, tableId * 10_000 + i, tableId, column, i);
        }

        foreach (Index index in table.GetIndexes())
        {
            if (index.GetCreateSql() is null)
            {
                continue;
            }

            // The reference reads Index.getColumns(); the port's Index exposes index columns,
            // so unwrap them to the table columns the catalogue reports.
            IndexColumn[] indexColumns = index.GetColumns();
            columns = new Column[indexColumns.Length];
            for (int i = 0; i < indexColumns.Length; i++)
            {
                columns[i] = indexColumns[i].GetColumn();
            }

            for (int i = 0; i < columns.Length;)
            {
                Column column = columns[i++];
                int indexId = index.GetId();
                AddAttribute(session, rows, 1_000_000 * indexId + tableId * 10_000 + i, indexId, column, i);
            }
        }
    }

    private void pgClass(SessionLocal session, List<Row> rows, Table table)
    {
        List<TriggerObject>? triggers = table.GetTriggers();
        AddClass(session, rows, table.GetId(), table.GetTableName(), table.GetSchema()!.GetId(),
            table.IsView() ? "v" : "r", false, triggers?.Count ?? 0);
        foreach (Index index in table.GetIndexes())
        {
            if (index.GetCreateSql() is null)
            {
                continue;
            }

            AddClass(session, rows, index.GetId(), index.GetName()!, index.GetSchema()!.GetId(),
                "i", true, 0);
        }
    }

    private void pgConstraint(SessionLocal session, List<Row> rows)
    {
        foreach (Constraint constraint in GetAllConstraints(session))
        {
            if (constraint.GetConstraintType() == Constraint.Type.DOMAIN)
            {
                continue;
            }

            Constraint.Type constraintType = constraint.GetConstraintType();
            Table table = constraint.GetTable();
            List<ValueSmallint> conkey = [];
            foreach (Column column in constraint.GetReferencedColumns(table))
            {
                conkey.Add(ValueSmallint.Get((short)(column.GetColumnId() + 1)));
            }

            Table? refTable = constraint.GetRefTable();
            Add(session, rows, ValueInteger.Get(constraint.GetId()), constraint.GetName(),
                StringUtils.ToLowerEnglish(ConstraintTypeRules.GetSqlName(constraintType)[..1]),
                ValueInteger.Get(table.GetId()),
                ValueInteger.Get(refTable is not null && !ReferenceEquals(refTable, table) ? table.GetId() : 0),
                ValueArray.Get(TypeInfo.TypeSmallint, [.. conkey], null));
        }
    }

    private void AddAttribute(
        SessionLocal session, List<Row> rows, int id, int relId, Column column, int ordinal)
    {
        long precision = column.GetPrecision();
        Add(session, rows, ValueInteger.Get(id), ValueInteger.Get(relId), column.GetName(),
            ValueInteger.Get(PgServer.ConvertType(column.GetTypeInfo())),
            ValueInteger.Get(precision > 255 ? -1 : (int)precision), ValueInteger.Get(ordinal),
            ValueInteger.Get(-1),
            ValueBoolean.Get(column.IsNullable() == Tables.Column.NotNullable),
            ValueBoolean.False, ValueBoolean.False);
    }

    private void AddClass(
        SessionLocal session, List<Row> rows, int id, string name, int schema, string kind,
        bool index, int triggers)
    {
        Add(session, rows, ValueInteger.Get(id), name, ValueInteger.Get(schema), kind,
            ValueInteger.Get(0), ValueDouble.Get(0d), ValueInteger.Get(0), ValueInteger.Get(0),
            ValueBoolean.Get(index), ValueBoolean.False, ValueBoolean.False,
            ValueSmallint.Get(0), ValueInteger.Get(triggers));
    }
}
