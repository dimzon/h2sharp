/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.command.dml.ScriptCommand, H2 2.5.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * SCRIPT.
 *
 * Generates the SQL that recreates the database. The reference can also write to a file,
 * encrypt, compress, and filter schemas/tables; only the in-memory result is ported here.
 * Settings and users/roles are not emitted: D-004 has no users or roles and settings are not
 * ported. Asking for either through the options still builds the same script, because the
 * default switches are read from the grammar exactly as in the reference.
 */
using System.Collections.Generic;
using System.Text;
using H2Sharp.Api;
using H2Sharp.Command.Query;
using H2Sharp.Constraints;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Indexes;
using H2Sharp.Message;
using H2Sharp.Result;
using H2Sharp.Schemas;
using H2Sharp.Tables;
using H2Sharp.Util;
using H2Sharp.Values;

namespace H2Sharp.Command.Dml;

/// <summary>The SCRIPT statement.</summary>
public sealed class ScriptCommand : Prepared
{
    private bool data = true;

    private bool passwords = true;

    private bool settings = true;

    private bool version = true;

    private bool drop;

    private bool simple;

    private bool withColumns;

    /// <summary>Creates the statement.</summary>
    public ScriptCommand(SessionLocal session)
        : base(session)
    {
    }

    /// <summary>Sets whether rows are written.</summary>
    public void SetData(bool value) => data = value;

    /// <summary>Sets whether passwords are included.</summary>
    public void SetPasswords(bool value) => passwords = value;

    /// <summary>Sets whether settings are included.</summary>
    public void SetSettings(bool value) => settings = value;

    /// <summary>Sets whether the version header is included.</summary>
    public void SetVersion(bool value) => version = value;

    /// <summary>Sets whether DROP statements are emitted.</summary>
    public void SetDrop(bool value) => drop = value;

    /// <summary>Sets one-line INSERT statements.</summary>
    public void SetSimple(bool value) => simple = value;

    /// <summary>Sets whether INSERT lists columns explicitly.</summary>
    public void SetWithColumns(bool value) => withColumns = value;

    /// <inheritdoc/>
    public override bool IsQuery() => true;

    /// <inheritdoc/>
    public override bool IsTransactional() => false;

    /// <inheritdoc/>
    public override bool IsReadOnly() => true;

    /// <inheritdoc/>
    public override int GetCommandType() => CommandInterface.Script;

    /// <inheritdoc/>
    public override ResultInterface QueryMeta()
    {
        LocalResult result = CreateResult();
        result.Done();
        return result;
    }

    private LocalResult CreateResult()
        => new(GetSession(),
            [new ExpressionColumn(GetSession().GetDatabase(), new Column("SCRIPT", TypeInfo.TypeVarchar))], 1, 1);

    /// <inheritdoc/>
    public override ResultInterface Run(long maxrows)
    {
        LocalResult result = CreateResult();
        List<string> lines = [];
        void Add(string? sql)
        {
            if (!string.IsNullOrEmpty(sql))
            {
                lines.Add(sql + ";");
            }
        }

        Database db = GetSession().GetDatabase();
        if (drop)
        {
            throw new TODOException("ScriptCommand: the DROP option is not ported");
        }

        if (version)
        {
            Add("-- H2 " + Constants.Version);
        }

        List<Schema> schemas = [];
        foreach (Schema schema in db.GetSchemas())
        {
            if (schema.IsSystem()) continue;
            schemas.Add(schema);
            if (!ReferenceEquals(schema, db.GetMainSchema()))
            {
                Add(schema.GetCreateSql());
            }
        }

        foreach (Schema schema in schemas)
        {
            List<Domain> domains = schema.GetAllDomains();
            domains.Sort(static (a, b) => string.CompareOrdinal(a.GetName(), b.GetName()));
            foreach (Domain domain in domains)
            {
                Add(domain.GetCreateSql());
            }
        }

        List<Table> tables = [];
        foreach (Schema schema in schemas)
        {
            tables.AddRange(schema.GetAllTablesAndViews(GetSession()));
        }

        tables.Sort(static (a, b) => a.GetId().CompareTo(b.GetId()));

        foreach (Table table in tables)
        {
            string? createSql = table.GetCreateSql();
            if (createSql is null) continue;
            Add(createSql);
            foreach (Constraint constraint in table.GetConstraints())
            {
                if (constraint.GetConstraintType() == Constraint.Type.PRIMARY_KEY)
                {
                    Add(constraint.GetCreateSqlWithoutIndexes());
                }
            }

            if (table.GetTableType() == Table.TABLE)
            {
                if (table.CanGetRowCount(GetSession()))
                {
                    StringBuilder builder = new StringBuilder("-- ")
                        .Append(table.GetRowCountApproximation(GetSession()))
                        .Append(" +/- SELECT COUNT(*) FROM ");
                    _ = table.GetSql(builder, HasSql.TraceSqlFlags);
                    Add(builder.ToString());
                }

                if (data)
                {
                    Add(GenerateInsertValues(table));
                }
            }

            foreach (Index index in table.GetIndexes())
            {
                if (index.GetIndexType() is { } indexType && !indexType.GetBelongsToConstraint())
                {
                    Add(index.GetCreateSql());
                }
            }
        }

        foreach (Schema schema in schemas)
        {
            foreach (Constraint constraint in schema.GetAllConstraints())
            {
                if (constraint.GetConstraintType() != Constraint.Type.PRIMARY_KEY)
                {
                    Add(constraint.GetCreateSqlWithoutIndexes());
                }
            }
        }

        foreach (string line in lines)
        {
            result.AddRow(ValueVarchar.Get(line));
        }

        result.Done();
        return result;
    }

    private string GenerateInsertValues(Table table)
    {
        StringBuilder builder = new("INSERT INTO ");
        _ = table.GetSql(builder, HasSql.DefaultSqlFlags);

        Column[] columns = table.GetColumns();
        bool withGenerated = false, withGeneratedAlwaysAsIdentity = false;
        foreach (Column column in columns)
        {
            if (!column.IsGenerated()) continue;
            if (column.IsIdentity()) withGeneratedAlwaysAsIdentity = true;
            else withGenerated = true;
        }

        if (withGenerated || withGeneratedAlwaysAsIdentity || withColumns)
        {
            _ = builder.Append('(');
            bool needComma = false;
            foreach (Column column in columns)
            {
                if (column.IsGenerated()) continue;
                if (needComma) _ = builder.Append(", ");
                needComma = true;
                _ = column.GetSql(builder, HasSql.DefaultSqlFlags);
            }

            _ = builder.Append(')');
            if (withGeneratedAlwaysAsIdentity)
            {
                _ = builder.Append(" OVERRIDING SYSTEM VALUE");
            }
        }

        _ = builder.Append(" VALUES");
        if (!simple)
        {
            _ = builder.Append('\n');
        }

        Index? scan = table.GetScanIndex(GetSession());
        if (scan is null)
        {
            throw DbException.GetInternalError("SCRIPT: table has no scan index");
        }

        Cursor cursor = scan.GetCursor(GetSession(), null, null, false)
            ?? throw DbException.GetInternalError("SCRIPT: table has no cursor");
        bool firstRow = true;
        while (cursor.Next())
        {
            Row? row = cursor.Get()
                ?? throw DbException.GetInternalError("SCRIPT: cursor returned no row");
            if (!firstRow)
            {
                _ = builder.Append(",\n(");
            }
            else
            {
                _ = builder.Append('(');
                firstRow = false;
            }

            bool needComma = false;
            for (int i = 0; i < columns.Length; i++)
            {
                if (columns[i].IsGenerated()) continue;
                if (needComma) _ = builder.Append(", ");
                needComma = true;
                _ = row.GetValue(i)!.GetSql(builder, HasSql.NoCasts);
            }

            _ = builder.Append(')');
        }

        if (firstRow)
        {
            // A table with no rows still gets its INSERT statement? The reference writes none.
            return string.Empty;
        }

        return builder.ToString();
    }
}
